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
    public double[][] SavingsPaths { get; }
    public double[][] OutstandingPrincipal { get; }
    public double[][] UnpaidBills { get; }

    public ReplayResult(int paths, int horizon)
    {
        Paths = Enumerable.Range(0, paths).Select(_ => new double[horizon]).ToArray();
        SavingsPaths = Enumerable.Range(0, paths).Select(_ => new double[horizon]).ToArray();
        OutstandingPrincipal = Enumerable.Range(0, paths).Select(_ => new double[horizon]).ToArray();
        UnpaidBills = Enumerable.Range(0, paths).Select(_ => new double[horizon]).ToArray();
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
        double[][]? ItemLevel = null, IReadOnlyDictionary<string, int>? ItemIndex = null, double[][][]? CardFractions = null);

    public static ReplayResult Run(Input inp)
    {
        int P = inp.Everyday.Length, H = inp.Horizon;
        if (P == 0 || H <= 0 || inp.Everyday.Any(path => path.Length != H || path.Any(v => !double.IsFinite(v) || v < 0)))
            throw new ArgumentException("Replay requires nonempty, finite nonnegative daily spending paths.");
        if (!double.IsFinite(inp.StartBalance) || !double.IsFinite(inp.StartSavings) || inp.StartSavings < 0)
            throw new ArgumentException("Invalid opening account balances.");
        if (inp.Events.Select(e => e.Key).Distinct().Count() != inp.Events.Count || inp.Events.Any(e => !double.IsFinite(e.Amount)))
            throw new ArgumentException("Events need unique keys and finite amounts.");
        if (inp.CardFractions != null && (inp.CardFractions.Length != P || inp.CardFractions.Any(path => path.Length != H ||
            path.Any(day => day.Length == 0 || day.Any(v => !double.IsFinite(v) || v < 0) || Math.Abs(day.Sum() - 1) > 1e-8))))
            throw new ArgumentException("Card proportions must sum to one for every path and day.");
        var byDay = Enumerable.Range(0, H).Select(_ => new List<FutureEvent>()).ToArray();
        foreach (var e in inp.Events) if (e.Day >= 0 && e.Day < H) byDay[e.Day].Add(e);

        var res = new ReplayResult(P, H);
        var r = inp.Rules; var bh = inp.Behaviour;
        for (int p = 0; p < P; p++)
        {
            double bal = inp.StartBalance, sav = inp.StartSavings, principal = 0, cutLeft = 0;
            int cutDays = 0;
            var pending = new Dictionary<string, (FutureEvent ev, double amount, int attempts)>();
            double factor = inp.LeverU[p] < bh.Adherence ? bh.SpendFactorIfAdhering : 1.0;
            for (int d = 0; d < H; d++)
            {
                int feesToday = 0;
                // Failed obligations remain outstanding even when automatic retries stop.
                var due = pending.Values.Where(x => x.attempts < 3 && x.ev.Channel != "Card").ToList();
                foreach (var e in byDay[d])
                {
                    double amt = Money(Draw(e, p, inp));
                    if (amt >= 0)
                    {
                        // Positive internal transfer is savings to checking, not external income.
                        if (e.Category == "Transfer")
                        {
                            if (sav + 1e-9 < amt) continue;
                            sav = Money(sav - amt);
                        }
                        bal = Money(bal + amt);
                        principal = Money(principal + e.PrincipalChange);
                    }
                    else due.Add((e, -amt, 0));
                }

                bool Pay(double amount, bool card, bool transfer)
                {
                    if (amount <= 0) return true;
                    // Do not draw savings to fund a transfer back into savings.
                    if (r.SavingsFirst && !transfer)
                    {
                        double gap = Math.Max(amount - bal, 0);
                        // Avoid draining savings for an authorization that still cannot succeed.
                        if ((card ? r.CardOverdraftOptIn : r.CourtesyPay) || bal + sav + 1e-9 >= amount)
                        {
                            double pull = Math.Min(gap, sav);
                            sav = Money(sav - pull); bal = Money(bal + pull); res.SavingsPulled[p] += pull;
                        }
                    }
                    bool shortfall = amount > bal + 1e-9;
                    bool covered = card ? r.CardOverdraftOptIn : r.CourtesyPay;
                    if (shortfall && !covered)
                    {
                        if (card) res.CardDeclined[p] += amount;
                        else { res.BillDeclines[p]++; bal = Money(bal - r.NsfFee); res.FeeDollars[p] += r.NsfFee; }
                        return false;
                    }
                    bal = Money(bal - amount);
                    if (transfer) sav = Money(sav + amount);
                    if (shortfall && amount >= r.MinItemForFee && feesToday < r.MaxFeesPerDay)
                    {
                        bal = Money(bal - r.OdFee); feesToday++; res.FeeCount[p]++; res.FeeDollars[p] += r.OdFee;
                    }
                    return true;
                }

                // Preserve channel and identity. Non-card debits post largest first, then card events.
                foreach (var (e, amount, attempts) in due.OrderBy(x => x.ev.Channel == "Card" ? 1 : 0)
                    .ThenByDescending(x => x.ev.Channel == "Card" ? 0 : x.amount))
                {
                    bool paid = Pay(amount, e.Channel == "Card", e.Category == "Transfer");
                    if (paid)
                    {
                        pending.Remove(e.Key);
                        // Principal is reduced only after the corresponding repayment actually posts.
                        principal = Money(Math.Max(0, principal + e.PrincipalChange));
                    }
                    else if (e.Category != "Transfer") pending[e.Key] = (e, amount, attempts + 1);
                }

                double spend = Math.Max(0, inp.Everyday[p][d] * factor);
                if (cutDays > 0)
                {
                    double cut = Math.Min(spend, cutLeft / cutDays);
                    spend -= cut; cutLeft -= cut; cutDays--;
                }
                // Daily model totals need an explicit ticket-size assumption. Baselines sample
                // historical within-day proportions once; a standalone replay defaults to one ticket.
                var fractions = inp.CardFractions?[p][d] ?? new[] { 1.0 };
                double total = Money(spend), allocated = 0;
                for (int t = 0; t < fractions.Length; t++)
                {
                    double remaining = Money(total - allocated);
                    double amount = t == fractions.Length - 1 ? remaining : Math.Clamp(Money(total * fractions[t]), 0, remaining);
                    allocated += amount;
                    Pay(amount, card: true, transfer: false);
                }
                if (bh.AlertThreshold is double th && bal < th && cutDays == 0 && inp.AlertU[p][d] < bh.AlertResponseRate)
                { cutLeft = bh.AlertCut; cutDays = bh.AlertDays; }
                res.Paths[p][d] = bal;
                res.SavingsPaths[p][d] = sav;
                res.OutstandingPrincipal[p][d] = principal;
                res.UnpaidBills[p][d] = pending.Values.Sum(x => x.amount);
            }
        }
        return res;
    }

    private static double Money(double value) => Math.Round(value, 2, MidpointRounding.ToEven);

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
