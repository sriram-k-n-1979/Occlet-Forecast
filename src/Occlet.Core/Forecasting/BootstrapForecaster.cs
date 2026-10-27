using Occlet.Core.Ledger;

namespace Occlet.Core.Forecasting;

/// <summary>
/// APPROACH 1 — the rudimentary forecaster. No model, no training.
///
/// To imagine one possible future, pick a real past day of this member's everyday spending at random
/// for each future day (a weekday from past weekdays, a weekend day from past weekends). Do that 2,000
/// times and you have 2,000 plausible futures. This is a "bootstrap".
///
/// One refinement: some months are simply heavier than others, so each future also gets its own
/// "spending mood" — a multiplier drawn from how much this member's 30-day average has swung around
/// its normal level. Without it the range is too narrow at 60–90 days.
///
/// It also serves as the benchmark that Approach 2's models have to beat.
/// </summary>
public sealed class BootstrapForecaster : ISpendForecaster
{
    public string Name => "Bootstrap";
    public string Version => "bootstrap-1.0";
    public static readonly string[] FeatureSchema = { "start_available_balance", "recurring_income_calendar", "recurring_bill_calendar",
        "everyday_spend_weekday_pool", "everyday_spend_weekend_pool", "spending_mood_levels", "product_fee_rules" };

    public void Fit(IReadOnlyList<MemberHistory> universe, DateOnly cutoff) { }   // nothing to learn up front

    public double[][] SamplePaths(ForecastContext ctx, int horizon, int nPaths, Random rng)
    {
        var s = ctx.Series;
        int from = Math.Max(0, s.Length - Settings.ResidualLookbackDays);

        // 1) pools of real past days — healthy days only (on low-balance days the ledger under-records spending)
        var weekday = new List<double>(); var weekend = new List<double>();
        for (int i = from; i < s.Length; i++)
        {
            if (!s.Healthy[i]) continue;
            (EverydaySeries.IsWeekend(s.DateAt(i)) ? weekend : weekday).Add(s.Posted[i]);
        }
        if (weekday.Count < 5) weekday = Enumerable.Range(from, s.Length - from).Select(i => s.Imputed[i]).ToList();
        if (weekend.Count < 2) weekend = weekday;

        // 2) spending moods: 30-day rolling mean ÷ overall mean, sampled weekly
        double overall = Enumerable.Range(from, s.Length - from).Average(i => s.Imputed[i]);
        var moods = new List<double>();
        for (int end = from + 29; end < s.Length; end += 7)
        {
            double m = 0; for (int i = end - 29; i <= end; i++) m += s.Imputed[i];
            moods.Add(overall > 0 ? m / 30 / overall : 1);
        }
        if (moods.Count < 4) moods = new List<double> { 1.0 };

        // 3) build the futures
        var paths = new double[nPaths][];
        for (int p = 0; p < nPaths; p++)
        {
            double mood = moods[rng.Next(moods.Count)];
            var row = new double[horizon];
            for (int d = 0; d < horizon; d++)
            {
                var pool = EverydaySeries.IsWeekend(ctx.Origin.AddDays(d + 1)) ? weekend : weekday;
                row[d] = pool[rng.Next(pool.Count)] * mood;
            }
            paths[p] = row;
        }
        return paths;
    }
}
