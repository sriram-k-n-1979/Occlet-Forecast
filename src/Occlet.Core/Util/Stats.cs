using System.Security.Cryptography;
using System.Text;

namespace Occlet.Core.Util;

/// <summary>Small, dependency-free statistics helpers used everywhere.</summary>
public static class Stats
{
    /// <summary>Quantile of an already-sorted array, linear interpolation between neighbours (Excel's PERCENTILE.INC).</summary>
    public static double QuantileSorted(double[] sorted, double q)
    {
        if (sorted.Length == 0) return double.NaN;
        if (sorted.Length == 1) return sorted[0];
        double pos = Math.Clamp(q, 0, 1) * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    public static double Quantile(IEnumerable<double> values, double q)
    {
        var a = values.ToArray();
        Array.Sort(a);
        return QuantileSorted(a, q);
    }

    public static double Median(IEnumerable<double> values) => Quantile(values, 0.5);

    public static double Mean(IReadOnlyCollection<double> v) => v.Count == 0 ? 0 : v.Average();

    /// <summary>Population standard deviation.</summary>
    public static double StdPop(IReadOnlyCollection<double> v)
    {
        if (v.Count == 0) return 0;
        double m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count);
    }

    /// <summary>Standard normal draw (Box–Muller).</summary>
    public static double Gaussian(Random r)
    {
        double u1 = 1.0 - r.NextDouble();
        double u2 = r.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Student-t draw with <paramref name="df"/> degrees of freedom (fatter tails than a normal).</summary>
    public static double StudentT(Random r, int df)
    {
        double chi = 0;
        for (int i = 0; i < df; i++) { double g = Gaussian(r); chi += g * g; }
        return Gaussian(r) / Math.Sqrt(chi / df);
    }

    /// <summary>Standard normal CDF Φ(x).</summary>
    public static double NormalCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2.0));

    // Numerical Recipes erfc approximation, relative error < 1.2e-7.
    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * z);
        double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                   t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                   t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? r : 2.0 - r;
    }

    /// <summary>Inverse standard normal CDF Φ⁻¹(p) (Acklam's algorithm, error ~1e-9).</summary>
    public static double NormalInv(double p)
    {
        p = Math.Clamp(p, 1e-12, 1 - 1e-12);
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double plow = 0.02425;
        if (p < plow)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p <= 1 - plow)
        {
            double q = p - 0.5, r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
    }

    /// <summary>FNV-1a 64-bit hash. Unlike string.GetHashCode it is identical on every run and machine.</summary>
    public static ulong StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>A reproducible int seed made from any list of parts (member id, origin date, base seed...).</summary>
    public static int StableSeed(params object[] parts) =>
        (int)(StableHash(string.Join("|", parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)))) & 0x7FFFFFFF);

    public static string Sha256Hex(byte[] data, int chars = 16) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()[..chars];

    public static string Sha256Hex(string text, int chars = 16) => Sha256Hex(Encoding.UTF8.GetBytes(text), chars);
}
