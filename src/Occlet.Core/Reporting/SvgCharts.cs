using System.Globalization;
using System.Text;
using Occlet.Core.Decisioning;
using Occlet.Core.Engine;

namespace Occlet.Core.Reporting;

/// <summary>Two small hand-drawn SVG charts per member (no charting library): the 90-day fan chart and the waterfall.</summary>
public static class SvgCharts
{
    private const string Surface = "#fcfcfb", Ink = "#0b0b0b", Ink2 = "#52514e", Grid = "#e6e5e0", Blue = "#2a78d6", Orange = "#eb6834", Neutral = "#8a8984";
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Fan(Baseline b, SlackSummary plan, string title, string planLabel)
    {
        var s = b.Summary; int H = b.Horizon;
        const int W = 920, Hh = 400, L = 70, R = 24, T = 52, Bm = 44;
        double lo = new[] { s.Q10.Min(), plan.Q50.Min(), 0 }.Min(), hi = new[] { s.Q90.Max(), plan.Q50.Max() }.Max();
        double pad = (hi - lo) * 0.06; lo -= pad; hi += pad;
        double X(int d) => L + (W - L - R) * d / (double)(H - 1);
        double Y(double v) => T + (Hh - T - Bm) * (hi - v) / (hi - lo);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {Hh}\" width=\"{W}\" height=\"{Hh}\" font-family=\"Arial, Helvetica, sans-serif\" role=\"img\" aria-label=\"{Esc(title)}\">");
        sb.Append($"<rect width=\"{W}\" height=\"{Hh}\" fill=\"{Surface}\"/>");
        sb.Append($"<text x=\"{L}\" y=\"24\" font-size=\"15\" fill=\"{Ink}\">{Esc(title)}</text>");
        // y grid + labels
        double step = NiceStep((hi - lo) / 6);
        for (double v = Math.Ceiling(lo / step) * step; v <= hi; v += step)
        {
            sb.Append($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{F(Y(v))}\" y2=\"{F(Y(v))}\" stroke=\"{(Math.Abs(v) < 1e-6 ? Ink2 : Grid)}\" stroke-width=\"{(Math.Abs(v) < 1e-6 ? 1.2 : 0.8)}\"/>");
            sb.Append($"<text x=\"{L - 8}\" y=\"{F(Y(v) + 4)}\" font-size=\"11\" fill=\"{Ink2}\" text-anchor=\"end\">{(v < 0 ? "−$" : "$")}{Math.Abs(v).ToString("N0", CultureInfo.InvariantCulture)}</text>");
        }
        // x ticks on the 1st and 15th
        for (int d = 0; d < H; d++)
        {
            var date = b.DateAt(d);
            if (date.Day != 1 && date.Day != 15) continue;
            sb.Append($"<line x1=\"{F(X(d))}\" x2=\"{F(X(d))}\" y1=\"{Hh - Bm}\" y2=\"{Hh - Bm + 5}\" stroke=\"{Ink2}\"/>");
            sb.Append($"<text x=\"{F(X(d))}\" y=\"{Hh - Bm + 19}\" font-size=\"11\" fill=\"{Ink2}\" text-anchor=\"middle\">{date.ToString("MMM dd", CultureInfo.InvariantCulture)}</text>");
        }
        // band q10–q90
        var band = new StringBuilder();
        for (int d = 0; d < H; d++) band.Append($"{F(X(d))},{F(Y(s.Q90[d]))} ");
        for (int d = H - 1; d >= 0; d--) band.Append($"{F(X(d))},{F(Y(s.Q10[d]))} ");
        sb.Append($"<polygon points=\"{band}\" fill=\"{Blue}\" fill-opacity=\"0.15\" stroke=\"none\"/>");
        sb.Append(Line(s.Q50, X, Y, Blue));
        sb.Append(Line(plan.Q50, X, Y, Orange));
        // lowest point annotation
        int low = s.LowestQ50Day;
        sb.Append($"<circle cx=\"{F(X(low))}\" cy=\"{F(Y(s.Q50[low]))}\" r=\"4\" fill=\"{Blue}\" stroke=\"{Surface}\" stroke-width=\"2\"/>");
        double ax = Math.Min(X(low) + 10, W - R - 150);
        sb.Append($"<text x=\"{F(ax)}\" y=\"{F(Math.Min(Y(s.Q50[low]) + 22, Hh - Bm - 6))}\" font-size=\"11\" fill=\"{Ink}\">Lowest likely: ${s.Q50[low].ToString("N0", CultureInfo.InvariantCulture)} on {b.DateAt(low).ToString("MMM dd", CultureInfo.InvariantCulture)}</text>");
        // legend
        int lx = L; int ly = 40;
        sb.Append($"<rect x=\"{lx}\" y=\"{ly - 9}\" width=\"14\" height=\"10\" fill=\"{Blue}\" fill-opacity=\"0.15\"/><text x=\"{lx + 20}\" y=\"{ly}\" font-size=\"11\" fill=\"{Ink2}\">Baseline 10th–90th percentile</text>");
        sb.Append($"<line x1=\"{lx + 210}\" x2=\"{lx + 228}\" y1=\"{ly - 4}\" y2=\"{ly - 4}\" stroke=\"{Blue}\" stroke-width=\"2\"/><text x=\"{lx + 234}\" y=\"{ly}\" font-size=\"11\" fill=\"{Ink2}\">Baseline likely (q50)</text>");
        sb.Append($"<line x1=\"{lx + 380}\" x2=\"{lx + 398}\" y1=\"{ly - 4}\" y2=\"{ly - 4}\" stroke=\"{Orange}\" stroke-width=\"2\"/><text x=\"{lx + 404}\" y=\"{ly}\" font-size=\"11\" fill=\"{Ink2}\">{Esc(planLabel)} (q50)</text>");
        return sb.Append("</svg>").ToString();
    }

    private static string Line(double[] v, Func<int, double> X, Func<double, double> Y, string color)
    {
        var pts = string.Join(" ", v.Select((x, d) => $"{F(X(d))},{F(Y(x))}"));
        return $"<polyline points=\"{pts}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\" stroke-linejoin=\"round\"/>";
    }

    public static string Waterfall(Plan plan, string title)
    {
        var bars = new List<(string label, double from, double to, string kind)> { ("Baseline", 0, plan.BaselineScore, "total") };
        double cur = plan.BaselineScore;
        foreach (var s in plan.Steps) { bars.Add((s.Lever.Id, cur, cur + s.Delta, s.Delta >= 0 ? "up" : "down")); cur += s.Delta; }
        bars.Add(("Plan", 0, plan.FinalScore, "total"));
        int n = bars.Count;
        const int L = 60, R = 96, T = 52, Bm = 56, Hh = 340;   // right margin holds the goal label
        int W = Math.Max(520, L + R + n * 120);
        double lo = Math.Min(0, bars.Min(b => Math.Min(b.from, b.to))), hi = Math.Max(Settings.SlackGoalDays, bars.Max(b => Math.Max(b.from, b.to)));
        double pad = (hi - lo) * 0.12; hi += pad; if (lo < 0) lo -= pad;
        double Y(double v) => T + (Hh - T - Bm) * (hi - v) / (hi - lo);
        double slot = (W - L - R) / (double)n, bw = Math.Min(64, slot * 0.6);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {Hh}\" width=\"{W}\" height=\"{Hh}\" font-family=\"Arial, Helvetica, sans-serif\" role=\"img\" aria-label=\"{Esc(title)}\">");
        sb.Append($"<rect width=\"{W}\" height=\"{Hh}\" fill=\"{Surface}\"/>");
        sb.Append($"<text x=\"{L}\" y=\"24\" font-size=\"15\" fill=\"{Ink}\">{Esc(title)}</text>");
        sb.Append($"<text x=\"{L}\" y=\"42\" font-size=\"11\" fill=\"{Ink2}\">Lowest TRUE slack on the tightest day (median future), in days of spending</text>");
        sb.Append($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{F(Y(0))}\" y2=\"{F(Y(0))}\" stroke=\"{Ink2}\"/>");
        sb.Append($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{F(Y(Settings.SlackGoalDays))}\" y2=\"{F(Y(Settings.SlackGoalDays))}\" stroke=\"{Ink2}\" stroke-dasharray=\"4 4\"/>");
        sb.Append($"<text x=\"{W - R + 8}\" y=\"{F(Y(Settings.SlackGoalDays) + 4)}\" font-size=\"11\" fill=\"{Ink2}\">goal {Settings.SlackGoalDays:0.0} days</text>");
        for (int i = 0; i < n; i++)
        {
            var (label, from, to, kind) = bars[i];
            double x = L + slot * i + (slot - bw) / 2;
            double top = Y(Math.Max(from, to)), bot = Y(Math.Min(from, to));
            string fill = kind == "total" ? Neutral : kind == "up" ? Blue : Orange;
            sb.Append($"<rect x=\"{F(x)}\" y=\"{F(top)}\" width=\"{F(bw)}\" height=\"{F(Math.Max(bot - top, 1.5))}\" rx=\"3\" fill=\"{fill}\"/>");
            string val = kind == "total" ? $"{to:0.0}" : $"{(to - from >= 0 ? "+" : "")}{to - from:0.0}";
            sb.Append($"<text x=\"{F(x + bw / 2)}\" y=\"{F(top - 6)}\" font-size=\"12\" fill=\"{Ink}\" text-anchor=\"middle\">{val.Replace('-', '−')}</text>");
            sb.Append($"<text x=\"{F(x + bw / 2)}\" y=\"{Hh - Bm + 18}\" font-size=\"11\" fill=\"{Ink2}\" text-anchor=\"middle\">{Esc(label)}</text>");
            if (i < n - 1)
            {
                double next = L + slot * (i + 1) + (slot - bw) / 2;
                sb.Append($"<line x1=\"{F(x + bw)}\" x2=\"{F(next)}\" y1=\"{F(Y(to))}\" y2=\"{F(Y(to))}\" stroke=\"{Ink2}\" stroke-width=\"0.8\" stroke-dasharray=\"2 2\"/>");
            }
        }
        return sb.Append("</svg>").ToString();
    }

    private static double NiceStep(double raw)
    {
        double mag = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-9))));
        double r = raw / mag;
        return (r < 1.5 ? 1 : r < 3 ? 2 : r < 7 ? 5 : 10) * mag;
    }
}
