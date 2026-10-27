namespace Occlet.Core;

/// <summary>
/// Every tunable number in one place. Occlet's design doc lists many of these as open
/// questions for the credit union (the spend window, the arbitration margin, the goal), so
/// they are settings, not constants buried in code.
/// </summary>
public static class Settings
{
    // ── Forecast shape ──────────────────────────────────────────────
    public const int Horizon = 90;                                   // days forecast
    public static readonly int[] ReportHorizons = { 30, 60, 90 };    // Occlet's three horizons
    public static int Paths { get; set; } = 2000;                    // simulated futures per member
    public static int Seed { get; set; } = 7;                        // fixed → same answer every run (auditability)

    // ── History used ────────────────────────────────────────────────
    public const int ResidualLookbackDays = 180;   // everyday-spending history the forecasters learn from
    public const int SpendWindowDays = 90;         // window for "average daily spend" (slack denominator)
    public const int MinHistoryDays = 60;          // less than this → member cannot be forecast
    public const double HealthyBalance = 150.0;    // days that START below this are "censored" (card buys get declined)

    // ── Recurring-item rules ────────────────────────────────────────
    public const int TightWindowDays = 5;          // Occlet: a bill due ≤5 days before payday is a "tight window"
    public static readonly HashSet<string> MovableBillCategories =
        new(StringComparer.OrdinalIgnoreCase) { "Phone", "Insurance", "Utilities", "Subscription", "Gym", "Internet" };
    public static readonly HashSet<string> HousingCategories =
        new(StringComparer.OrdinalIgnoreCase) { "Rent", "Mortgage" };
    public static readonly HashSet<string> DiscretionaryCategories =
        new(StringComparer.OrdinalIgnoreCase) { "Dining", "Shopping", "Coffee", "Entertainment" };

    // ── Learned-lever assumptions (illustrative until measured on real cohorts) ──
    public const double AlertThreshold = 150;      // low-balance alert fires below $150
    public const double AlertResponseRate = 0.35;  // Occlet's illustrative figure: 35% respond ...
    public const double AlertCut = 40;             // ... by trimming ~$40 ...
    public const int AlertCutDays = 3;             // ... over the next 3 days
    public const double TrimCut = 0.25;            // trim the top discretionary category by 25% ...
    public const double TrimAdherence = 0.50;      // ... and half of members actually stick to it

    // ── Liquidity levers ────────────────────────────────────────────
    public const double CashLoanPrincipal = 1000, CashLoanApr = 0.1399; public const int CashLoanMonths = 12;
    public const double RefiApr = 0.1199; public const int RefiMonths = 36;
    public const double AtrShareOfSurplus = 0.5;   // loan payment must fit in half of the monthly surplus

    // ── Decision rules ("How it decides what to recommend") ─────────
    public const double SlackGoalDays = 2.0;          // goal: on the tightest day, ≥ 2 days of spending in hand
    public const double MinBehaviourGain = 0.1;       // a behaviour lever must add at least 0.1 day
    public const double ArbitrationMarginDays = 1.0;  // a lower-priority lever must add ≥ 1 day of TRUE slack ...
    public const double HighConfidence = 0.80;        // ... in ≥ 80% of simulated futures
    public const double MaxExtraBillDeclines = 0.25;  // a lever that adds > 0.25 declined bills / 90 days needs a conversation, not auto-adoption
}
