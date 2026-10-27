namespace Occlet.Core.Ledger;

/// <summary>One posted ledger row. Amount is signed: + money in (credit), − money out (debit).</summary>
public sealed record Transaction(
    string TxnId, string MemberId, string Account, DateOnly Date,
    string Description, string Category, string Channel, double Amount, double RunningBalance);

/// <summary>Per-member product settings — what the core banking system would tell us about the account.</summary>
public sealed record MemberProfile
{
    public required string MemberId { get; init; }
    public required string Name { get; init; }
    public DateOnly MemberSince { get; init; }
    public string Product { get; init; } = "";
    public bool CourtesyPay { get; init; }          // overdrafts on bills are paid ... for a fee
    public double OverdraftFee { get; init; } = 35;
    public int MaxFeesPerDay { get; init; } = 3;
    public double MinItemForFee { get; init; } = 15;
    public bool CardOverdraftOptIn { get; init; }   // Reg E opt-in for debit card overdrafts
    public double NsfFee { get; init; }             // fee when a payment is declined instead
    public bool SavingsFirst { get; init; }         // pull from savings before overdrawing
    public bool LowBalanceAlert { get; init; }
    public double AlertThreshold { get; init; } = Settings.AlertThreshold;
    public bool DirectDeposit { get; init; }
    public double ExternalCardDebt { get; init; }   // balance on an outside credit card (for the refinance lever)
    public double ExternalCardApr { get; init; }
    public string Notes { get; init; } = "";
}

/// <summary>
/// A member's ledger up to an "as-of" date, with one end-of-day balance per calendar day.
/// <see cref="UpTo"/> cuts the history at an earlier date, which is how forecasts and back-tests
/// avoid seeing the future.
/// </summary>
public sealed class MemberHistory
{
    private readonly double[] _chk, _sav;

    public MemberProfile Profile { get; }
    public IReadOnlyList<Transaction> Transactions { get; }
    public DateOnly FirstDate { get; }
    public DateOnly LastDate { get; }
    public string Id => Profile.MemberId;
    public int HistoryDays => LastDate.DayNumber - FirstDate.DayNumber + 1;
    public IEnumerable<Transaction> Checking => Transactions.Where(t => t.Account == "CHK");

    public MemberHistory(MemberProfile profile, IEnumerable<Transaction> transactions, DateOnly asOf)
    {
        Profile = profile;
        // OrderBy is stable, so rows on the same day keep their posting order from the ledger
        Transactions = transactions.Where(t => t.Date <= asOf).OrderBy(t => t.Date).ToList();
        FirstDate = Transactions.Where(t => t.Account == "CHK").Select(t => t.Date).DefaultIfEmpty(asOf).Min();
        LastDate = asOf;
        _chk = EndOfDay("CHK");
        _sav = EndOfDay("SAV");
    }

    public MemberHistory UpTo(DateOnly cutoff) => new(Profile, Transactions, cutoff < LastDate ? cutoff : LastDate);

    public double CheckingEod(DateOnly d) => _chk[Index(d)];
    public double SavingsEod(DateOnly d) => _sav[Index(d)];

    private int Index(DateOnly d) => Math.Clamp(d.DayNumber - FirstDate.DayNumber, 0, _chk.Length - 1);

    /// <summary>Balance at the end of each day = RunningBalance of that day's last row; quiet days carry yesterday's balance forward.</summary>
    private double[] EndOfDay(string account)
    {
        int n = Math.Max(1, LastDate.DayNumber - FirstDate.DayNumber + 1);
        var result = new double[n];
        var rows = Transactions.Where(t => t.Account == account).ToList();
        double current = rows.Count > 0 ? rows[0].RunningBalance - rows[0].Amount : 0; // opening balance
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            var day = FirstDate.AddDays(i);
            while (j < rows.Count && rows[j].Date <= day) current = rows[j++].RunningBalance;
            result[i] = current;
        }
        return result;
    }
}
