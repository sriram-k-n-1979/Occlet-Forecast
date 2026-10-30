using System.Globalization;
using Occlet.Core.Scenarios;

namespace Occlet.Core.Decisioning;

public sealed record CandidateLog(int Round, string LeverId, string Label, Tier Tier, LeverMethod Method,
                                  double MedianGain, double ScoreGain, double PPositive, bool Accepted, string Reason);

public sealed record PlanStep(Lever Lever, double ScoreBefore, double ScoreAfter, double MedianPairedGain, double PPositive)
{
    public double Delta => ScoreAfter - ScoreBefore;
}

public sealed class Plan
{
    public required ScenarioResult Baseline { get; init; }
    public ScenarioResult Final { get; set; } = null!;
    public List<PlanStep> Steps { get; } = new();
    public List<CandidateLog> Log { get; } = new();
    public string Status { get; set; } = "";
    public string? ValidationBlock { get; private set; }
    public void BlockRecommendation(string reason) { ValidationBlock = reason; Status = "UNVALIDATED: " + reason + " " + Status; }
    /// <summary>Set when typical spending exceeds income: levers can move dates, not close the gap.</summary>
    public double MonthlySurplus { get; set; }
    public bool StructuralGap => MonthlySurplus < 0;
    public bool GoalMet => Final.Summary.LowestTrueSlack >= Settings.SlackGoalDays;
    public double BaselineScore => Baseline.Summary.LowestTrueSlack;
    public double FinalScore => Final.Summary.LowestTrueSlack;

    /// <summary>Specialist view, in Occlet's deck format: "12.0 days baseline → +3.1 (move phone bill) → … → 18.1 days".</summary>
    public string Waterfall()
    {
        var parts = new List<string> { $"{BaselineScore:0.0} days baseline" };
        parts.AddRange(Steps.Select(s => $"{(s.Delta >= 0 ? "+" : "")}{s.Delta:0.0} ({s.Lever.Label})"));
        parts.Add($"{FinalScore:0.0} days");
        return string.Join(" → ", parts);
    }

    /// <summary>Member view: one plain sentence.</summary>
    public string MemberSentence()
    {
        if (ValidationBlock != null) return "No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.";
        var b = Baseline.Summary; var f = Final.Summary;
        if (Steps.Count == 0)
            return BaselineScore >= Settings.SlackGoalDays
                ? $"You keep about {b.LowestTrueSlack:0.0} days of spending in hand even on your tightest day over the next 90 days — no change needed."
                : StructuralGap
                    ? $"Your usual spending runs about ${-MonthlySurplus:N0} a month above your income, so moving dates alone cannot fix the tight days (about {b.LowestTrueSlack:0.0} days of cushion now); a specialist can go through a budget and other options with you."
                    : $"None of the available changes meaningfully lifts your tightest day (about {b.LowestTrueSlack:0.0} days of cushion); a specialist can review other options with you.";
        string what = JoinAnd(Steps.Select(s => s.Lever.MemberText).ToList());
        string fees = b.ExpFeeDollars - f.ExpFeeDollars >= 5
            ? $", and expected overdraft fees drop from about ${b.ExpFeeDollars:N0} to ${f.ExpFeeDollars:N0}" : "";
        string shortfall = GoalMet ? "" : StructuralGap
            ? $" Because your usual spending runs about ${-MonthlySurplus:N0} a month above your income, that is still short of the {Settings.SlackGoalDays:0}-day goal — worth a budget conversation with a specialist."
            : $" That is still short of the {Settings.SlackGoalDays:0}-day goal, so it is worth a conversation with a specialist.";
        string consent = Steps.Any(s => s.Lever.Id == "move_rent") ? " (if your landlord agrees)" : "";
        return $"If you {what}{consent}, your tightest day over the next 90 days goes from about {b.LowestTrueSlack:0.0} to {f.LowestTrueSlack:0.0} days of spending in hand{fees}.{shortfall}";
    }

    private static string JoinAnd(List<string> items) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}

/// <summary>
/// "How it decides what to recommend" — the four rules, implemented literally.
///  1. Try behaviour-only first. If that gives enough cushion, stop there.
///  2. Only move down to product, then loan, then income if needed.
///  3. A lower-priority option only wins if it beats what the higher-priority levers already achieve
///     by a clear margin of TRUE slack (≥ 1.0 day) and with high confidence (gain &gt; 0 in ≥ 80% of futures).
///  4. Always show the breakdown (the waterfall).
/// The hierarchy lives here, in decisioning — not in model weights.
/// </summary>
public static class Recommender
{
    private static readonly Tier[] Order = { Tier.Behaviour, Tier.Product, Tier.Liquidity, Tier.Income };

