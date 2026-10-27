using Occlet.Core.Ledger;
using Occlet.Core.Util;

namespace Occlet.Core.Forecasting;

public enum ItemKind { Income, Bill }
public enum Cadence { Weekly, Biweekly, Monthly }

/// <summary>A payday or bill that repeats on a schedule — the "known future" in Occlet's design.</summary>
public sealed record RecurringItem(
    string Key, string Description, string Category, string Channel, ItemKind Kind, Cadence Cadence,
    int PeriodDays, int DayOfMonth, DateOnly LastSeen, double Amount, double AmountCv, int Occurrences)
{
    public bool IsPayday => Kind == ItemKind.Income;
    /// <summary>Uncertainty of the estimated average amount itself (standard error ÷ mean), shared by all future occurrences.</summary>
    public double LevelCv => AmountCv < 0.03 ? 0 : AmountCv / Math.Sqrt(Math.Min(Occurrences, 26));
    public double MonthlyAmount => Cadence == Cadence.Monthly ? Amount : Amount * 30.4 / PeriodDays;
}

/// <summary>One projected cash event inside the forecast window. Day 0 = the day after the origin.</summary>
public sealed record FutureEvent(
    int Day, DateOnly Date, string Key, string Description, string Category, string Channel,
    double Amount, double AmountCv, ItemKind Kind, string Source)
{
    public bool IsPayday => Kind == ItemKind.Income && Category == "Income";
}

public static class RecurringDetector
{
    /// <summary>
    /// Finds paydays and bills. A description counts as recurring when it repeats on a steady
    /// rhythm (weekly, every two weeks, twice a month, or monthly) for a steady amount and is still active.
    /// Fees are excluded: they are an OUTPUT of the product rules, never an input.
    /// </summary>
    public static List<RecurringItem> Detect(MemberHistory h)
    {
        var found = new List<RecurringItem>();
        var rows = h.Checking.Where(t => t.Category != "Fee" && t.Category != "Loan proceeds").ToList();

        // complete calendar months only — a half month at either end would make items look "missing"
        var first = h.FirstDate; var last = h.LastDate;
        var fullStart = first.Day == 1 ? first : new DateOnly(first.Year, first.Month, 1).AddMonths(1);
        var fullEnd = last.AddDays(1).Day == 1 ? last : new DateOnly(last.Year, last.Month, 1).AddDays(-1);
        int fullMonths = Math.Max(0, (fullEnd.Year - fullStart.Year) * 12 + fullEnd.Month - fullStart.Month + 1);

        foreach (var g in rows.GroupBy(t => t.Description))
        {
            var occ = g.OrderBy(t => t.Date).ToList();
            if (occ.Count < 3) continue;
            if (occ.Any(t => Math.Sign(t.Amount) != Math.Sign(occ[0].Amount))) continue;
            var kind = occ[0].Amount > 0 ? ItemKind.Income : ItemKind.Bill;
            var gaps = occ.Zip(occ.Skip(1), (a, b) => (double)(b.Date.DayNumber - a.Date.DayNumber)).ToList();
            double medGap = Stats.Median(gaps);
            var amounts = occ.Select(t => t.Amount).ToList();
            var recent = amounts.TakeLast(26).ToList();
            double cv = Stats.StdPop(recent) / Math.Abs(recent.Average());
            // fixed amounts: the recent median catches a price change; varying amounts (hours, gig payouts):
            // the mean, because the simulation draws around it and must keep the average right
            double amount = cv < 0.03 ? Stats.Median(amounts.TakeLast(6)) : recent.Average();
            if (cv > 0.40) continue;                              // too irregular to call "scheduled"

            // 1) twice a month on fixed days (e.g. the 8th and 23rd)
            var inFull = occ.Where(t => t.Date >= fullStart && t.Date <= fullEnd).ToList();
            var byMonth = inFull.GroupBy(t => (t.Date.Year, t.Date.Month)).ToList();
            if (fullMonths >= 2 && byMonth.Count >= 2 && byMonth.Count(m => m.Count() == 2) >= 0.8 * fullMonths)
            {
                var ranked = byMonth.Where(m => m.Count() == 2)
                    .SelectMany(m => m.OrderBy(t => t.Date).Select((t, i) => (t, rank: i))).ToList();
                var r0 = ranked.Where(x => x.rank == 0).Select(x => (double)x.t.Date.Day).ToList();
                var r1 = ranked.Where(x => x.rank == 1).Select(x => (double)x.t.Date.Day).ToList();
                if (Stats.StdPop(r0) <= 2.5 && Stats.StdPop(r1) <= 2.5)
                {
                    foreach (var (days, rank) in new[] { (r0, 0), (r1, 1) })
                        Add(found, h, occ, $"{g.Key}#{rank}", kind, Cadence.Monthly, 30, (int)Math.Round(Stats.Median(days)), amount, cv, needed: 6);
                    continue;
                }
            }
            // 2) weekly, 3) every two weeks — the gaps themselves are steady
            double share7 = gaps.Count(x => Math.Abs(x - 7) <= 1) / (double)gaps.Count;
            double share14 = gaps.Count(x => Math.Abs(x - 14) <= 1) / (double)gaps.Count;
            if (medGap is >= 6 and <= 8 && share7 >= 0.75) { Add(found, h, occ, g.Key, kind, Cadence.Weekly, 7, 0, amount, cv, needed: 6); continue; }
            if (medGap is >= 13 and <= 15 && share14 >= 0.75) { Add(found, h, occ, g.Key, kind, Cadence.Biweekly, 14, 0, amount, cv, needed: 4); continue; }
            // 4) monthly on a fixed day of the month
            var doms = occ.Select(t => (double)t.Date.Day).ToList();
            if (medGap is >= 27 and <= 34 && Stats.StdPop(doms) <= 2.5)
                Add(found, h, occ, g.Key, kind, Cadence.Monthly, 30, (int)Math.Round(Stats.Median(doms)), amount, cv, needed: 3);
        }
        return found.OrderBy(i => i.Kind).ThenBy(i => i.Cadence).ThenBy(i => i.DayOfMonth).ToList();
    }

