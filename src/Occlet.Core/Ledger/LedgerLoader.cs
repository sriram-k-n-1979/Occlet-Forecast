using System.Globalization;
using Occlet.Core.Io;

namespace Occlet.Core.Ledger;

/// <summary>Reads the "Ledger" and "Members" sheets of the spreadsheet into one <see cref="MemberHistory"/> per member.</summary>
public static class LedgerLoader
{
    public static List<MemberHistory> Load(string path)
    {
        var ledger = XlsxReader.ReadSheet(path, "Ledger");
        var members = XlsxReader.ReadSheet(path, "Members");

        var lc = Columns(ledger[0]);
        var txns = ledger.Skip(1).Where(r => r.Length > 0 && !string.IsNullOrWhiteSpace(Cell(r, lc, "MemberID")))
            .Select(r => new Transaction(
                TxnId: Cell(r, lc, "TxnID"),
                MemberId: Cell(r, lc, "MemberID"),
                Account: Cell(r, lc, "Account").ToUpperInvariant(),
                Date: ParseDate(Cell(r, lc, "PostedDate")),
                Description: Cell(r, lc, "Description"),
                Category: Cell(r, lc, "Category"),
                Channel: Cell(r, lc, "Channel"),
                Amount: Num(Cell(r, lc, "Amount")),
                RunningBalance: Num(Cell(r, lc, "RunningBalance"))))
            .ToList();

        // Every member's history runs to the extract date (the last date anywhere in the ledger),
        // even if that member had no transaction on that day.
        var asOf = txns.Max(t => t.Date);

        var mc = Columns(members[0]);
        var profiles = members.Skip(1).Where(r => r.Length > 0 && !string.IsNullOrWhiteSpace(Cell(r, mc, "MemberID")))
            .Select(r => new MemberProfile
            {
                MemberId = Cell(r, mc, "MemberID"),
                Name = Cell(r, mc, "Name"),
                MemberSince = ParseDate(Cell(r, mc, "MemberSince")),
                Product = Cell(r, mc, "Product"),
                CourtesyPay = Bool(Cell(r, mc, "CourtesyPay")),
                OverdraftFee = Num(Cell(r, mc, "OverdraftFee"), 35),
                MaxFeesPerDay = (int)Num(Cell(r, mc, "MaxFeesPerDay"), 3),
                MinItemForFee = Num(Cell(r, mc, "MinItemForFee"), 15),
                CardOverdraftOptIn = Bool(Cell(r, mc, "CardOverdraftOptIn")),
                NsfFee = Num(Cell(r, mc, "NsfFee")),
                SavingsFirst = Bool(Cell(r, mc, "SavingsFirst")),
                LowBalanceAlert = Bool(Cell(r, mc, "LowBalanceAlert")),
                AlertThreshold = Num(Cell(r, mc, "AlertThreshold"), Settings.AlertThreshold),
                DirectDeposit = Bool(Cell(r, mc, "DirectDeposit")),
                ExternalCardDebt = Num(Cell(r, mc, "ExternalCardDebt")),
                ExternalCardApr = Num(Cell(r, mc, "ExternalCardApr")),
                Notes = Cell(r, mc, "Notes"),
            }).ToList();

        return profiles
            .Select(p => new MemberHistory(p, txns.Where(t => t.MemberId == p.MemberId), asOf))
            .Where(h => h.Transactions.Count > 0)
            .ToList();
    }

    private static Dictionary<string, int> Columns(string?[] header) =>
        header.Select((h, i) => (h: (h ?? "").Trim(), i)).Where(x => x.h.Length > 0)
              .ToDictionary(x => x.h, x => x.i, StringComparer.OrdinalIgnoreCase);

    private static string Cell(string?[] row, Dictionary<string, int> cols, string name) =>
        cols.TryGetValue(name, out int i) && i < row.Length ? (row[i] ?? "").Trim() : "";

    private static double Num(string s, double fallback = 0) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool Bool(string s) =>
        s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
        s == "1" || s.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase) || s.Equals("On", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dates arrive either as Excel serial numbers (45930) or as text (2026-10-27).</summary>
    private static DateOnly ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateOnly.MinValue;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return DateOnly.Parse(s, CultureInfo.InvariantCulture);
    }
}
