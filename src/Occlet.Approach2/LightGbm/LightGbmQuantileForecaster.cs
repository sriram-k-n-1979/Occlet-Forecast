using Occlet.Core;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Util;

namespace Occlet.Approach2.LightGbm;

/// <summary>Diagnostics from one Fit: what the model learned and how the calibration moved it.</summary>
public sealed record LgbmFitReport(DateOnly Cutoff, int TrainRows, int CalibrationRows, int Members,
    double[] ConformalLow, double[] ConformalHigh, double CopulaLambda, double CopulaCorrObserved,
    double RawCoverageCal, double CalibratedCoverageCal, double RawBelowQ10Cal, double CalibratedBelowQ10Cal);

/// <summary>
/// APPROACH 2a — LightGBM, as Occlet's doc proposes, plus the three fixes it needs for this problem.
///
///  • Model: one GLOBAL model per quantile (q10, q25, q50, q75, q90), trained on every member's history
///    ("pooling" — a single member has far too few rows). Target = everyday spending over the next H days
///    as a multiple of the member's normal pace; horizon is a feature, so 5 models cover every day 1–90.
///    Known paydays and bills are NOT predicted — they are added exactly by the replay engine.
///  • Fix 1 — calibration (conformalised quantile regression): quantile trees are over-confident on small,
///    overlapping time-series data. Purged, time-blocked cross-validation produces honest out-of-fold errors;
///    the bands are then widened (or narrowed) by exactly the amount they missed, per horizon bucket. This
///    targets Occlet's "never compromise" test: the 10th percentile must really be the 10th.
///  • Fix 2 — quantile crossing: the five predictions are sorted, and made non-decreasing over the horizon.
///  • Fix 3 — paths: LightGBM gives a separate distribution for each horizon, not a joined-up future.
///    Slack's "lowest day" and fee rules need whole paths, so a Gaussian copula ties the horizons together
///    (a Brownian-motion correlation plus a persistent "mood" share λ estimated from the calibration data).
/// </summary>
public sealed class LightGbmQuantileForecaster : ISpendForecaster, IDisposable
{
    public static readonly double[] Alphas = { 0.10, 0.25, 0.50, 0.75, 0.90 };
    private static readonly int[] BucketEdges = { 7, 30, 90 };   // horizon buckets for calibration: 1–7, 8–30, 31–90

    private readonly bool _conformal;
    private LgbmBooster[]? _models;
    private double[] _cLo = new double[3], _cHi = new double[3];
    private double _lambda = 0.5;

    public LightGbmQuantileForecaster(bool conformal = true) => _conformal = conformal;
    public string Name => _conformal ? "LightGBM" : "LightGBM-raw";
    public string Version => $"lightgbm-4.5.0-quantile-{(_conformal ? "cqr-" : "")}copula-1.0";
    public LgbmFitReport? LastFit { get; private set; }
    public static IReadOnlyList<string> FeatureSchema => FeatureBuilder.Names;

    public const string DatasetParams = "max_bin=63 min_data_in_bin=3 verbose=-1";
    public static string TrainParams(double alpha) =>
        $"objective=quantile alpha={alpha.ToString(Fmt.Culture)} learning_rate=0.05 num_leaves=7 min_data_in_leaf=60 " +
        "feature_fraction=0.8 bagging_fraction=0.8 bagging_freq=1 lambda_l2=5.0 seed=7 deterministic=true " +
        "force_row_wise=true num_threads=2 verbose=-1";
    public const int Iterations = 200;

