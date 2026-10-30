using Occlet.Core.Engine;
using Occlet.Core.Forecasting;

namespace Occlet.Core.Scenarios;

/// <summary>Occlet's lever hierarchy, highest priority first.</summary>
public enum Tier { Behaviour = 1, Product = 2, Liquidity = 3, Income = 4 }

/// <summary>Exact = pure arithmetic on cash events. Learned = depends on how people react (estimated, wider band).</summary>
public enum LeverMethod { Exact, Learned }

/// <summary>One change we could make. <see cref="Apply"/> edits a COPY of the baseline's inputs.</summary>
public sealed class Lever
{
    public required string Id { get; init; }
    public required string Label { get; init; }          // specialist wording
    public required string MemberText { get; init; }     // member wording ("move your phone bill to the 9th")
    public required Tier Tier { get; init; }
    public required LeverMethod Method { get; init; }
    public required Action<ScenarioState> Apply { get; init; }
    public Func<ScenarioState, (bool pass, string why)>? Gate { get; init; }  // e.g. ability-to-repay
    public bool Surfaced { get; init; } = true;          // stress tests are shown, never recommended
    public bool RequiresConsent { get; init; }           // someone else must agree (landlord, lender) — tried after self-service levers
    public string Caveat { get; init; } = "";
    public override string ToString() => Id;
}

/// <summary>The mutable working copy a scenario edits: events, product rules, behaviour, and loan bookkeeping.</summary>
public sealed class ScenarioState
{
    public Baseline Baseline { get; }
    public List<FutureEvent> Events { get; }
    public ProductRules Rules { get; set; }
    public Behaviour Behaviour { get; set; }
    public double ExtraDailySpend { get; set; }   // loan repayments counted as spending (true-slack denominator)
    public List<string> Notes { get; } = new();
    public List<(string lever, bool pass, string why)> Gates { get; } = new();

    public ScenarioState(Baseline b)
    {
        Baseline = b;
        Events = b.Events.ToList();                 // a copy — the frozen baseline is never touched
        Rules = b.Rules; Behaviour = b.Behaviour;
    }

    public int NextPaydayAfter(int day) => Events.Where(e => e.IsPayday && e.Day > day).Select(e => e.Day).DefaultIfEmpty(-1).Min();
    public int PrevPaydayOnOrBefore(int day) => Events.Where(e => e.IsPayday && e.Day <= day).Select(e => e.Day).DefaultIfEmpty(-999).Max();
    public FutureEvent MoveTo(FutureEvent e, int newDay)
    {
        newDay = Math.Clamp(newDay, 0, Baseline.Horizon - 1);
        return e with { Day = newDay, Date = Baseline.DateAt(newDay) };
    }
}

public sealed class ScenarioResult
{
    public required IReadOnlyList<Lever> Levers { get; init; }
    public required SlackSummary Summary { get; init; }
    public required ReplayResult Result { get; init; }
    public required ScenarioState State { get; init; }
    public string Key => Levers.Count == 0 ? "baseline" : string.Join(" + ", Levers.Select(l => l.Id));
    public bool GatesPass => State.Gates.All(g => g.pass);
}

/// <summary>
/// Problem 2's skeleton, exactly as Occlet's doc describes it:
/// copy the frozen baseline → apply one or more levers → replay the SAME futures → compare.
/// The Phase 1 forecaster is never called again.
/// </summary>
public sealed class ScenarioEngine
{
    private readonly Baseline _b;
    private readonly Dictionary<string, ScenarioResult> _cache = new();
    public ScenarioEngine(Baseline b) => _b = b;
    public Baseline Baseline => _b;

    public ScenarioResult Run(IReadOnlyList<Lever> levers)
    {
        string key = string.Join("+", levers.Select(l => l.Id));
        if (_b.ComputeHash() != _b.FrozenHash)
            throw new InvalidOperationException("The frozen baseline was modified — scenarios must only edit copies.");

        if (_cache.TryGetValue(key, out var hit)) return hit;
        var st = new ScenarioState(_b);
        foreach (var lever in levers)
        {
            lever.Apply(st);

        }
        // Evaluate affordability after every edit has been applied to the candidate.
        foreach (var lever in levers)
            if (lever.Gate != null) { var g = lever.Gate(st); st.Gates.Add((lever.Id, g.pass, g.why)); }
        var events = st.Events.OrderBy(e => e.Day).ThenByDescending(e => e.Amount).ToList();
        var r = Replay.Run(new Replay.Input(_b.StartBalance, _b.StartSavings, events, _b.Horizon, _b.Everyday, _b.AlertU,
                                            _b.LeverU, _b.EventZ, _b.EventKeyIndex, st.Rules, st.Behaviour, _b.ItemLevel, _b.ItemIndex, _b.CardFractions));
        var res = new ScenarioResult { Levers = levers.ToList(), Summary = SlackSummary.From(r, _b.Ads, st.ExtraDailySpend), Result = r, State = st };
        _cache[key] = res;
        return res;
    }

    /// <summary>Per-future difference in lowest TRUE slack between two scenarios (same futures, so it is a fair paired comparison).</summary>
    public static double[] PairedGain(ScenarioResult after, ScenarioResult before) =>
        after.Summary.PathLowestTrueSlack.Zip(before.Summary.PathLowestTrueSlack, (a, b) => a - b).ToArray();
}
