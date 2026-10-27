using Occlet.Core;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Util;

namespace Occlet.Approach2.StateSpace;

/// <summary>Estimated parameters of one member's spending model (or the pooled prior).</summary>
public sealed record SpendParams(int HealthyDays, double PWeekday, double PWeekend, double MuWeekday, double MuWeekend,
    double SigmaWithin, double Phi, double SigmaX, double StateMean, double StateVar, double MaxDaily, double MuStdErr = 0.1)
{
    public static readonly SpendParams Default = new(0, 0.6, 0.7, Math.Log(30), Math.Log(35), 0.7, 0.5, 0.15, 0, 0.02, 500, 0.15);
}

/// <summary>
/// APPROACH 2b — the closer-fitting alternative: a probabilistic STATE-SPACE model of everyday spending
/// (a classical cousin of DeepAR / the Temporal Fusion Transformer Occlet names as challenger, sized for
/// small data). Unlike LightGBM it is a GENERATIVE time-series model, so it produces joined-up daily
/// paths natively, extrapolates naturally to longer horizons, and its uncertainty widens with time
/// for a stated reason.
///
///   On each day:   spend?  ~ Bernoulli(p_weekday or p_weekend)              ("hurdle": many days have no spend)
///                  amount  = exp( μ_daytype + x_week + ε ),  ε ~ σ·t₅      (log-normal with fat tails for shocks)
///   Each week:     x_week = φ · x_lastweek + η,  η ~ N(0, σx²(1−φ²))       (a persistent "spending mood", AR(1))
///   Each future:   level ~ N(0, se(μ)²)                                    (we only estimated μ — parameter uncertainty)
///
/// x is not observed directly; a Kalman filter estimates where it stands today from the weekly averages.
/// Thin files borrow strength: shape parameters (p, σ, φ, σx) are shrunk toward the median of all members
/// with credibility weight n / (n + 60), where n = the member's usable days (Occlet's credibility idea).
/// </summary>
public sealed class StateSpaceForecaster : ISpendForecaster
{
    private const double PriorDays = 60;
    private SpendParams _prior = SpendParams.Default;
    public string Name => "StateSpace";
    public string Version => "hurdle-lognormal-ar1-kalman-credibility-1.0";
    public SpendParams Prior => _prior;
    public Dictionary<string, (SpendParams raw, SpendParams used)> LastParams { get; } = new();
    public static readonly string[] FeatureSchema = { "start_available_balance", "recurring_income_calendar", "recurring_bill_calendar",
        "p_spend_weekday", "p_spend_weekend", "mu_log_amount_by_daytype", "sigma_within", "phi_weekly_ar1", "sigma_state", "kalman_state", "credibility_prior" };

    /// <summary>Learn the pooled prior from every member's history up to the cutoff.</summary>
    public void Fit(IReadOnlyList<MemberHistory> universe, DateOnly cutoff)
    {
        var est = universe.Select(h => { var u = h.UpTo(cutoff); var rec = RecurringDetector.Detect(u); return Estimate(EverydaySeries.Build(u, rec)); })
                          .Where(e => e.HealthyDays >= 30).ToList();
        if (est.Count == 0) { _prior = SpendParams.Default; return; }
        _prior = SpendParams.Default with
        {
            PWeekday = Stats.Median(est.Select(e => e.PWeekday)), PWeekend = Stats.Median(est.Select(e => e.PWeekend)),
            SigmaWithin = Stats.Median(est.Select(e => e.SigmaWithin)), Phi = Stats.Median(est.Select(e => e.Phi)),
            SigmaX = Stats.Median(est.Select(e => e.SigmaX)),
        };
    }

    public double[][] SamplePaths(ForecastContext ctx, int horizon, int nPaths, Random rng)
    {
        var raw = Estimate(ctx.Series);
        double w = raw.HealthyDays / (raw.HealthyDays + PriorDays);                  // credibility weight
        double Blend(double own, double prior) => w * own + (1 - w) * prior;
        var p = raw with
        {
            PWeekday = Blend(raw.PWeekday, _prior.PWeekday), PWeekend = Blend(raw.PWeekend, _prior.PWeekend),
            SigmaWithin = Blend(raw.SigmaWithin, _prior.SigmaWithin), Phi = Blend(raw.Phi, _prior.Phi), SigmaX = Blend(raw.SigmaX, _prior.SigmaX),
        };
        LastParams[ctx.History.Id] = (raw, p);

        double innov = p.SigmaX * Math.Sqrt(1 - p.Phi * p.Phi);
        double tScale = Math.Sqrt(3.0 / 5.0);                                          // unit-variance t₅
        var paths = new double[nPaths][];
        for (int k = 0; k < nPaths; k++)
        {
            double x = p.StateMean + Math.Sqrt(p.StateVar) * Stats.Gaussian(rng);      // where the mood stands today (uncertain)
            double level = p.MuStdErr * Stats.Gaussian(rng);                            // we only ESTIMATED the typical amount: carry that uncertainty
            var row = new double[horizon];
            for (int d = 0; d < horizon; d++)
            {
                if (d > 0 && d % 7 == 0) x = p.Phi * x + innov * Stats.Gaussian(rng);  // mood moves week to week
                bool weekend = EverydaySeries.IsWeekend(ctx.Origin.AddDays(d + 1));
                if (rng.NextDouble() >= (weekend ? p.PWeekend : p.PWeekday)) continue;
                double eps = p.SigmaWithin * tScale * Stats.StudentT(rng, 5);
                row[d] = Math.Min(Math.Exp((weekend ? p.MuWeekend : p.MuWeekday) + level + x + eps), p.MaxDaily);
            }
            paths[k] = row;
        }
        return paths;
    }