    public void Fit(IReadOnlyList<MemberHistory> universe, DateOnly cutoff)
    {
        DisposeModels();
        // 1) training rows: every member, origins every 2 days, horizons on a grid, labels fully observed by the cutoff
        var rows = new List<(double[] x, double y, DateOnly origin, int h, string key)>();
        foreach (var hist in universe)
        {
            var fb = FeatureBuilder.FromHistory(hist.UpTo(cutoff));
            for (var origin = fb.Series.Start.AddDays(Settings.MinHistoryDays - 1); origin < cutoff; origin = origin.AddDays(2))
            {
                var hs = FeatureBuilder.HorizonGrid.Where(hh => origin.AddDays(hh) <= cutoff).ToList();
                if (hs.Count == 0) continue;
                var x = fb.Rows(origin, hs);
                for (int r = 0; r < hs.Count; r++)
                    rows.Add((x.AsSpan(r * NF, NF).ToArray(), fb.Target(origin, hs[r]), origin, hs[r], $"{hist.Id}@{origin:yyyyMMdd}"));
            }
        }

        // 2) honest out-of-fold predictions for calibration: split origins into 4 time blocks; for each block,
        //    train only on rows whose label window ENDS before the block starts or whose features START after
        //    the block's label windows end ("purged" CV — overlapping 90-day windows would otherwise leak).
        var oof = new double[5][];
        for (int k = 0; k < 5; k++) oof[k] = new double[rows.Count];
        var hasOof = new bool[rows.Count];
        if (_conformal && rows.Count > 200)
        {
            var dates = rows.Select(r => r.origin).Distinct().OrderBy(d => d).ToList();
            for (int fold = 0; fold < Folds; fold++)
            {
                var bStart = dates[fold * dates.Count / Folds];
                var bEnd = dates[Math.Min((fold + 1) * dates.Count / Folds, dates.Count) - 1];
                var calIdx = Enumerable.Range(0, rows.Count).Where(i => rows[i].origin >= bStart && rows[i].origin <= bEnd).ToList();
                var trIdx = Enumerable.Range(0, rows.Count).Where(i =>
                    rows[i].origin.AddDays(rows[i].h) < bStart || rows[i].origin.AddDays(-90) > bEnd.AddDays(Settings.Horizon)).ToList();
                if (calIdx.Count < 30 || trIdx.Count < 300) continue;
                var models = TrainAll(trIdx.Select(i => rows[i]).ToList());
                var xc = calIdx.SelectMany(i => rows[i].x).ToArray();
                for (int k = 0; k < 5; k++)
                {
                    var pr = models[k].Predict(xc, calIdx.Count);
                    for (int j = 0; j < calIdx.Count; j++) oof[k][calIdx[j]] = pr[j];
                    models[k].Dispose();
                }
                foreach (int i in calIdx) hasOof[i] = true;
            }
        }
        var cal = Enumerable.Range(0, rows.Count).Where(i => hasOof[i]).ToList();

        // 3) final models: one LightGBM per quantile on ALL rows
        _models = TrainAll(rows);

        // 4) conformal offsets per horizon bucket from the honest out-of-fold errors
        _cLo = new double[3]; _cHi = new double[3];
        for (int b = 0; b < 3; b++)
        {
            var idx = cal.Where(i => Bucket(rows[i].h) == b).ToList();
            if (idx.Count < 20)
            {   // too few honest errors for this horizon (short history): carry over the shorter bucket's widening rather than none
                if (b > 0) { _cLo[b] = Math.Max(_cLo[b - 1], 0); _cHi[b] = Math.Max(_cHi[b - 1], 0); }
                continue;
            }
            _cLo[b] = ConformalQuantile(idx.Select(i => oof[0][i] - rows[i].y).ToList(), 0.10);   // q10 should be beaten 90% of the time
            _cHi[b] = ConformalQuantile(idx.Select(i => rows[i].y - oof[4][i]).ToList(), 0.10);
        }

        // 5) copula: how strongly is a member's 7-day surprise carried into their 90-day surprise?
        var z7 = new Dictionary<string, double>(); var z90 = new Dictionary<string, double>();
        foreach (int i in cal)
        {
            if (rows[i].h != 7 && rows[i].h != 90) continue;
            var q = Adjust(Enumerable.Range(0, 5).Select(k => oof[k][i]).ToArray(), rows[i].h);
            (rows[i].h == 7 ? z7 : z90)[rows[i].key] = Stats.NormalInv(Cdf(rows[i].y, q));
        }
        var keys = z7.Keys.Intersect(z90.Keys).ToList();
        double corr = keys.Count >= 10 ? Pearson(keys.Select(k => z7[k]).ToList(), keys.Select(k => z90[k]).ToList()) : double.NaN;
        double brownian = Math.Sqrt(7.0 / 90.0);
        _lambda = double.IsNaN(corr) ? 0.5 : Math.Clamp((corr - brownian) / (1 - brownian), 0.0, 0.95);

        // 6) diagnostics on the out-of-fold rows: band coverage before vs after the conformal adjustment
        double rawCov = 0, calCov = 0, rawHi = 0, calHi = 0;
        foreach (int i in cal)
        {
            double y = rows[i].y;
            var adj = Adjust(Enumerable.Range(0, 5).Select(k => oof[k][i]).ToArray(), rows[i].h);
            rawCov += y >= oof[0][i] && y <= oof[4][i] ? 1 : 0; rawHi += y > oof[4][i] ? 1 : 0;   // spend ABOVE q90 = balance BELOW its q10
            calCov += y >= adj[0] && y <= adj[4] ? 1 : 0; calHi += y > adj[4] ? 1 : 0;
        }
        int nc = Math.Max(cal.Count, 1);
        LastFit = new LgbmFitReport(cutoff, rows.Count, cal.Count, universe.Count, _cLo.ToArray(), _cHi.ToArray(), _lambda, corr,
                                    rawCov / nc, calCov / nc, rawHi / nc, calHi / nc);
    }

