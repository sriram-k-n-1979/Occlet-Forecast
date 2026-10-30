// ─────────────────────────────────────────────────────────────────────────────
// Occlet.Approach2 — the same two problems with real forecasting algorithms.
//
//   2a  LightGBM quantile regression (Occlet's proposal) via LightGBM's native C API, plus the fixes
//       this problem needs: conformal calibration, no-crossing, and a copula to turn per-horizon
//       distributions into whole daily paths.
//   2b  A probabilistic state-space model (hurdle log-normal spending with an AR(1) weekly "mood",
//       Kalman-filtered, credibility-pooled) — the closer fit for small, path-dependent data.
//
// Both are compared against Approach 1's bootstrap with a rolling-origin back-test; the champion is
// chosen by Occlet's rule (must pass the low-end honesty gate; a challenger must beat the incumbent by
// an agreed margin). Problem 2 (scenarios + decision rules + waterfall) then runs on every model's
// frozen baseline, so we can see whether the recommendation depends on the model.
//
// Usage: dotnet run --project src/Occlet.Approach2 -- [ledger.xlsx] [outDir] [--paths N] [--origins N]
// ─────────────────────────────────────────────────────────────────────────────
using System.Diagnostics;
using System.Text;
using Occlet.Approach2.LightGbm;
using Occlet.Approach2.StateSpace;
using Occlet.Core;
using Occlet.Core.Engine;
using Occlet.Core.Evaluation;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Reporting;
using Occlet.Core.Util;

Fmt.Install();
var valueFlags = new HashSet<string> { "--paths", "--origins" };
var positional = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !valueFlags.Contains(args[i - 1]))).ToList();
string root = FindRepoRoot();
string ledgerPath = positional.ElementAtOrDefault(0) ?? Path.Combine(root, "data", "members_ledger.xlsx");
string outDir = positional.ElementAtOrDefault(1) ?? Path.Combine(root, "output", "approach2");
int Arg(string name, int dflt) { int i = Array.IndexOf(args, name); return i >= 0 ? int.Parse(args[i + 1]) : dflt; }
Settings.Paths = Arg("--paths", Settings.Paths);
int maxOrigins = Arg("--origins", 12);
const double ChallengerMargin = 0.05;   // a challenger must cut scaled pinball loss by ≥ 5% (Occlet: "an agreed margin")
var clock = Stopwatch.StartNew();

var members = LedgerLoader.Load(ledgerPath);
var origin = members.Max(m => m.LastDate);
Console.WriteLine($"Loaded {members.Count} members; forecast origin {origin:yyyy-MM-dd}");

bool lgbmOk = LightGbmNative.IsAvailable(out string lgbmMsg);
Console.WriteLine($"LightGBM: {lgbmMsg}");

var bootstrap = new BootstrapForecaster();
var stateSpace = new StateSpaceForecaster();
using var lightgbm = new LightGbmQuantileForecaster(conformal: true);
using var lightgbmRaw = new LightGbmQuantileForecaster(conformal: false);
var finalists = new List<ISpendForecaster> { bootstrap };
if (lgbmOk) finalists.Add(lightgbm);
finalists.Add(stateSpace);
var backtested = finalists.ToList();
if (lgbmOk) backtested.Insert(2, lightgbmRaw);   // ablation: LightGBM WITHOUT the conformal fix