    /// <summary>Fits the hurdle-lognormal-AR(1) model to the last 180 days of healthy (uncensored) days.</summary>
    public static SpendParams Estimate(EverydaySeries s)
    {
        int from = Math.Max(0, s.Length - Settings.ResidualLookbackDays);
        var wdAll = new List<double>(); var weAll = new List<double>();
        var spendDays = new List<(int i, bool weekend, double logY)>();
        double maxDaily = 0;
        for (int i = from; i < s.Length; i++)
        {
            if (!s.Healthy[i]) continue;
            bool we = EverydaySeries.IsWeekend(s.DateAt(i));
            (we ? weAll : wdAll).Add(s.Posted[i]);
            maxDaily = Math.Max(maxDaily, s.Posted[i]);
            if (s.Posted[i] > 0.5) spendDays.Add((i, we, Math.Log(s.Posted[i])));
        }
        int healthy = wdAll.Count + weAll.Count;
        if (spendDays.Count < 10) return SpendParams.Default with { HealthyDays = healthy };

        double pWd = wdAll.Count > 0 ? wdAll.Count(v => v > 0.5) / (double)wdAll.Count : 0.6;
        double pWe = weAll.Count > 0 ? weAll.Count(v => v > 0.5) / (double)weAll.Count : pWd;
        var lw = spendDays.Where(t => !t.weekend).Select(t => t.logY).ToList();
        var le = spendDays.Where(t => t.weekend).Select(t => t.logY).ToList();
        double muWd = lw.Count > 0 ? lw.Average() : spendDays.Average(t => t.logY);
        double muWe = le.Count >= 3 ? le.Average() : muWd;

        // weekly blocks of residuals → within-week noise, between-week signal, week-to-week persistence
        var blocks = spendDays.GroupBy(t => (t.i - from) / 7).OrderBy(g => g.Key)
            .Select(g => (week: g.Key, r: g.Select(t => t.logY - (t.weekend ? muWe : muWd)).ToList())).ToList();
        double ssWithin = 0; int dfWithin = 0;
        foreach (var b in blocks) { double m = b.r.Average(); ssWithin += b.r.Sum(v => (v - m) * (v - m)); dfWithin += b.r.Count - 1; }
        double var0 = spendDays.Select(t => t.logY - (t.weekend ? muWe : muWd)).Select(v => v * v).Average();
        double sigmaW2 = dfWithin > 5 ? ssWithin / dfWithin : var0;
        var usable = blocks.Where(b => b.r.Count >= 2).ToList();
        double meanW = usable.Count > 0 ? usable.Average(b => b.r.Average()) : 0;
        double varW = usable.Count > 1 ? usable.Average(b => Math.Pow(b.r.Average() - meanW, 2)) : 0;
        double noise = usable.Count > 0 ? usable.Average(b => sigmaW2 / b.r.Count) : sigmaW2;
        double sigmaX2 = Math.Max(varW - noise, 0.0025);
        double c1 = 0; int n1 = 0;
        for (int k = 1; k < usable.Count; k++)
            if (usable[k].week == usable[k - 1].week + 1)
            { c1 += (usable[k].r.Average() - meanW) * (usable[k - 1].r.Average() - meanW); n1++; }
        double phi = n1 >= 4 ? Math.Clamp(c1 / n1 / sigmaX2, 0, 0.95) : 0.5;

        // Kalman filter over the weeks: today's best estimate of the mood x and how sure we are
        double mState = 0, pState = sigmaX2, q = sigmaX2 * (1 - phi * phi);
        int lastWeek = (s.Length - 1 - from) / 7;
        var byWeek = blocks.ToDictionary(b => b.week, b => b.r);
        for (int wk = 0; wk <= lastWeek; wk++)
        {
            mState *= phi; pState = phi * phi * pState + q;
            if (!byWeek.TryGetValue(wk, out var r) || r.Count == 0) continue;
            double R = sigmaW2 / r.Count, K = pState / (pState + R);
            mState += K * (r.Average() - mState); pState *= 1 - K;
        }
        // standard error of the estimated log-level: day-to-day noise averaged over spend days, plus the slower
        // mood component averaged over (roughly independent) weeks
        int weeks = Math.Max(usable.Count, 1);
        double muSe = Math.Sqrt(sigmaW2 / spendDays.Count + sigmaX2 * (1 + phi) / (1 - phi) / weeks);
        return new SpendParams(healthy, pWd, pWe, muWd, muWe, Math.Sqrt(sigmaW2), phi, Math.Sqrt(sigmaX2), mState, pState,
                               Math.Max(maxDaily * 1.5, 50), muSe);
    }
}
