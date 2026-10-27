using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;

namespace Occlet.Core.Engine;

/// <summary>The deposit product's rules — what happens when money runs short.</summary>
public sealed record ProductRules(
    bool CourtesyPay, double OdFee, int MaxFeesPerDay, double MinItemForFee,
    bool CardOverdraftOptIn, double NsfFee, bool SavingsFirst)
{
    public static ProductRules From(MemberProfile p) =>
        new(p.CourtesyPay, p.OverdraftFee, p.MaxFeesPerDay, p.MinItemForFee, p.CardOverdraftOptIn, p.NsfFee, p.SavingsFirst);
}

/// <summary>How the member behaves. The "learned" levers (alerts, trimming a category) live here.</summary>
public sealed record Behaviour
{
    public double? AlertThreshold { get; init; }                   // null = no alert lever simulated
    public double AlertResponseRate { get; init; } = Settings.AlertResponseRate;
    public double AlertCut { get; init; } = Settings.AlertCut;
    public int AlertDays { get; init; } = Settings.AlertCutDays;
    public double SpendFactorIfAdhering { get; init; } = 1.0;      // e.g. 0.95 = everyday spend 5% lower
    public double Adherence { get; init; } = 1.0;                  // share of futures in which the member sticks to it
    public static readonly Behaviour None = new();
}

/// <summary>Outcome of replaying every simulated future day by day.</summary>
public sealed class ReplayResult
{
    public double[][] Paths { get; }        // [path][day] end-of-day available balance
    public double[] FeeCount { get; }
    public double[] FeeDollars { get; }
    public double[] BillDeclines { get; }
    public double[] CardDeclined { get; }   // dollars of card purchases that did not fit
    public double[] SavingsPulled { get; }

    public ReplayResult(int paths, int horizon)
    {
        Paths = Enumerable.Range(0, paths).Select(_ => new double[horizon]).ToArray();
        FeeCount = new double[paths]; FeeDollars = new double[paths]; BillDeclines = new double[paths];
        CardDeclined = new double[paths]; SavingsPulled = new double[paths];
    }
}

/// <summary>
/// The replay engine: pure arithmetic, no model. It is shared by Problem 1 (baseline) and
/// Problem 2 (scenarios), so any difference between the two comes only from the levers.
/// </summary>
public static class Replay
{
    public sealed record Input(
        double StartBalance, double StartSavings, IReadOnlyList<FutureEvent> Events, int Horizon,
        double[][] Everyday, double[][] AlertU, double[] LeverU,
        double[][] EventZ, IReadOnlyDictionary<string, int> EventKeyIndex,
        ProductRules Rules, Behaviour Behaviour,
        double[][]? ItemLevel = null, IReadOnlyDictionary<string, int>? ItemIndex = null);

    public static ReplayResult Run(Input inp)
    {
        int P = inp.Everyday.Length, H = inp.Horizon;
        var byDay = Enumerable.Range(0, H).Select(_ => new List<FutureEvent>()).ToArray();
        foreach (var e in inp.Events) if (e.Day >= 0 && e.Day < H) byDay[e.Day].Add(e);

        var res = new ReplayResult(P, H);
        var r = inp.Rules; var bh = inp.Behaviour;
        var bills = new List<(double amount, int attempt)>();
        var retry = new List<(double amount, int attempt)>();

        for (int p = 0; p < P; p++)
        {
            double bal = inp.StartBalance, sav = inp.StartSavings, cutLeft = 0;
            int cutDays = 0;
            retry.Clear();
            double factor = inp.LeverU[p] < bh.Adherence ? bh.SpendFactorIfAdhering : 1.0;
            var row = res.Paths[p];

            for (int d = 0; d < H; d++)
            {
                int feesToday = 0;
                bills.Clear(); bills.AddRange(retry); retry.Clear();

                // 1) money in first, then collect today's bills
                foreach (var e in byDay[d])
                {
                    double amt = Draw(e, p, inp);
                    if (amt >= 0) bal += amt; else bills.Add((-amt, 0));
                }

                // 2) bills, largest first
                bills.Sort((a, b) => b.amount.CompareTo(a.amount));
                foreach (var (amt, attempt) in bills)
                {
                    if (r.SavingsFirst) PullFromSavings(ref bal, ref sav, amt, res, p);
                    bool shortfall = amt > bal + 1e-9;
                    if (r.CourtesyPay)
                    {
                        bal -= amt;                                                   // paid even if it overdraws ...
                        if (shortfall && amt >= r.MinItemForFee && feesToday < r.MaxFeesPerDay)
                        { bal -= r.OdFee; feesToday++; res.FeeCount[p]++; res.FeeDollars[p] += r.OdFee; } // ... for a fee
                    }
                    else if (shortfall)
                    {
                        res.BillDeclines[p]++;                                        // declined: nothing leaves ...
                        bal -= r.NsfFee; res.FeeDollars[p] += r.NsfFee;
                        if (attempt < 2) retry.Add((amt, attempt + 1));               // ... and it is presented again tomorrow
                    }
                    else bal -= amt;
                }

                // 3) everyday card spending (minus any cut the member is making after an alert)
                double spend = inp.Everyday[p][d] * factor;
                if (cutDays > 0) { double c = cutLeft / cutDays; spend = Math.Max(0, spend - c); cutLeft -= c; cutDays--; }
                if (r.SavingsFirst) PullFromSavings(ref bal, ref sav, spend, res, p);
                if (r.CardOverdraftOptIn)
                {
                    bool shortfall = spend > bal + 1e-9;
                    bal -= spend;
                    if (shortfall && spend >= r.MinItemForFee && feesToday < r.MaxFeesPerDay)
                    { bal -= r.OdFee; res.FeeCount[p]++; res.FeeDollars[p] += r.OdFee; }
                }
                else
                {
                    // Reg E: without opt-in, a card purchase that does not fit is simply declined
                    double fits = Math.Max(bal, 0), paid = Math.Min(spend, fits);
                    res.CardDeclined[p] += spend - paid;
                    bal -= paid;
                }

                // 4) learned lever: a low-balance alert makes some members trim spending for a few days
                if (bh.AlertThreshold is double th && bal < th && cutDays == 0 && inp.AlertU[p][d] < bh.AlertResponseRate)
                { cutLeft = bh.AlertCut; cutDays = bh.AlertDays; }

                row[d] = bal;
            }
        }
        return res;
    }

    private static void PullFromSavings(ref double bal, ref double sav, double need, ReplayResult res, int p)
    {
        double gap = need - bal;
        if (gap <= 0 || sav <= 0) return;
        double pull = Math.Min(gap, sav);
        sav -= pull; bal += pull; res.SavingsPulled[p] += pull;
    }

    /// <summary>Known dates, uncertain amounts: a gig payout with CV 30% gets a different (but repeatable) amount in each future.</summary>
    private static double Draw(FutureEvent e, int p, Input inp)
    {
        if (e.AmountCv < 0.03 || !inp.EventKeyIndex.TryGetValue(e.Key, out int k)) return e.Amount;
        double cv = Math.Min(e.AmountCv, 0.6);
        double sigma = Math.Sqrt(Math.Log(1 + cv * cv));
        double level = 1.0;                                       // shared shock for this item in this future
        int at = e.Key.IndexOf('@');
        if (inp.ItemLevel != null && inp.ItemIndex != null && at > 0 && inp.ItemIndex.TryGetValue(e.Key[..at], out int it))
            level = inp.ItemLevel[p][it];
        return e.Amount * level * Math.Exp(sigma * inp.EventZ[p][k] - sigma * sigma / 2);
    }
}