// ── 1) Rolling-origin back-test of every forecaster ──────────────────────────
Console.WriteLine($"Back-testing {backtested.Count} forecasters over up to {maxOrigins} origins...");
var metrics = new List<BacktestMetric>();
var byMember = new List<(string member, BacktestMetric metric)>();
var lgbmFits = new List<LgbmFitReport>();
var byOrigin = new List<object?[]>();
foreach (var f in backtested)
{
    var t = Stopwatch.StartNew();
    var (pts, wins) = Backtester.Run(members, f, maxOrigins: maxOrigins,
        afterFit: _ => { if (f == lightgbm && lightgbm.LastFit != null) lgbmFits.Add(lightgbm.LastFit); });
    if (args.Contains("--dump-points"))
    {
        Directory.CreateDirectory(outDir);
        File.WriteAllLines(Path.Combine(outDir, $"backtest_points_{Pipeline.Slug(f.Name)}.csv"),
            new[] { "member,origin,horizon,day,actual,q10,q50,q90,ads" }.Concat(pts.Select(p =>
                $"{p.MemberId},{p.Origin:yyyy-MM-dd},{p.Horizon},{p.Day},{p.Actual:0.##},{p.Q10:0.##},{p.Q50:0.##},{p.Q90:0.##},{p.Ads:0.##}")));
    }
    var m = Backtester.Score(pts, wins);
    metrics.AddRange(m);
    byOrigin.AddRange(pts.Where(p => p.Horizon == Settings.Horizon).GroupBy(p => p.Origin).OrderBy(g => g.Key).Select(g => new object?[] {
        f.Name, g.Key, g.Select(p => p.MemberId).Distinct().Count(), g.Count(),
        g.Count(p => p.Actual >= p.Q10 && p.Actual <= p.Q90) / (double)g.Count(), g.Count(p => p.Actual < p.Q10) / (double)g.Count() }));
    byMember.AddRange(Backtester.ScoreByMember(pts, wins));
    Console.WriteLine($"  {f.Name,-13} {t.Elapsed.TotalSeconds,5:0.0}s  " + string.Join("  ", m.Select(x =>
        $"{x.Horizon}d: band {x.Coverage80:P0} below-q10 {x.BelowQ10:P0} pinball {x.ScaledPinball:0.00}")));
}

// ── 2) Champion selection (Occlet's rule) ────────────────────────────────────
var table = finalists.Select(f =>
{
    var ms = metrics.Where(m => m.Forecaster == f.Name).ToList();
    return (f, pinball: ms.Average(m => m.ScaledPinball), lowEndOk: ms.All(m => m.PassesLowEndGate),
            coverOk: ms.Count(m => m.PassesCoverageGate), accepted: ModelSelection.IsEligible(ms), minimumOk: ms.Count(m => m.PassesMinimumGate), belowGap: ms.Average(m => Math.Abs(m.BelowQ10 - 0.10)));
}).ToList();
string? championName = ModelSelection.Select(metrics.Where(m => finalists.Any(f => f.Name == m.Forecaster)), margin: ChallengerMargin);
ISpendForecaster? champion = finalists.FirstOrDefault(f => f.Name == championName);
string why = champion == null
    ? "No model passes endpoint coverage, lower-tail and path-minimum gates at every horizon. Abstain; results are experimental."
    : "Selected by endpoint loss among models passing all endpoint and path-minimum gates; 5% incumbent improvement margin applied.";
Console.WriteLine($"Champion: {champion?.Name ?? "NONE"} — {why}");

// ── 3) Problem 1 + 2 on the latest data, for every finalist ──────────────────
var runs = new List<MemberRun>();
var shap = new List<object?[]>();
var ssParams = new List<object?[]>();
foreach (var f in finalists)
{
    f.Fit(members.Where(m => m.HistoryDays >= Settings.MinHistoryDays).ToList(), origin);
    IReadOnlyList<string> schema = f == lightgbm ? LightGbmQuantileForecaster.FeatureSchema
                                 : f == stateSpace ? StateSpaceForecaster.FeatureSchema : BootstrapForecaster.FeatureSchema;
    foreach (var m in members.Where(m => m.HistoryDays >= Settings.MinHistoryDays))
    {
        var run = Pipeline.RunMember(m, f, origin, Path.Combine(outDir, "artifacts"), schema);
        // Retain hypothetical plans for model comparison, but withhold member advice unless
        // this model and this member's historical cohort both pass validation.
        var memberMetrics = byMember.Where(x => x.member == m.Id && x.metric.Forecaster == f.Name).Select(x => x.metric);
        if (!table.Single(t => t.f == f).accepted || !ModelSelection.IsEligible(memberMetrics))
            run.Plan.BlockRecommendation("Model or member validation gates failed, or insufficient backtest history.");
        runs.Add(run);
        if (f == lightgbm)
            foreach (var (feat, val, contrib) in lightgbm.Explain(BaselineBuilder.Context(m, origin)).Take(6))
                shap.Add(new object?[] { m.Id, m.Profile.Name, feat, val, contrib });
        if (f == stateSpace && stateSpace.LastParams.TryGetValue(m.Id, out var pr))
            ssParams.Add(new object?[] { m.Id, m.Profile.Name, pr.raw.HealthyDays, pr.used.PWeekday, pr.used.PWeekend, Math.Exp(pr.used.MuWeekday),
                Math.Exp(pr.used.MuWeekend), pr.used.SigmaWithin, pr.used.Phi, pr.used.SigmaX, pr.used.StateMean, pr.used.MuStdErr,
                pr.raw.HealthyDays / (pr.raw.HealthyDays + 60.0) });
    }
    Console.WriteLine($"  {f.Name}: " + string.Join("  ", runs.Where(r => r.Baseline.Forecaster == f.Name)
        .Select(r => $"{r.Member.Id} {r.Baseline.Summary.LowestTrueSlack:0.0}→{r.Plan.FinalScore:0.0}")));
}
double[] importance = lgbmOk ? lightgbm.Importance() : Array.Empty<double>();
if (lgbmOk) lightgbm.SaveModels(Path.Combine(outDir, "models"));

