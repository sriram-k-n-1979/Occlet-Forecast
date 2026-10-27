using Occlet.Core.Ledger;

namespace Occlet.Core.Forecasting;

/// <summary>Everything a forecaster may look at for one member at one origin date (nothing after it).</summary>
public sealed record ForecastContext(
    MemberHistory History, IReadOnlyList<RecurringItem> Recurring, IReadOnlyList<FutureEvent> Events,
    EverydaySeries Series, DateOnly Origin, double StartBalance, double AverageDailySpend);

/// <summary>
/// The ONLY part that differs between Approach 1 and Approach 2.
///
/// Paydays and bills are known in advance, so they are added by plain arithmetic (the replay engine).
/// A forecaster's single job is the uncertain part: everyday spending over the next N days,
/// returned as many possible futures ("sample paths"), each a list of daily dollar amounts.
/// </summary>
public interface ISpendForecaster
{
    string Name { get; }
    string Version { get; }
    /// <summary>Global models learn here from every member's history up to <paramref name="cutoff"/>. Per-member models may do nothing.</summary>
    void Fit(IReadOnlyList<MemberHistory> universe, DateOnly cutoff);
    /// <summary>Returns [path][day] everyday spending in dollars (≥ 0).</summary>
    double[][] SamplePaths(ForecastContext ctx, int horizon, int nPaths, Random rng);
}