    private static void Add(List<RecurringItem> found, MemberHistory h, List<Transaction> occ, string key, ItemKind kind,
                            Cadence cadence, int period, int dom, double amount, double cv, int needed)
    {
        var last = occ[^1].Date;
        if (occ.Count < needed) return;
        if (h.LastDate.DayNumber - last.DayNumber > 1.5 * period + 3) return;   // stopped: no longer active
        var t = occ[^1];
        found.Add(new RecurringItem(key, t.Description, t.Category, t.Channel, kind, cadence, period, dom, last, amount, cv, occ.Count));
    }

    /// <summary>Projects each recurring item onto the forecast window (origin+1 … origin+horizon).</summary>
    public static List<FutureEvent> Project(IReadOnlyList<RecurringItem> items, DateOnly origin, int horizon)
    {
        var first = origin.AddDays(1);
        var last = origin.AddDays(horizon);
        var events = new List<FutureEvent>();
        foreach (var it in items)
        {
            if (it.Cadence == Cadence.Monthly)
            {
                for (var m = new DateOnly(first.Year, first.Month, 1); m <= last; m = m.AddMonths(1))
                {
                    var d = new DateOnly(m.Year, m.Month, Math.Min(it.DayOfMonth, DateTime.DaysInMonth(m.Year, m.Month)));
                    if (it.IsPayday) while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1); // payroll posts the Friday before
                    if (d >= first && d <= last) events.Add(Make(it, d, origin));
                }
            }
            else
            {
                // first occurrence after the origin, stepping from the last time we saw it (works backwards too)
                int k = (int)Math.Ceiling((first.DayNumber - it.LastSeen.DayNumber) / (double)it.PeriodDays);
                for (var d = it.LastSeen.AddDays(k * it.PeriodDays); d <= last; d = d.AddDays(it.PeriodDays))
                    if (d >= first) events.Add(Make(it, d, origin));
            }
        }
        // money in before money out on the same day; bigger bills first (a common core-banking posting order)
        return events.OrderBy(e => e.Day).ThenByDescending(e => e.Amount).ToList();
    }

    private static FutureEvent Make(RecurringItem it, DateOnly d, DateOnly origin) =>
        new(d.DayNumber - origin.DayNumber - 1, d, $"{it.Key}@{d:yyyy-MM-dd}", it.Description, it.Category, it.Channel,
            it.Amount, it.AmountCv, it.Kind, "recurring");
}