// ── 4) Outputs ───────────────────────────────────────────────────────────────
var agreement = members.Where(m => runs.Any(r => r.Member.Id == m.Id)).Select(m =>
{
    var plans = finalists.Select(f => runs.First(r => r.Member.Id == m.Id && r.Baseline.Forecaster == f.Name).Plan).ToList();
    var ids = plans.Select(p => string.Join(" + ", p.Steps.Select(s => s.Lever.Id))).ToList();
    return new object?[] { m.Id, m.Profile.Name }.Concat(ids.Select(x => (object?)(x.Length == 0 ? "(no change)" : x)))
        .Append(ids.Distinct().Count() == 1 ? "YES" : "NO").ToArray();
}).ToList();

void ExtraSheets(Occlet.Core.Io.XlsxWriter x)
{
    x.AddSheet("Model_Selection", new[] { "Forecaster", "AvgScaledPinball", "PassesLowEndGateAllHorizons", "HorizonsPassingCoverageGate", "AvgGapFrom10%BelowQ10", "MinimumHorizonsPassing", "AllGatesPass", "Champion" },
        table.Select(t => new object?[] { t.f.Name, t.pinball, t.lowEndOk, t.coverOk, t.belowGap, t.minimumOk, t.accepted, t.f == champion ? "CHAMPION — " + why : "" }));
    x.AddSheet("Backtest_ByMember", new[] { "MemberID", "Forecaster", "Horizon", "DaysScored", "Coverage80", "BelowQ10", "AboveQ90", "ScaledPinball", "MAE q50 (days)" },
        byMember.Select(b => new object?[] { b.member, b.metric.Forecaster, b.metric.Horizon, b.metric.DaysScored, b.metric.Coverage80, b.metric.BelowQ10, b.metric.AboveQ90, b.metric.ScaledPinball, b.metric.MaeQ50Days }));
    x.AddSheet("Backtest_ByOrigin", new[] { "Forecaster", "Origin", "Members", "DaysScored (90d windows)", "Coverage80", "BelowQ10" }, byOrigin);
    x.AddSheet("Recommendation_Agreement", new[] { "MemberID", "Name" }.Concat(finalists.Select(f => f.Name + " plan")).Append("All models agree"), agreement);
    if (lgbmOk)
    {
        x.AddSheet("LightGBM_Fits", new[] { "Cutoff", "TrainRows", "CalibrationRows", "Members", "ConformalLow 1-7d", "ConformalLow 8-30d", "ConformalLow 31-90d",
                "ConformalHigh 1-7d", "ConformalHigh 8-30d", "ConformalHigh 31-90d", "CopulaLambda", "ObservedCorr(z7,z90)",
                "Cal coverage raw", "Cal coverage calibrated", "Cal spend>q90 raw", "Cal spend>q90 calibrated" },
            lgbmFits.Concat(lightgbm.LastFit is { } lf ? new[] { lf } : Array.Empty<LgbmFitReport>()).Select(r => new object?[] { r.Cutoff, r.TrainRows, r.CalibrationRows, r.Members,
                r.ConformalLow[0], r.ConformalLow[1], r.ConformalLow[2], r.ConformalHigh[0], r.ConformalHigh[1], r.ConformalHigh[2],
                r.CopulaLambda, r.CopulaCorrObserved, r.RawCoverageCal, r.CalibratedCoverageCal, r.RawBelowQ10Cal, r.CalibratedBelowQ10Cal }));
        x.AddSheet("LightGBM_Importance", new[] { "Feature", "Gain importance (q50 model)", "Share" },
            FeatureBuilder.Names.Select((n, i) => (n, g: importance[i])).OrderByDescending(t => t.g)
                .Select(t => new object?[] { t.n, t.g, t.g / Math.Max(importance.Sum(), 1e-9) }));
        x.AddSheet("LightGBM_SHAP_Drivers", new[] { "MemberID", "Name", "Feature", "FeatureValue", "SHAP contribution (× normal pace, next 30d, q50)" }, shap);
    }
    x.AddSheet("StateSpace_Params", new[] { "MemberID", "Name", "HealthyDays", "P(spend) weekday", "P(spend) weekend", "Typical $ weekday", "Typical $ weekend",
            "SigmaWithin", "Phi (weekly AR1)", "SigmaState", "Kalman state today", "Level std error", "Credibility weight" }, ssParams);
}

