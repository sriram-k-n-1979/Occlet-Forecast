using Occlet.Core.Decisioning;
using Occlet.Core.Engine;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Scenarios;

namespace Occlet.Core;

/// <summary>Everything produced for one member by one forecaster.</summary>
public sealed record MemberRun(
    MemberHistory Member, Baseline Baseline, string ArtifactPath, string ArtifactHash,
    IReadOnlyList<Lever> Catalog, IReadOnlyList<string> CatalogNotes,
    IReadOnlyList<ScenarioResult> Singles, ScenarioResult BaselineResult, Plan Plan);

public static class Pipeline
{
    /// <summary>
    /// Problem 1 then Problem 2 for one member:
    /// build + freeze the baseline → write the artifact → list the levers → run each lever alone →
    /// let the decision rules build the plan and its waterfall.
    /// </summary>
    public static MemberRun RunMember(MemberHistory member, ISpendForecaster forecaster, DateOnly origin,
                                      string artifactDir, IReadOnlyList<string> featureSchema)
    {
        var baseline = BaselineBuilder.Build(member, origin, forecaster);
        string artifactPath = Path.Combine(artifactDir, $"baseline_{member.Id}_{Slug(forecaster.Name)}.json");
        string artifactHash = ArtifactWriter.Write(baseline, artifactPath, featureSchema);

        var engine = new ScenarioEngine(baseline);
        var (catalog, notes) = LeverCatalog.Build(baseline);
        var baseRes = engine.Run(Array.Empty<Lever>());
        var singles = catalog.Select(l => engine.Run(new[] { l })).ToList();
        var plan = Recommender.Decide(engine, catalog);
        plan.MonthlySurplus = LeverCatalog.MonthlySurplus(baseline);
        if (plan.StructuralGap) plan.Status += $" Structural gap: typical spending exceeds income by ${-plan.MonthlySurplus:N0}/month.";
        return new MemberRun(member, baseline, artifactPath, artifactHash, catalog, notes, singles, baseRes, plan);
    }

    public static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
