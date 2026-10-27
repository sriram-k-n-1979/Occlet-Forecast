using System.Globalization;
using System.Text;
using System.Text.Json;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Util;

namespace Occlet.Core.Engine;

/// <summary>
/// Problem 1's output: the frozen "if nothing changes" forecast for one member.
/// Everything random (everyday spending, amount draws, alert luck) is drawn ONCE here and reused
/// by every scenario, so a scenario's difference from the baseline is caused only by its levers.
/// <see cref="FrozenHash"/> fingerprints those inputs; the scenario engine refuses to run if they change.
/// </summary>
public sealed class Baseline
{
    public required MemberHistory History { get; init; }
    public MemberProfile Profile => History.Profile;
    public required DateOnly Origin { get; init; }
    public required int Horizon { get; init; }
    public required string Forecaster { get; init; }
    public required string ForecasterVersion { get; init; }
    public required int Seed { get; init; }
    public required double StartBalance { get; init; }
    public required double StartSavings { get; init; }
    public required double Ads { get; init; }
    public required IReadOnlyList<RecurringItem> Recurring { get; init; }
    public required IReadOnlyList<FutureEvent> Events { get; init; }
    public required EverydaySeries Series { get; init; }
    public required double[][] Everyday { get; init; }
    public required double[][] AlertU { get; init; }
    public required double[] LeverU { get; init; }
    public required double[][] EventZ { get; init; }
    public required IReadOnlyDictionary<string, int> EventKeyIndex { get; init; }
    public required double[][] ItemLevel { get; init; }
    public required IReadOnlyDictionary<string, int> ItemIndex { get; init; }
    public required ProductRules Rules { get; init; }
    public required Behaviour Behaviour { get; init; }
    public string FrozenHash { get; private set; } = "";
    public SlackSummary Summary { get; private set; } = null!;
    public ReplayResult Result { get; private set; } = null!;
    public DateOnly DateAt(int day) => Origin.AddDays(day + 1);
    public int Paths => Everyday.Length;

    internal void Freeze(ReplayResult r, SlackSummary s) { Result = r; Summary = s; FrozenHash = ComputeHash(); }

    public string ComputeHash()
    {
        using var ms = new MemoryStream();
        void Add(double[] a) { var b = new byte[a.Length * 8]; Buffer.BlockCopy(a, 0, b, 0, b.Length); ms.Write(b); }
        foreach (var row in Everyday) Add(row);
        foreach (var row in AlertU) Add(row);
        Add(LeverU);
        foreach (var row in EventZ) Add(row);
        foreach (var row in ItemLevel) Add(row);
        ms.Write(Encoding.UTF8.GetBytes(string.Join(";", Events.Select(e => $"{e.Key}:{e.Day}:{e.Amount.ToString("R", CultureInfo.InvariantCulture)}"))));
        ms.Write(Encoding.UTF8.GetBytes(Rules.ToString() + Behaviour));
        return Stats.Sha256Hex(ms.ToArray());
    }
}

public static class BaselineBuilder
{
    /// <summary>What a forecaster may see for this member on the origin date: history up to it, recurring items, projected events.</summary>
    public static ForecastContext Context(MemberHistory member, DateOnly origin, int horizon = Settings.Horizon)
    {
        var h = member.UpTo(origin);                                   // only what was known on the origin date
        var recurring = RecurringDetector.Detect(h);
        var events = RecurringDetector.Project(recurring, origin, horizon);
        var series = EverydaySeries.Build(h, recurring);
        return new ForecastContext(h, recurring, events, series, origin, h.CheckingEod(origin), SpendStats.AverageDailySpend(h));
    }