void ExtraMarkdown(StringBuilder sb)
{
    sb.AppendLine("## Champion selection").AppendLine();
    sb.AppendLine("| Forecaster | Avg scaled pinball (days) | Low-end gate at 30/60/90d | Endpoint coverage passes | Minimum gates pass | Champion |");
    sb.AppendLine("|---|---:|---|---:|---:|---|");
    foreach (var t in table)
        sb.AppendLine($"| {t.f.Name} | {t.pinball:0.00} | {(t.lowEndOk ? "PASS" : "FAIL")} | {t.coverOk}/3 | {t.minimumOk}/3 | {(t.f == champion ? "**yes**" : "")} |");
    sb.AppendLine().AppendLine($"**{champion?.Name ?? "NONE"}** — {why}").AppendLine();
    sb.AppendLine("## Do the models agree on the recommendation?").AppendLine();
    sb.AppendLine("| Member | " + string.Join(" | ", finalists.Select(f => f.Name)) + " | Agree |");
    sb.AppendLine("|---|" + string.Concat(finalists.Select(_ => "---|")) + "---|");
    foreach (var a in agreement) sb.AppendLine("| " + string.Join(" | ", a.Skip(1).Select(v => v?.ToString())) + " |");
    sb.AppendLine();
    if (lgbmOk && lightgbm.LastFit is { } fit)
    {
        sb.AppendLine("## LightGBM diagnostics (final fit)").AppendLine();
        sb.AppendLine($"- {fit.TrainRows:N0} training rows, {fit.CalibrationRows:N0} calibration rows, {fit.Members} members (one global model per quantile).");
        sb.AppendLine($"- Calibration rows inside the raw q10–q90 band: {fit.RawCoverageCal:P0} → after conformal adjustment {fit.CalibratedCoverageCal:P0}. Spend above q90 (spending diagnostic, not replayed balance calibration): {fit.RawBelowQ10Cal:P0} → {fit.CalibratedBelowQ10Cal:P0}.");
        sb.AppendLine($"- Copula: observed correlation of 7-day vs 90-day surprises {fit.CopulaCorrObserved:0.00} → persistent share λ = {fit.CopulaLambda:0.00}.");
        sb.AppendLine("- Top features by gain (q50 model): " + string.Join(", ", FeatureBuilder.Names.Select((n, i) => (n, g: importance[i]))
            .OrderByDescending(t => t.g).Take(6).Select(t => $"`{t.n}` {t.g / importance.Sum():P0}")) + ".");
        sb.AppendLine();
    }
}

ResultWriter.WriteAll(outDir, "Approach 2 — LightGBM and state-space forecasters + scenario engine", runs, metrics, ExtraMarkdown, ExtraSheets);
Console.WriteLine($"Wrote {outDir} in {clock.Elapsed.TotalSeconds:0.0}s");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Occlet.Forecast.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
