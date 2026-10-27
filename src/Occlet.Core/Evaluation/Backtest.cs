using Occlet.Core.Engine;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Util;

namespace Occlet.Core.Evaluation;

public sealed record BacktestPoint(string Forecaster, string MemberId, DateOnly Origin, int Horizon, int Day,
                                   double Actual, double Q10, double Q50, double Q90, double Ads);

public sealed record BacktestWindow(string Forecaster, string MemberId, DateOnly Origin, int Horizon, double ActualMin,
                                    double MinQ10, double MinQ50, double MinQ90, double Ads);

public sealed record BacktestMetric(string Forecaster, int Horizon, int Windows, int DaysScored,
    double Coverage80, double BelowQ10, double AboveQ90, double ScaledPinball, double MaeQ50Days,
    double MinCoverage80, double MinBelowQ10, bool PassesCoverageGate, bool PassesLowEndGate);

/// <summary>
/// Rolling-origin back-test: pretend "today" was each of several earlier dates (two weeks apart),
/// forecast from there using ONLY data up to that date, and score against what really happened.
/// One origin is one roll of the dice; many origins give an honest picture.
/// </summary>
public static class Backtester
{
    public static (List<BacktestPoint> points, List<BacktestWindow> windows) Run(
        IReadOnlyList<MemberHistory> members, ISpendForecaster forecaster, int stepDays = 14, int maxOrigins = 12,
        int paths = 500, Action<string>? log = null, Action<DateOnly>? afterFit = null)
    {
        var points = new List<BacktestPoint>();
        var windows = new List<BacktestWindow>();
        var end = members.Max(m => m.LastDate);
        var start = members.Min(m => m.FirstDate);
        int H = Settings.ReportHorizons.Max();
        for (int k = 0; k < maxOrigins; k++)
        {
            var origin = end.AddDays(-H - k * stepDays);
            if (origin.DayNumber - start.DayNumber < 120) break;
            var universe = members.Select(m => m.UpTo(origin)).Where(m => m.HistoryDays >= Settings.MinHistoryDays && m.FirstDate <= origin).ToList();
            forecaster.Fit(universe, origin);
            afterFit?.Invoke(origin);
            foreach (var m in members)
            {
                var hist = m.UpTo(origin);
                if (m.FirstDate > origin || hist.HistoryDays < Settings.MinHistoryDays) continue;
                var b = BaselineBuilder.Build(m, origin, forecaster, H, paths);
                foreach (int h in Settings.ReportHorizons)
                {
                    double actualMin = double.MaxValue;
                    var simMin = new double[b.Paths];
                    Array.Fill(simMin, double.MaxValue);
                    for (int d = 0; d < h; d++)
                    {
                        double y = m.CheckingEod(origin.AddDays(d + 1));
                        actualMin = Math.Min(actualMin, y);
                        for (int p = 0; p < b.Paths; p++) simMin[p] = Math.Min(simMin[p], b.Result.Paths[p][d]);
                        points.Add(new BacktestPoint(forecaster.Name, m.Id, origin, h, d, y, b.Summary.Q10[d], b.Summary.Q50[d], b.Summary.Q90[d], b.Ads));
                    }
                    Array.Sort(simMin);
                    windows.Add(new BacktestWindow(forecaster.Name, m.Id, origin, h, actualMin,
                        Stats.QuantileSorted(simMin, 0.1), Stats.QuantileSorted(simMin, 0.5), Stats.QuantileSorted(simMin, 0.9), b.Ads));
                }
            }
            log?.Invoke($"    {forecaster.Name}: origin {origin:yyyy-MM-dd} scored ({universe.Count} members)");
        }
        return (points, windows);
    }

    /// <summary>Same metrics, split by member (shows which members a model serves well or badly).</summary>
    public static List<(string member, BacktestMetric metric)> ScoreByMember(IEnumerable<BacktestPoint> points, IEnumerable<BacktestWindow> windows)
    {
        var w = windows.ToList();
        return points.GroupBy(p => p.MemberId).OrderBy(g => g.Key)
            .SelectMany(g => Score(g, w.Where(x => x.MemberId == g.Key)).Select(m => (g.Key, m))).ToList();
    }

    private static double Pinball(double q, double y, double f) => Math.Max(q * (y - f), (q - 1) * (y - f));

    /// <summary>
    /// Per horizon: share of real days inside the 80% band (Occlet gate: 75–85%), share below q10
    /// (the low-end honesty test — target ≈ 10%), scaled pinball loss (in days of spending, so members
    /// of different sizes are comparable), and the same honesty checks for the LOWEST balance in each window.
    /// </summary>
    public static List<BacktestMetric> Score(IEnumerable<BacktestPoint> points, IEnumerable<BacktestWindow> windows)
    {
        var w = windows.ToList();
        return points.GroupBy(p => (p.Forecaster, p.Horizon)).OrderBy(g => g.Key.Forecaster).ThenBy(g => g.Key.Horizon).Select(g =>
        {
            var pts = g.ToList();
            double cov = pts.Count(p => p.Actual >= p.Q10 && p.Actual <= p.Q90) / (double)pts.Count;
            double below = pts.Count(p => p.Actual < p.Q10) / (double)pts.Count;
            double above = pts.Count(p => p.Actual > p.Q90) / (double)pts.Count;
            double pin = pts.Average(p => (Pinball(0.1, p.Actual, p.Q10) + Pinball(0.5, p.Actual, p.Q50) + Pinball(0.9, p.Actual, p.Q90)) / 3 / p.Ads);
            double mae = pts.Average(p => Math.Abs(p.Actual - p.Q50) / p.Ads);
            var ww = w.Where(x => x.Forecaster == g.Key.Forecaster && x.Horizon == g.Key.Horizon).ToList();
            double minCov = ww.Count == 0 ? double.NaN : ww.Count(x => x.ActualMin >= x.MinQ10 && x.ActualMin <= x.MinQ90) / (double)ww.Count;
            double minBelow = ww.Count == 0 ? double.NaN : ww.Count(x => x.ActualMin < x.MinQ10) / (double)ww.Count;
            return new BacktestMetric(g.Key.Forecaster, g.Key.Horizon, ww.Count, pts.Count, cov, below, above, pin, mae, minCov, minBelow,
                                      cov >= 0.75 && cov <= 0.85, below >= 0.05 && below <= 0.15);
        }).ToList();
    }
}