    private const int Folds = 4;
    private static readonly int NF = FeatureBuilder.Names.Length;

    private static LgbmBooster[] TrainAll(IReadOnlyList<(double[] x, double y, DateOnly origin, int h, string key)> rows)
    {
        var X = rows.SelectMany(r => r.x).ToArray();
        var Y = rows.Select(r => (float)r.y).ToArray();
        return Alphas.Select(a => LgbmBooster.Train(X, rows.Count, NF, Y, FeatureBuilder.Names, DatasetParams, TrainParams(a), Iterations)).ToArray();
    }

    public double[][] SamplePaths(ForecastContext ctx, int horizon, int nPaths, Random rng)
    {
        if (_models == null) throw new InvalidOperationException("Fit must be called first.");
        var fb = new FeatureBuilder(ctx.History, ctx.Recurring, ctx.Series);
        var grid = FeatureBuilder.HorizonGrid.Where(h => h <= horizon).Append(horizon).Distinct().OrderBy(h => h).ToList();
        var x = fb.Rows(ctx.Origin, grid);
        var raw = _models.Select(m => m.Predict(x, grid.Count)).ToArray();
        double scale = fb.Scale(ctx.Origin);

        // cumulative-spend quantiles at each grid horizon, calibrated, sorted, non-decreasing in horizon
        var cum = new double[grid.Count][];
        for (int j = 0; j < grid.Count; j++)
        {
            var q = Adjust(Enumerable.Range(0, 5).Select(k => raw[k][j]).ToArray(), grid[j]);
            cum[j] = q.Select(v => Math.Max(v, 0) * scale * grid[j]).ToArray();
            if (j > 0) for (int k = 0; k < 5; k++) cum[j][k] = Math.Max(cum[j][k], cum[j - 1][k]);
        }

        double wWeekend = fb.WeekendWeight();
        var weights = Enumerable.Range(0, horizon).Select(d => EverydaySeries.IsWeekend(ctx.Origin.AddDays(d + 1)) ? wWeekend : 1.0).ToArray();
        var paths = new double[nPaths][];
        for (int p = 0; p < nPaths; p++)
        {
            double z0 = Stats.Gaussian(rng), w = 0, prevC = 0; int prevH = 0;
            var row = new double[horizon];
            for (int j = 0; j < grid.Count; j++)
            {
                w += Stats.Gaussian(rng) * Math.Sqrt(grid[j] - prevH);                       // Brownian motion over the horizon
                double z = Math.Sqrt(_lambda) * z0 + Math.Sqrt(1 - _lambda) * w / Math.Sqrt(grid[j]);
                double c = Math.Max(InvCdf(Stats.NormalCdf(z), cum[j]), prevC);
                double segW = 0; for (int d = prevH; d < grid[j]; d++) segW += weights[d];
                for (int d = prevH; d < grid[j]; d++) row[d] = (c - prevC) * weights[d] / segW;   // spread the segment over its days
                prevC = c; prevH = grid[j];
            }
            paths[p] = row;
        }
        return paths;
    }

