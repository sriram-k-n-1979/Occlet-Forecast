using Occlet.Core.Ledger;

namespace Occlet.Core.Forecasting;

/// <summary>
/// Daily "everyday" spending: every debit that is NOT a recurring bill, NOT a fee and NOT a
/// transfer to the member's own savings (groceries, gas, coffee, and surprises like a car repair).
///
/// Censoring fix: when money runs low, card purchases get declined, so the ledger under-records what
/// the member wanted to spend. A day is "healthy" if it started with at least $150 (above a typical
/// day's spending, so ordinary purchases fit). Other days are flagged un-healthy and
/// their value is imputed from the trailing 60-day average of healthy days of the same type
/// (weekday or weekend). Only past days are used, so the series never peeks at the future.
/// </summary>
public sealed class EverydaySeries
{
    public DateOnly Start { get; }
    public double[] Posted { get; }
    public double[] Imputed { get; }
    public bool[] Healthy { get; }
    public IReadOnlyDictionary<string, double> CategoryShare { get; }
    public int Length => Posted.Length;
    public DateOnly DateAt(int i) => Start.AddDays(i);
    public static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private EverydaySeries(DateOnly start, double[] posted, double[] imputed, bool[] healthy, Dictionary<string, double> share)
    { Start = start; Posted = posted; Imputed = imputed; Healthy = healthy; CategoryShare = share; }

    public static EverydaySeries Build(MemberHistory h, IReadOnlyList<RecurringItem> recurring)
    {
        var recurringDescriptions = recurring.Select(r => r.Description).ToHashSet();
        int n = h.HistoryDays;
        var posted = new double[n];
        var byCat = new Dictionary<string, double>();
        foreach (var t in h.Checking)
        {
            if (t.Amount >= 0 || t.Category is "Fee" or "Transfer" || recurringDescriptions.Contains(t.Description)) continue;
            int i = t.Date.DayNumber - h.FirstDate.DayNumber;
            if (i < 0 || i >= n) continue;
            posted[i] += -t.Amount;
            if (i >= n - Settings.ResidualLookbackDays) byCat[t.Category] = byCat.GetValueOrDefault(t.Category) - t.Amount;
        }
        var healthy = new bool[n];
        var imputed = new double[n];
        for (int i = 0; i < n; i++)
        {
            var d = h.FirstDate.AddDays(i);
            // healthy = the day STARTED with enough money that ordinary purchases fit. (Filtering on the END-of-day
            // balance would be wrong: heavy-spending days end low, so they would be dropped and spending under-stated.)
            double startOfDay = i == 0 ? h.CheckingEod(d) - DayNet(h, d) : h.CheckingEod(d.AddDays(-1));
            healthy[i] = startOfDay >= Settings.HealthyBalance;
            if (healthy[i]) { imputed[i] = posted[i]; continue; }
            // trailing healthy days of the same type
            var same = new List<double>(); var any = new List<double>();
            for (int j = Math.Max(0, i - 60); j < i; j++)
            {
                if (!healthy[j]) continue;
                any.Add(posted[j]);
                if (IsWeekend(h.FirstDate.AddDays(j)) == IsWeekend(d)) same.Add(posted[j]);
            }
            imputed[i] = same.Count >= 4 ? same.Average() : any.Count > 0 ? any.Average() : posted[i];
        }
        double total = byCat.Values.Sum();
        var share = byCat.ToDictionary(kv => kv.Key, kv => total > 0 ? kv.Value / total : 0);
        return new EverydaySeries(h.FirstDate, posted, imputed, healthy, share);
    }

    private static double DayNet(MemberHistory h, DateOnly d) => h.Checking.Where(t => t.Date == d).Sum(t => t.Amount);

    /// <summary>Mean of the imputed series over the last <paramref name="days"/> days (the member's spending "scale").</summary>
    public double TrailingMean(int days)
    {
        int from = Math.Max(0, Length - days);
        double s = 0; for (int i = from; i < Length; i++) s += Imputed[i];
        return Length - from == 0 ? 0 : s / (Length - from);
    }
}

public static class SpendStats
{
    /// <summary>
    /// Slack denominator: every checking outflow (rent, bills, card, fees) except moving money into
    /// one's own savings, averaged over the last 90 days (or the whole history if shorter).
    /// The doc says this must match the credit union's own definition — it is one setting.
    /// </summary>
    public static double AverageDailySpend(MemberHistory h)
    {
        int window = Math.Min(Settings.SpendWindowDays, h.HistoryDays);
        var from = h.LastDate.AddDays(-window + 1);
        double outflow = -h.Checking.Where(t => t.Date >= from && t.Amount < 0 && t.Category != "Transfer").Sum(t => t.Amount);
        return Math.Max(outflow / window, 1.0);
    }
}
