// ─────────────────────────────────────────────────────────────────────────────
// Occlet.Approach1 — the RUDIMENTARY solution to both problems.
//
//   Problem 1  Baseline forecast ("if nothing changes"): for each member, forecast the daily available
//              balance for 90 days as three lines (q10 pessimistic / q50 likely / q90 optimistic),
//              derive slack, explain the dip, freeze the result as a versioned artifact.
//   Problem 2  Scenario engine ("what if…?"): copy the frozen baseline, apply levers, replay the same
//              futures, compute TRUE slack, and let the four decision rules pick a plan + waterfall.
//
// Forecaster: BootstrapForecaster (re-uses real past days; no model).
// Usage:  dotnet run --project src/Occlet.Approach1 -- [ledger.xlsx] [outDir] [--paths N] [--no-backtest]
// ─────────────────────────────────────────────────────────────────────────────
using System.Diagnostics;
using Occlet.Core;
using Occlet.Core.Evaluation;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Reporting;

Occlet.Core.Util.Fmt.Install();

var positional = args.Where(a => !a.StartsWith("--")).ToList();
string root = FindRepoRoot();
string ledgerPath = positional.ElementAtOrDefault(0) ?? Path.Combine(root, "data", "members_ledger.xlsx");
string outDir = positional.ElementAtOrDefault(1) ?? Path.Combine(root, "output", "approach1");
int pathsArg = Array.IndexOf(args, "--paths");
if (pathsArg >= 0) Settings.Paths = int.Parse(args[pathsArg + 1]);
bool backtest = !args.Contains("--no-backtest");
var clock = Stopwatch.StartNew();

// 1) Read the ledger: one MemberHistory per member (Ledger + Members sheets)
var members = LedgerLoader.Load(ledgerPath);
var origin = members.Max(m => m.LastDate);                 // "today" = the extract date
Console.WriteLine($"Loaded {members.Count} members from {ledgerPath}; forecast origin {origin:yyyy-MM-dd}");

// 2) Problem 1 + Problem 2 for every member with enough history
var forecaster = new BootstrapForecaster();
forecaster.Fit(members, origin);
var runs = new List<MemberRun>();
foreach (var m in members)
{
    if (m.HistoryDays < Settings.MinHistoryDays) { Console.WriteLine($"  {m.Id}: only {m.HistoryDays} days of history — skipped"); continue; }
    var run = Pipeline.RunMember(m, forecaster, origin, Path.Combine(outDir, "artifacts"), BootstrapForecaster.FeatureSchema);
    runs.Add(run);
    var s = run.Baseline.Summary;
    Console.WriteLine($"  {m.Id} {m.Profile.Name,-5} headline90={s.Headline[90],5:0.0}d  lowestTrue={s.LowestTrueSlack,5:0.0}d  " +
                      $"P(od)={s.POverdraft,4:P0}  fees=${s.ExpFeeDollars,4:N0}  → {run.Plan.Waterfall()}");
}

// 3) Honesty check: rolling-origin back-test of the bootstrap
var metrics = new List<BacktestMetric>();
if (backtest)
{
    Console.WriteLine("Back-testing (rolling origins, 2 weeks apart)...");
    var (points, windows) = Backtester.Run(members, forecaster);
    metrics = Backtester.Score(points, windows);
    foreach (var mt in metrics)
        Console.WriteLine($"  {mt.Horizon}d: in 80% band {mt.Coverage80:P0} (gate 75-85%: {(mt.PassesCoverageGate ? "PASS" : "FAIL")}), " +
                          $"below q10 {mt.BelowQ10:P0} (target ~10%: {(mt.PassesLowEndGate ? "PASS" : "FAIL")}), scaled pinball {mt.ScaledPinball:0.00} days");
}

// 4) Write results.xlsx, charts/*.svg, artifacts/*.json and REPORT.md
ResultWriter.WriteAll(outDir, "Approach 1 — rudimentary bootstrap forecaster + scenario engine", runs, metrics);
Console.WriteLine($"Wrote {outDir} in {clock.Elapsed.TotalSeconds:0.0}s");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Occlet.Forecast.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
