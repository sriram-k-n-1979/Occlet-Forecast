using System.Globalization;
using System.Text;
using Occlet.Core.Engine;
using Occlet.Core.Evaluation;
using Occlet.Core.Io;
using Occlet.Core.Scenarios;

namespace Occlet.Core.Reporting;

/// <summary>Writes results.xlsx, the SVG charts and REPORT.md for a set of member runs.</summary>
public static class ResultWriter
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static void WriteAll(string outDir, string title, IReadOnlyList<MemberRun> runs,
                                IReadOnlyList<BacktestMetric> backtest,
                                Action<StringBuilder>? extraMarkdown = null, Action<XlsxWriter>? extraSheets = null)
    {
        Directory.CreateDirectory(outDir);
        var chartsDir = Path.Combine(outDir, "charts");
        Directory.CreateDirectory(chartsDir);
        foreach (var r in runs)
        {
            string slug = Pipeline.Slug(r.Baseline.Forecaster);
            File.WriteAllText(Path.Combine(chartsDir, $"{r.Member.Id}_{slug}_forecast.svg"),
                SvgCharts.Fan(r.Baseline, r.Plan.Final.Summary, $"{r.Member.Profile.Name} — 90-day available balance ({r.Baseline.Forecaster})",
                              r.Plan.Steps.Count == 0 ? "No change" : r.Plan.ValidationBlock != null ? "Experimental scenario" : "Recommended plan"));
            File.WriteAllText(Path.Combine(chartsDir, $"{r.Member.Id}_{slug}_waterfall.svg"),
                SvgCharts.Waterfall(r.Plan, $"{r.Member.Profile.Name} — waterfall ({r.Baseline.Forecaster})"));
        }

        var x = new XlsxWriter();
        x.AddSheet("Summary", new[] { "MemberID", "Name", "Forecaster", "Origin", "StartBalance", "AvgDailySpend",
                "HeadlineSlack30d", "HeadlineSlack60d", "HeadlineSlack90d", "TrueSlack90d", "LowestTrueSlack(median future)",
                "LowestTrueSlack(p10 future)", "RiskDays(q10<0)", "P(any overdraft)", "ExpectedFees$_90d", "PlanStatus",
                "PlanLowestTrueSlack", "Waterfall", "MemberSentence", "Explanation", "ArtifactHash" },
            runs.Select(r => { var s = r.Baseline.Summary; return new object?[] {
                r.Member.Id, r.Member.Profile.Name, r.Baseline.Forecaster, r.Baseline.Origin, r.Baseline.StartBalance, r.Baseline.Ads,
                s.Headline.GetValueOrDefault(30), s.Headline.GetValueOrDefault(60), s.Headline.GetValueOrDefault(90), s.TrueSlack.GetValueOrDefault(90),
                s.LowestTrueSlack, s.LowestTrueSlackP10, s.RiskDays, s.POverdraft, s.ExpFeeDollars, r.Plan.Status,
                r.Plan.FinalScore, r.Plan.Waterfall(), r.Plan.MemberSentence(), Explainer.Dip(r.Baseline), r.ArtifactHash }; }));

        x.AddSheet("Baseline_Paths", new[] { "MemberID", "Forecaster", "Date", "q10_pessimistic", "q50_likely", "q90_optimistic", "Plan_q50", "KnownEvents" },
            runs.SelectMany(r => Enumerable.Range(0, r.Baseline.Horizon).Select(d => new object?[] {
                r.Member.Id, r.Baseline.Forecaster, r.Baseline.DateAt(d), r.Baseline.Summary.Q10[d], r.Baseline.Summary.Q50[d], r.Baseline.Summary.Q90[d],
                r.Plan.Final.Summary.Q50[d],
                string.Join("; ", r.Baseline.Events.Where(e => e.Day == d).Select(e => $"{e.Description} {e.Amount.ToString("+#,##0;-#,##0", Ci)}")) })));

        x.AddSheet("Recurring_Detected", new[] { "MemberID", "Forecaster", "Kind", "Description", "Category", "Cadence", "DayOfMonth", "PeriodDays", "Amount", "AmountCV", "Occurrences", "LastSeen" },
            runs.SelectMany(r => r.Baseline.Recurring.Select(i => new object?[] {
                r.Member.Id, r.Baseline.Forecaster, i.Kind.ToString(), i.Description, i.Category, i.Cadence.ToString(), i.DayOfMonth, i.PeriodDays, i.Amount, i.AmountCv, i.Occurrences, i.LastSeen })));

        x.AddSheet("Levers_Alone", new[] { "MemberID", "Forecaster", "Lever", "Label", "Tier", "Method", "Surfaced", "LowestTrueSlack", "Δ vs baseline",
                "P(gain>0)", "HeadlineSlack90d", "TrueSlack90d", "RiskDays", "P(overdraft)", "ExpFees$", "ExpBillDeclines", "ExpCardDeclined$", "ExpSavingsPulled$", "Gate", "Caveat", "Unpaid bills at horizon" },
            runs.SelectMany(r => r.Singles.Select(sr =>
            {
                var l = sr.Levers[0]; var s = sr.Summary; var g = ScenarioEngine.PairedGain(sr, r.BaselineResult);
                return new object?[] { r.Member.Id, r.Baseline.Forecaster, l.Id, l.Label, l.Tier.ToString(), l.Method.ToString(), l.Surfaced,
                    s.LowestTrueSlack, s.LowestTrueSlack - r.BaselineResult.Summary.LowestTrueSlack, g.Count(v => v > 1e-9) / (double)g.Length,
                    s.Headline.GetValueOrDefault(90), s.TrueSlack.GetValueOrDefault(90), s.RiskDays, s.POverdraft, s.ExpFeeDollars,
                    s.ExpBillDeclines, s.ExpCardDeclined, s.ExpSavingsPulled,
                    string.Join("; ", sr.State.Gates.Select(x => $"{(x.pass ? "PASS" : "FAIL")}: {x.why}")), l.Caveat, s.ExpUnpaidBills };
            })));

        x.AddSheet("Plan_Waterfall", new[] { "MemberID", "Forecaster", "Step", "Lever", "Label", "Tier", "Method", "Δ days", "Running total", "Median paired gain", "P(gain>0)" },
            runs.SelectMany(r =>
            {
                var rows = new List<object?[]> { new object?[] { r.Member.Id, r.Baseline.Forecaster, 0, "baseline", "Baseline", "", "", null, r.Plan.BaselineScore, null, null } };
                rows.AddRange(r.Plan.Steps.Select((s, i) => new object?[] { r.Member.Id, r.Baseline.Forecaster, i + 1, s.Lever.Id, s.Lever.Label,
                    s.Lever.Tier.ToString(), s.Lever.Method.ToString(), s.Delta, s.ScoreAfter, s.MedianPairedGain, s.PPositive }));
                rows.Add(new object?[] { r.Member.Id, r.Baseline.Forecaster, r.Plan.Steps.Count + 1, "plan", r.Plan.ValidationBlock != null ? "Experimental scenario" : "Recommended plan", "", "", null, r.Plan.FinalScore, null, null });
                return rows;
            }));

        x.AddSheet("Decision_Log", new[] { "MemberID", "Forecaster", "Round", "Lever", "Tier", "Method", "ScoreGain(days)", "MedianPairedGain", "P(gain>0)", "Accepted", "Reason" },
            runs.SelectMany(r => r.Plan.Log.Select(l => new object?[] { r.Member.Id, r.Baseline.Forecaster, l.Round, l.LeverId, l.Tier.ToString(),
                l.Method.ToString(), l.ScoreGain, l.MedianGain, l.PPositive, l.Accepted, l.Reason })));

        x.AddSheet("Catalog_Notes", new[] { "MemberID", "Forecaster", "Note" },
            runs.SelectMany(r => r.CatalogNotes.Select(n => new object?[] { r.Member.Id, r.Baseline.Forecaster, n })));

        if (backtest.Count > 0)
            x.AddSheet("Backtest", new[] { "Forecaster", "Horizon", "Windows", "DaysScored", "EndpointCoverage80 (target 75-85%)", "EndpointBelow q10 (target ~10%)", "Above q90",
                    "ScaledPinball (days)", "MAE q50 (days)", "LowestBalance coverage80", "LowestBalance below q10", "CoverageGate", "LowEndGate", "EndpointsScored", "WindowCoverage80", "WindowBelowQ10", "WindowScaledPinball", "MinimumGate", "AllGates" },
                backtest.Select(m => new object?[] { m.Forecaster, m.Horizon, m.Windows, m.DaysScored, m.Coverage80, m.BelowQ10, m.AboveQ90,
                    m.ScaledPinball, m.MaeQ50Days, m.MinCoverage80, m.MinBelowQ10, m.PassesCoverageGate ? "PASS" : "FAIL", m.PassesLowEndGate ? "PASS" : "FAIL", m.EndpointsScored, m.WindowCoverage80, m.WindowBelowQ10, m.WindowScaledPinball, m.PassesMinimumGate, m.PassesAllGates }));
        File.WriteAllText(Path.Combine(outDir, "metrics.json"), System.Text.Json.JsonSerializer.Serialize(backtest,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        extraSheets?.Invoke(x);
        x.Save(Path.Combine(outDir, "results.xlsx"));

        File.WriteAllText(Path.Combine(outDir, "REPORT.md"), Markdown(title, runs, backtest, extraMarkdown));
    }

    private static string Markdown(string title, IReadOnlyList<MemberRun> runs, IReadOnlyList<BacktestMetric> backtest, Action<StringBuilder>? extra)
    {
        var sb = new StringBuilder();
        var origin = runs[0].Baseline.Origin;
        sb.AppendLine($"# {title}").AppendLine();
        sb.AppendLine($"Forecast origin **{origin:yyyy-MM-dd}**, horizon {Settings.Horizon} days, {runs[0].Baseline.Paths:N0} simulated futures per member, seed {Settings.Seed}. All data is illustrative.").AppendLine();
        sb.AppendLine("Balances are posted-balance proxies: holds and pending authorizations are not supplied. Ticket sizes are sampled from historical within-day proportions; they are a modelling assumption. Plans marked UNVALIDATED are hypothetical comparisons, not member advice.").AppendLine();
        sb.AppendLine("Score used by the decision rules: **lowest TRUE slack** = on the tightest day of each simulated future, (balance − borrowed principal still owed) ÷ (average daily spend + loan repayments); the median across futures. Goal: ≥ " +
                      $"{Settings.SlackGoalDays:0.0} days.").AppendLine();
        sb.AppendLine("| Member | Forecaster | Headline slack 90d | Lowest true slack | P(overdraft) | Exp. fees 90d | Waterfall |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var r in runs)
        {
            var s = r.Baseline.Summary;
            sb.AppendLine($"| {r.Member.Profile.Name} | {r.Baseline.Forecaster} | {s.Headline[90]:0.0} | {s.LowestTrueSlack:0.0} | {s.POverdraft:P0} | ${s.ExpFeeDollars:N0} | {r.Plan.Waterfall()} |");
        }
        sb.AppendLine();
        if (backtest.Count > 0)
        {
            sb.AppendLine("## Back-test (rolling origins, 2 weeks apart)").AppendLine();
            sb.AppendLine("| Forecaster | Horizon | Endpoints scored | In 80% band (gate 75–85%) | Below q10 (target ≈10%) | Scaled pinball (days) | Lowest-balance below q10 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var m in backtest)
                sb.AppendLine($"| {m.Forecaster} | {m.Horizon}d | {m.EndpointsScored:N0} | {m.Coverage80:P1} {(m.PassesCoverageGate ? "✅" : "❌")} | {m.BelowQ10:P1} {(m.PassesLowEndGate ? "✅" : "❌")} | {m.ScaledPinball:0.00} | {m.MinBelowQ10:P1} |");
            sb.AppendLine().AppendLine("Endpoint metrics score only days 30, 60 and 90. The following window metrics average all days 1 through H and are separate diagnostics. Origins overlap; these are not independent samples.").AppendLine();
            sb.AppendLine("| Forecaster | Horizon | Window coverage | Window below q10 | Window pinball | Minimum coverage | All gates |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---|");
            foreach (var m in backtest)
                sb.AppendLine($"| {m.Forecaster} | {m.Horizon} | {m.WindowCoverage80:P1} | {m.WindowBelowQ10:P1} | {m.WindowScaledPinball:0.00} | {m.MinCoverage80:P1} | {(m.PassesAllGates ? "PASS" : "FAIL")} |");
            sb.AppendLine();
        }
        extra?.Invoke(sb);
        foreach (var r in runs)
        {
            var s = r.Baseline.Summary; string slug = Pipeline.Slug(r.Baseline.Forecaster);
            sb.AppendLine($"## {r.Member.Profile.Name} ({r.Member.Id}) — {r.Baseline.Forecaster}").AppendLine();
            if (!string.IsNullOrWhiteSpace(r.Member.Profile.Notes)) sb.AppendLine($"*{r.Member.Profile.Notes}*").AppendLine();
            sb.AppendLine($"- Start balance ${r.Baseline.StartBalance:N0}, savings ${r.Baseline.StartSavings:N0}, average daily spend ${r.Baseline.Ads:N2}.");
            sb.AppendLine($"- Headline slack 30/60/90 d: {s.Headline[30]:0.0} / {s.Headline[60]:0.0} / {s.Headline[90]:0.0} days. Lowest true slack: **{s.LowestTrueSlack:0.0}** (bad case p10 {s.LowestTrueSlackP10:0.0}). Risk days: {s.RiskDays}. P(any overdraft): {s.POverdraft:P0}. Expected fees: ${s.ExpFeeDollars:N0}. Outstanding unpaid bills at horizon: ${s.ExpUnpaidBills:N0}.");
            sb.AppendLine($"- Explanation: {Explainer.Dip(r.Baseline)}");
            sb.AppendLine($"- **Decision:** {r.Plan.Status}");
            sb.AppendLine($"- **Waterfall:** {r.Plan.Waterfall()}");
            sb.AppendLine($"- **Member sentence:** “{r.Plan.MemberSentence()}”").AppendLine();
            sb.AppendLine($"![forecast](charts/{r.Member.Id}_{slug}_forecast.svg)").AppendLine();
            sb.AppendLine($"![waterfall](charts/{r.Member.Id}_{slug}_waterfall.svg)").AppendLine();
            sb.AppendLine("| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|");
            foreach (var sr in r.Singles)
            {
                var l = sr.Levers[0];
                sb.AppendLine($"| {l.Label}{(l.Surfaced ? "" : " *(not surfaced)*")} | {l.Tier} | {l.Method} | {sr.Summary.LowestTrueSlack:0.0} | {sr.Summary.LowestTrueSlack - r.BaselineResult.Summary.LowestTrueSlack:+0.0;−0.0;0.0} | ${sr.Summary.ExpFeeDollars:N0} | {sr.Summary.ExpBillDeclines:0.0} | {string.Join("; ", sr.State.Gates.Select(g => (g.pass ? "PASS " : "FAIL ") + g.why))} |");
            }
            sb.AppendLine();
            var rejected = r.Plan.Log.Where(l => !l.Accepted).GroupBy(l => l.LeverId).Select(g => g.Last()).ToList();
            if (rejected.Count > 0)
            {
                sb.AppendLine("Why the other levers were not added:").AppendLine();
                foreach (var l in rejected) sb.AppendLine($"- `{l.LeverId}` — {l.Reason}");
                sb.AppendLine();
            }
            if (r.CatalogNotes.Count > 0) { sb.AppendLine("Catalogue notes: " + string.Join(" ", r.CatalogNotes)).AppendLine(); }
        }
        return sb.ToString();
    }
}