    public static Plan Decide(ScenarioEngine engine, IReadOnlyList<Lever> catalog)
    {
        var baseline = engine.Run(Array.Empty<Lever>());
        var plan = new Plan { Baseline = baseline, Final = baseline };
        double goal = Settings.SlackGoalDays;
        if (baseline.Summary.LowestTrueSlack >= goal)
        {
            plan.Status = $"Goal already met at baseline ({baseline.Summary.LowestTrueSlack:0.0} ≥ {goal:0.0} days) — no lever needed.";
            return plan;
        }

        var chosen = new List<Lever>();
        var current = baseline;
        int round = 0;
        foreach (var tier in Order)                                                     // Rule 2: tiers strictly in order
        {
            if (current.Summary.LowestTrueSlack >= goal) break;                         // Rule 1: stop as soon as the goal is met
            // within a tier, levers the member controls come before ones that need someone else's consent
            foreach (bool consentStage in new[] { false, true })
            {
            var pool = catalog.Where(l => l.Tier == tier && l.Surfaced && l.RequiresConsent == consentStage).ToList();
            while (pool.Count > 0 && current.Summary.LowestTrueSlack < goal)
            {
                round++;
                var evaluated = new List<(Lever lever, ScenarioResult res, double gain, double pPos, bool ok, string why)>();
                foreach (var lever in pool)
                {
                    var res = engine.Run(chosen.Append(lever).ToList());
                    var gain = ScenarioEngine.PairedGain(res, current);
                    double med = Util.Stats.Median(gain), pPos = gain.Count(g => g > 1e-9) / (double)gain.Length;
                    double scoreGain = res.Summary.LowestTrueSlack - current.Summary.LowestTrueSlack;
                    var (ok, why) = Judge(tier, res, current, scoreGain, pPos);
                    evaluated.Add((lever, res, scoreGain, pPos, ok, why));
                    plan.Log.Add(new CandidateLog(round, lever.Id, lever.Label, tier, lever.Method, med, scoreGain, pPos, false, why));
                }
                // best acceptable lever this round: biggest gain, exact before learned on ties
                var best = evaluated.Where(e => e.ok).OrderByDescending(e => Math.Round(e.gain, 2))
                                    .ThenBy(e => e.lever.Method == LeverMethod.Exact ? 0 : 1).FirstOrDefault();
                if (best.lever == null) break;
                int logIdx = plan.Log.FindLastIndex(l => l.Round == round && l.LeverId == best.lever.Id);
                plan.Log[logIdx] = plan.Log[logIdx] with { Accepted = true, Reason = "ACCEPTED — " + plan.Log[logIdx].Reason };
                var gainVec = ScenarioEngine.PairedGain(best.res, current);
                plan.Steps.Add(new PlanStep(best.lever, current.Summary.LowestTrueSlack, best.res.Summary.LowestTrueSlack,
                                            Util.Stats.Median(gainVec), best.pPos));
                chosen.Add(best.lever);
                current = best.res;
                pool.Remove(best.lever);
            }
            }
        }
        plan.Final = current;
        plan.Status = plan.GoalMet
            ? $"Goal met in tier '{plan.Steps[^1].Lever.Tier}' after {plan.Steps.Count} lever(s)."
            : plan.Steps.Count == 0 ? "No lever passed the rules; baseline stands." : "Goal not met; best partial plan shown.";
        return plan;
    }

    private static (bool ok, string why) Judge(Tier tier, ScenarioResult res, ScenarioResult current, double gain, double pPos)
    {
        var ci = Util.Fmt.Culture;
        var failed = res.State.Gates.FirstOrDefault(g => !g.pass);
        if (failed.lever != null) return (false, $"gate failed ({failed.lever}): {failed.why}");
        if (res.Summary.ExpUnpaidBills > current.Summary.ExpUnpaidBills + 0.01)
            return (false, "increases outstanding unpaid obligations; specialist review required");
        double extraDeclines = res.Summary.ExpBillDeclines - current.Summary.ExpBillDeclines;
        if (extraDeclines > Settings.MaxExtraBillDeclines)
            return (false, string.Format(ci, "turns fees into ~{0:0.0} declined bill payments per 90 days — needs a member conversation, not auto-adoption", extraDeclines));
        if (tier == Tier.Behaviour)
            return gain >= Settings.MinBehaviourGain
                ? (true, string.Format(ci, "behaviour lever adds {0:+0.00;-0.00} days", gain))
                : (false, string.Format(ci, "adds only {0:+0.00;-0.00} days (< {1:0.0})", gain, Settings.MinBehaviourGain));
        // Rule 3: lower priority must beat the higher-priority plan by a clear margin, with high confidence
        if (gain < Settings.ArbitrationMarginDays)
            return (false, string.Format(ci, "beats the higher-priority plan by only {0:+0.00;-0.00} days (< {1:0.0}-day margin)", gain, Settings.ArbitrationMarginDays));
        if (pPos < Settings.HighConfidence)
            return (false, string.Format(ci, "gain positive in only {0:P0} of futures (< {1:P0} confidence)", pPos, Settings.HighConfidence));
        return (true, string.Format(ci, "beats higher-priority plan by {0:+0.00} days, positive in {1:P0} of futures", gain, pPos));
    }
}
