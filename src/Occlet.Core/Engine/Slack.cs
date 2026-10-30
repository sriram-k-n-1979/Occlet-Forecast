using Occlet.Core.Util;

namespace Occlet.Core.Engine;

/// <summary>
/// Turns simulated balance paths into Occlet's outputs. Slack is never modelled directly — it is
/// always derived from the balance path: slack = balance ÷ average daily spend.
///
/// TRUE slack (Occlet deck p.5): borrowed principal still owed is taken OUT of the numerator and the
/// loan repayment is ADDED to the denominator, so a loan cannot fake an improvement.
/// </summary>
public sealed class SlackSummary
{
    public required double[] Q10 { get; init; }
    public required double[] Q50 { get; init; }
    public required double[] Q90 { get; init; }
    public double Ads { get; init; }
    public double Denominator { get; init; }
    public required double[] PathLowestTrueSlack { get; init; }   // per future: min over days of true slack
    public double LowestTrueSlack { get; init; }                  // median across futures — the decision score
    public double LowestTrueSlackP10 { get; init; }               // bad-case version
    public int LowestQ50Day { get; init; }
    public double LowestQ50Slack { get; init; }
    public required Dictionary<int, double> Headline { get; init; }
    public required Dictionary<int, double> TrueSlack { get; init; }
    public int RiskDays { get; init; }
    public double POverdraft { get; init; }
    public double ExpFees { get; init; }
    public double ExpFeeDollars { get; init; }
    public double ExpBillDeclines { get; init; }
    public double ExpCardDeclined { get; init; }
    public double ExpSavingsPulled { get; init; }
    public double ExpUnpaidBills { get; init; }

    public static SlackSummary From(ReplayResult r, double ads, double extraDailySpend = 0)
    {
        int P = r.Paths.Length, H = r.Paths[0].Length;
        double denom = Math.Max(ads + extraDailySpend, 1.0);
        var q10 = new double[H]; var q50 = new double[H]; var q90 = new double[H];
        var col = new double[P];
        for (int d = 0; d < H; d++)
        {
            for (int p = 0; p < P; p++) col[p] = r.Paths[p][d];
            Array.Sort(col);
            q10[d] = Stats.QuantileSorted(col, 0.10);
            q50[d] = Stats.QuantileSorted(col, 0.50);
            q90[d] = Stats.QuantileSorted(col, 0.90);   // sorted per day, so the three lines can never cross
        }
        var pathLowest = new double[P];
        int overdrawn = 0;
        for (int p = 0; p < P; p++)
        {
            double lo = double.MaxValue, minBal = double.MaxValue;
            for (int d = 0; d < H; d++)
            {
                double b = r.Paths[p][d];
                minBal = Math.Min(minBal, b);
                lo = Math.Min(lo, b - r.OutstandingPrincipal[p][d]);
            }
            pathLowest[p] = lo / denom;
            if (minBal < 0) overdrawn++;
        }
        var headline = new Dictionary<int, double>();
        var trueSlack = new Dictionary<int, double>();
        foreach (int h in Settings.ReportHorizons.Where(h => h <= H))
        {
            headline[h] = q50[h - 1] / ads;
            trueSlack[h] = Stats.Median(r.Paths.Select((path, p) => path[h - 1] - r.OutstandingPrincipal[p][h - 1])) / denom;
        }
        int lowDay = Array.IndexOf(q50, q50.Min());
        return new SlackSummary
        {
            Q10 = q10, Q50 = q50, Q90 = q90, Ads = ads, Denominator = denom,
            PathLowestTrueSlack = pathLowest,
            LowestTrueSlack = Stats.Median(pathLowest),
            LowestTrueSlackP10 = Stats.Quantile(pathLowest, 0.10),
            LowestQ50Day = lowDay, LowestQ50Slack = q50[lowDay] / ads,
            Headline = headline, TrueSlack = trueSlack,
            RiskDays = q10.Count(x => x < 0),
            POverdraft = overdrawn / (double)P,
            ExpFees = r.FeeCount.Average(), ExpFeeDollars = r.FeeDollars.Average(),
            ExpBillDeclines = r.BillDeclines.Average(), ExpCardDeclined = r.CardDeclined.Average(),
            ExpSavingsPulled = r.SavingsPulled.Average(),
            ExpUnpaidBills = r.UnpaidBills.Average(path => path[^1]),
        };
    }
}