    /// <summary>Builds and freezes the baseline for one member as of <paramref name="origin"/>.</summary>
    public static Baseline Build(MemberHistory member, DateOnly origin, ISpendForecaster forecaster,
                                 int horizon = Settings.Horizon, int? paths = null, int? seed = null)
    {
        int P = paths ?? Settings.Paths;
        int baseSeed = seed ?? Settings.Seed;
        var ctx = Context(member, origin, horizon);
        var (h, recurring, events, series, ads) = (ctx.History, ctx.Recurring, ctx.Events, ctx.Series, ctx.AverageDailySpend);

        var rng = new Random(Stats.StableSeed(baseSeed, member.Id, origin.DayNumber, forecaster.Name));
        var everyday = forecaster.SamplePaths(ctx, horizon, P, rng);  // the uncertain part
        var alertU = Enumerable.Range(0, P).Select(_ => Enumerable.Range(0, horizon).Select(_ => rng.NextDouble()).ToArray()).ToArray();
        var leverU = Enumerable.Range(0, P).Select(_ => rng.NextDouble()).ToArray();
        var keyIndex = events.Select((e, i) => (e.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var eventZ = Enumerable.Range(0, P).Select(_ => events.Select(_ => Stats.Gaussian(rng)).ToArray()).ToArray();
        // parameter uncertainty: in each future, every variable item (e.g. gig pay) gets one shared level shock
        var levelCv = recurring.ToDictionary(r => r.Key, r => r.LevelCv);
        var itemLevel = Enumerable.Range(0, P).Select(_ => recurring.Select(r => Math.Exp(r.LevelCv * Stats.Gaussian(rng) - r.LevelCv * r.LevelCv / 2)).ToArray()).ToArray();
        var itemIndex = recurring.Select((r, i) => (r.Key, i)).ToDictionary(x => x.Key, x => x.i);

        var b = new Baseline
        {
            History = h, Origin = origin, Horizon = horizon, Forecaster = forecaster.Name, ForecasterVersion = forecaster.Version,
            Seed = baseSeed, StartBalance = ctx.StartBalance, StartSavings = h.SavingsEod(origin), Ads = ads,
            Recurring = recurring, Events = events, Series = series, Everyday = everyday,
            AlertU = alertU, LeverU = leverU, EventZ = eventZ, EventKeyIndex = keyIndex, ItemLevel = itemLevel, ItemIndex = itemIndex,
            Rules = ProductRules.From(h.Profile),
            // an alert the member ALREADY has is part of their history — not simulated again (no double counting)
            Behaviour = Behaviour.None,
        };
        var result = Replay.Run(new Replay.Input(b.StartBalance, b.StartSavings, events, horizon, everyday, alertU, leverU,
                                                 eventZ, keyIndex, b.Rules, b.Behaviour, itemLevel, itemIndex));
        b.Freeze(result, SlackSummary.From(result, ads));
        return b;
    }
}

public static class Explainer
{
    /// <summary>Member-facing sentence naming the dated events behind the lowest point of the likely path.</summary>
    public static string Dip(Baseline b)
    {
        var s = b.Summary;
        int low = s.LowestQ50Day;
        var bills = b.Events.Where(e => e.Day <= low && e.Day >= low - 6 && e.Amount < 0)
                            .Select(e => $"{e.Category.ToLowerInvariant()} ${-e.Amount:N0} on {e.Date:MMM dd}").ToList();
        var nextPay = b.Events.FirstOrDefault(e => e.Day > low && e.IsPayday);
        string recover = nextPay != null ? $" Payday on {nextPay.Date:MMM dd} brings it back." : "";
        return $"Your balance is likely lowest on {b.DateAt(low):MMM dd} (about ${s.Q50[low]:N0}, {s.LowestQ50Slack:0.0} days of spending). " +
               $"What pulls it down: {(bills.Count > 0 ? string.Join(", ", bills) : "everyday spending")}.{recover}";
    }
}

public static class ArtifactWriter
{
    /// <summary>Writes the immutable, versioned baseline artifact that Phase 2 consumes (Occlet doc §1.3).</summary>
    public static string Write(Baseline b, string path, IReadOnlyList<string> featureSchema)
    {
        var s = b.Summary;
        string ledgerHash = Stats.Sha256Hex(string.Join("\n", b.History.Transactions.Select(t =>
            $"{t.TxnId},{t.Date:yyyy-MM-dd},{t.Amount.ToString("R", CultureInfo.InvariantCulture)},{t.RunningBalance.ToString("R", CultureInfo.InvariantCulture)}")));
        var art = new
        {
            member_id = b.Profile.MemberId,
            member_name = b.Profile.Name,
            forecaster = b.Forecaster,
            model_version = b.ForecasterVersion,
            feature_hash = Stats.Sha256Hex(string.Join(",", featureSchema), 12),
            feature_schema = featureSchema,
            ledger_hash = ledgerHash,
            frozen_inputs_hash = b.FrozenHash,
            origin = b.Origin.ToString("yyyy-MM-dd"),
            seed = b.Seed, paths = b.Paths, horizon_days = b.Horizon,
            start_available_balance = Math.Round(b.StartBalance, 2),
            start_savings_balance = Math.Round(b.StartSavings, 2),
            average_daily_spend = Math.Round(b.Ads, 2),
            product_rules = b.Rules,
            recurring = b.Recurring.Select(r => new { r.Description, r.Category, kind = r.Kind.ToString(), cadence = r.Cadence.ToString(), r.DayOfMonth, r.PeriodDays, r.Amount, amount_cv = Math.Round(r.AmountCv, 3), r.Occurrences }),
            known_future_events = b.Events.Select(e => new { date = e.Date.ToString("yyyy-MM-dd"), e.Description, e.Category, e.Amount }),
            path = Enumerable.Range(0, b.Horizon).Select(d => new { date = b.DateAt(d).ToString("yyyy-MM-dd"), q10 = Math.Round(s.Q10[d], 2), q50 = Math.Round(s.Q50[d], 2), q90 = Math.Round(s.Q90[d], 2) }),
            metrics = new
            {
                headline_slack = s.Headline.ToDictionary(kv => $"{kv.Key}d", kv => Math.Round(kv.Value, 2)),
                true_slack = s.TrueSlack.ToDictionary(kv => $"{kv.Key}d", kv => Math.Round(kv.Value, 2)),
                lowest_true_slack_median_path = Math.Round(s.LowestTrueSlack, 2),
                lowest_true_slack_p10 = Math.Round(s.LowestTrueSlackP10, 2),
                lowest_q50_date = b.DateAt(s.LowestQ50Day).ToString("yyyy-MM-dd"),
                risk_days = s.RiskDays,
                p_overdraft = Math.Round(s.POverdraft, 3),
                expected_courtesy_fees = Math.Round(s.ExpFees, 2),
                expected_fee_dollars = Math.Round(s.ExpFeeDollars, 2),
            },
            explanation = Explainer.Dip(b),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string json = JsonSerializer.Serialize(art, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return Stats.Sha256Hex(json);
    }
}