    /// <summary>TreeSHAP drivers of the q50 prediction for the next 30 days (in "multiple of normal pace" units).</summary>
    public List<(string feature, double value, double contribution)> Explain(ForecastContext ctx, int horizon = 30)
    {
        var fb = new FeatureBuilder(ctx.History, ctx.Recurring, ctx.Series);
        var x = fb.Rows(ctx.Origin, new[] { horizon });
        var c = _models![2].Contributions(x, 1);
        return FeatureBuilder.Names.Select((n, i) => (n, x[i], c[i])).OrderByDescending(t => Math.Abs(t.Item3)).ToList();
    }

    public double[] Importance() => _models![2].GainImportance();
    public void SaveModels(string dir) { for (int k = 0; k < Alphas.Length; k++) _models![k].Save(Path.Combine(dir, $"lightgbm_q{Alphas[k] * 100:00}.txt")); }

    private static int Bucket(int h) => h <= BucketEdges[0] ? 0 : h <= BucketEdges[1] ? 1 : 2;

    /// <summary>Apply the conformal offsets, then sort so the quantiles can never cross.</summary>
    private double[] Adjust(double[] q, int h)
    {
        int b = Bucket(h);
        var a = new[] { q[0] - _cLo[b], q[1] - _cLo[b] / 2, q[2], q[3] + _cHi[b] / 2, q[4] + _cHi[b] };
        Array.Sort(a);
        return a;
    }

    /// <summary>Split-conformal quantile with the finite-sample correction ⌈(n+1)(1−α)⌉ / n.</summary>
    private static double ConformalQuantile(List<double> scores, double alpha)
    {
        scores.Sort();
        int n = scores.Count, k = (int)Math.Ceiling((n + 1) * (1 - alpha)) - 1;
        return scores[Math.Clamp(k, 0, n - 1)];
    }

    /// <summary>Quantile function through the five knots, with straight-line tails (never below 0).</summary>
    private static double InvCdf(double u, double[] q)
    {
        if (u <= Alphas[0]) return Math.Max(0, q[0] - (Alphas[0] - u) / (Alphas[1] - Alphas[0]) * (q[1] - q[0]));
        if (u >= Alphas[4]) return q[4] + (u - Alphas[4]) / (Alphas[4] - Alphas[3]) * (q[4] - q[3]);
        int k = 0; while (u > Alphas[k + 1]) k++;
        return q[k] + (u - Alphas[k]) / (Alphas[k + 1] - Alphas[k]) * (q[k + 1] - q[k]);
    }

    private static double Cdf(double y, double[] q)
    {
        double lo = 0.001, hi = 0.999;
        for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; if (InvCdf(mid, q) < y) lo = mid; else hi = mid; }
        return (lo + hi) / 2;
    }

    private static double Pearson(List<double> a, List<double> b)
    {
        double ma = a.Average(), mb = b.Average();
        double cov = a.Zip(b, (x, y) => (x - ma) * (y - mb)).Sum();
        double va = a.Sum(x => (x - ma) * (x - ma)), vb = b.Sum(y => (y - mb) * (y - mb));
        return va > 0 && vb > 0 ? cov / Math.Sqrt(va * vb) : double.NaN;
    }

    private void DisposeModels() { if (_models != null) foreach (var m in _models) m.Dispose(); _models = null; }
    public void Dispose() => DisposeModels();
}
