using Occlet.Core;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;

namespace Occlet.Approach2.LightGbm;

/// <summary>
/// Turns a member's history into LightGBM rows. Each row answers: "standing on ORIGIN date, how much
/// everyday spending will happen over the next H days, as a multiple of this member's normal pace?"
///
/// Feature families follow Occlet's design doc: past behaviour (spend levels, volatility, overdrafts),
/// known future events (paydays, scheduled bills, tight windows), calendar and profile.
/// Only data on or before the origin is used for features.
/// </summary>
internal sealed class FeatureBuilder
{
    public static readonly string[] Names =
    {
        "horizon_days", "spend7_over_spend90", "spend30_over_spend90", "spend_cv30", "weekend_share", "holiday_share",
        "cushion_days", "overdraft_fees_90d", "days_to_payday", "known_net_flow_ratio", "tight_windows", "tenure_days",
        "courtesy_pay", "direct_deposit", "alerts_on", "origin_day_of_month", "origin_day_of_week", "income_cv", "log_spend_scale",
    };
    public static readonly int[] HorizonGrid = { 1, 2, 3, 5, 7, 10, 14, 21, 28, 30, 35, 45, 60, 75, 90 };

    public MemberHistory History { get; }
    public IReadOnlyList<RecurringItem> Recurring { get; }
    public EverydaySeries Series { get; }
    private readonly double[] _prefix, _prefixSq;
    private readonly List<DateOnly> _feeDates;
    private readonly double _incomeCv;

    public FeatureBuilder(MemberHistory h, IReadOnlyList<RecurringItem> recurring, EverydaySeries series)
    {
        History = h; Recurring = recurring; Series = series;
        _prefix = new double[series.Length + 1]; _prefixSq = new double[series.Length + 1];
        for (int i = 0; i < series.Length; i++)
        {
            _prefix[i + 1] = _prefix[i] + series.Imputed[i];
            _prefixSq[i + 1] = _prefixSq[i] + series.Imputed[i] * series.Imputed[i];
        }
        _feeDates = h.Checking.Where(t => t.Category == "Fee").Select(t => t.Date).ToList();
        _incomeCv = recurring.Where(r => r.IsPayday).Select(r => r.AmountCv).DefaultIfEmpty(0).Max();
    }

    public static FeatureBuilder FromHistory(MemberHistory h)
    {
        var rec = RecurringDetector.Detect(h);
        return new FeatureBuilder(h, rec, EverydaySeries.Build(h, rec));
    }

    private int Index(DateOnly d) => d.DayNumber - Series.Start.DayNumber;

    /// <summary>Mean daily everyday spend over the <paramref name="days"/> days ending on <paramref name="d"/>.</summary>
    public double TrailingMean(DateOnly d, int days)
    {
        int end = Math.Min(Index(d), Series.Length - 1), start = Math.Max(0, end - days + 1);
        return end < start ? 0 : (_prefix[end + 1] - _prefix[start]) / (end - start + 1);
    }

    /// <summary>The member's spending "scale" — normal pace over the last 90 days (never below $1/day).</summary>
    public double Scale(DateOnly origin) => origin < History.LastDate
        ? FromHistory(History.UpTo(origin)).Scale(origin) : Math.Max(TrailingMean(origin, 90), 1.0);

    public bool HasHistoryAt(DateOnly origin) => Index(origin) >= Settings.MinHistoryDays - 1;

    /// <summary>
    /// Labels use future observations, but the origin's recurring classification and scale stay fixed.
    /// Unobserved attempted spending is still a trailing-imputation proxy, not ground truth demand.
    /// </summary>
    public double[] Targets(MemberHistory observed, IReadOnlyList<int> horizons)
    {
        var origin = History.LastDate;
        if (horizons.Any(h => h <= 0 || origin.AddDays(h) > observed.LastDate))
            throw new ArgumentException("Every label must be fully observed.");
        var future = EverydaySeries.Build(observed.UpTo(origin.AddDays(horizons.Max())), Recurring);
        int start = origin.DayNumber - future.Start.DayNumber + 1;
        double scale = Scale(origin);
        return horizons.Select(h => future.Imputed.Skip(start).Take(h).Sum() / (scale * h)).ToArray();
    }

    /// <summary>Feature rows for one origin and several horizons (row-major).</summary>
    public double[] Rows(DateOnly origin, IReadOnlyList<int> horizons)
    {
        if (origin > History.LastDate) throw new ArgumentException("Feature origin exceeds available history.");
        if (origin < History.LastDate) return FromHistory(History.UpTo(origin)).Rows(origin, horizons);
        int nf = Names.Length;
        var x = new double[horizons.Count * nf];
        double scale = Scale(origin);
        double s7 = TrailingMean(origin, 7), s30 = TrailingMean(origin, 30);
        int end = Math.Min(Index(origin), Series.Length - 1), st = Math.Max(0, end - 29), n = end - st + 1;
        double mean30 = (_prefix[end + 1] - _prefix[st]) / n;
        double var30 = Math.Max((_prefixSq[end + 1] - _prefixSq[st]) / n - mean30 * mean30, 0);
        int fees90 = _feeDates.Count(d => d > origin.AddDays(-90) && d <= origin);
        var events = RecurringDetector.Project(Recurring, origin, horizons.Max());
        var paydays = events.Where(e => e.IsPayday).Select(e => e.Day).ToList();
        int daysToPay = paydays.Count > 0 ? paydays.Min() + 1 : 30;
        double cushion = Math.Clamp(History.CheckingEod(origin) / scale, -20, 60);
        var p = History.Profile;
        for (int r = 0; r < horizons.Count; r++)
        {
            int h = horizons[r];
            int weekend = 0, holiday = 0;
            for (int d = 1; d <= h; d++)
            {
                var date = origin.AddDays(d);
                if (EverydaySeries.IsWeekend(date)) weekend++;
                if (date.Month == 12 && date.Day < 25) holiday++;
            }
            double net = events.Where(e => e.Day < h).Sum(e => e.Amount);
            int tight = events.Count(e => e.Day < h && e.Kind == ItemKind.Bill &&
                                          paydays.Any(pd => pd > e.Day && pd - e.Day <= Settings.TightWindowDays));
            var row = new double[]
            {
                h, s7 / scale, s30 / scale, mean30 > 0 ? Math.Sqrt(var30) / mean30 : 0, weekend / (double)h, holiday / (double)h,
                cushion, fees90, daysToPay, net / (scale * h), tight, origin.DayNumber - p.MemberSince.DayNumber,
                p.CourtesyPay ? 1 : 0, p.DirectDeposit ? 1 : 0, p.LowBalanceAlert ? 1 : 0, origin.Day, (int)origin.DayOfWeek,
                _incomeCv, Math.Log(scale),
            };
            Array.Copy(row, 0, x, r * nf, nf);
        }
        return x;
    }

    /// <summary>Weekend-vs-weekday weight, used to spread a multi-day total across individual days.</summary>
    public double WeekendWeight()
    {
        double wd = 0, we = 0; int nwd = 0, nwe = 0;
        for (int i = Math.Max(0, Series.Length - Settings.ResidualLookbackDays); i < Series.Length; i++)
        {
            if (!Series.Healthy[i]) continue;
            if (EverydaySeries.IsWeekend(Series.DateAt(i))) { we += Series.Posted[i]; nwe++; } else { wd += Series.Posted[i]; nwd++; }
        }
        return nwd > 0 && nwe > 0 && wd > 0 ? Math.Clamp(we / nwe / (wd / nwd), 0.5, 3) : 1.3;
    }
}
