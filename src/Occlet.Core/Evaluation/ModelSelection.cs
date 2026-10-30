namespace Occlet.Core.Evaluation;

public static class ModelSelection
{
    public static bool IsEligible(IEnumerable<BacktestMetric> metrics)
    {
        var rows = metrics.ToList();
        return Settings.ReportHorizons.All(h => rows.Any(m => m.Horizon == h)) && rows.All(m => m.PassesAllGates);
    }

    public static string? Select(IEnumerable<BacktestMetric> metrics, string incumbent = "Bootstrap", double margin = 0.05)
    {
        var eligible = metrics.GroupBy(m => m.Forecaster).Where(IsEligible)
            .Select(g => (name: g.Key, loss: g.Average(m => m.ScaledPinball))).OrderBy(x => x.loss).ToList();
        if (eligible.Count == 0) return null;
        var best = eligible[0];
        var current = eligible.FirstOrDefault(x => x.name == incumbent);
        return current.name != null && best.loss > current.loss * (1 - margin) ? incumbent : best.name;
    }
}
