using System.Globalization;

namespace Occlet.Core.Util;

/// <summary>Invariant culture (dot decimals, same output on every machine) but with percentages written "25%" not "25 %".</summary>
public static class Fmt
{
    public static readonly CultureInfo Culture = Make();

    private static CultureInfo Make()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.PercentPositivePattern = 1;   // n%
        c.NumberFormat.PercentNegativePattern = 1;   // -n%
        return CultureInfo.ReadOnly(c);
    }

    /// <summary>Call once at program start so every interpolated string uses it.</summary>
    public static void Install()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.CurrentCulture = Culture;
    }
}
