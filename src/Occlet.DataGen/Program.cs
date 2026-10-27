// Occlet.DataGen — builds an ILLUSTRATIVE one-year ledger for five credit-union members and writes
// data/members_ledger.xlsx (sheets: Ledger, Members, README). Every name and number is made up.
//
//   A  Asha  — Occlet's "Member A": paid on the 8th & 23rd, but rent/phone/insurance land BEFORE payday.
//   B  Ben   — hourly worker paid every other Friday (amount varies ±8%); low-balance alerts ALREADY on.
//   C  Chen  — salaried, paid monthly, large cushion: the engine should recommend nothing.
//   D  Dee   — joined in June: a thin file (~4 months), so it cannot be back-tested yet.
//   E  Eli   — gig driver paid weekly with ±30% swings, frequent overdrafts, expensive outside card debt.
using System.Globalization;
using Occlet.Core.Io;

Occlet.Core.Util.Fmt.Install();

var root = args.Length > 0 ? args[0] : FindRepoRoot();
var outPath = Path.Combine(root, "data", "members_ledger.xlsx");
var end = new DateOnly(2026, 10, 27);                    // extract date ("today")
var start = end.AddDays(-364);                           // one year of history

var members = new[] { Specs.Asha(), Specs.Ben(), Specs.Chen(), Specs.Dee(), Specs.Eli() };
var ledger = new List<object?[]>();
foreach (var m in members) ledger.AddRange(Simulate(m, start, end));

new XlsxWriter()
    .AddSheet("Ledger", new[] { "TxnID", "MemberID", "Account", "PostedDate", "Description", "Category", "Channel", "Amount", "RunningBalance" }, ledger)
    .AddSheet("Members", new[] { "MemberID", "Name", "MemberSince", "Product", "CourtesyPay", "OverdraftFee", "MaxFeesPerDay", "MinItemForFee",
            "CardOverdraftOptIn", "NsfFee", "SavingsFirst", "LowBalanceAlert", "AlertThreshold", "DirectDeposit", "ExternalCardDebt", "ExternalCardApr", "Notes" },
        members.Select(m => new object?[] { m.Id, m.Name, m.Since, m.Product, m.CourtesyPay, 35.0, 3, 15.0, false, 0.0, false, m.AlertsOn,
            m.AlertsOn ? 200.0 : 150.0, true, m.CardDebt, m.CardApr, m.Notes }))
    .AddSheet("README", new[] { "Item", "Meaning" }, new object?[][]
    {
        new object?[] { "What this is", "Illustrative one-year ledger for five credit-union members. Not real data." },
        new object?[] { "Ledger.TxnID", "Unique id, in posting order (member prefix + sequence)" },
        new object?[] { "Ledger.Account", "CHK = checking (the account slack is measured on), SAV = savings" },
        new object?[] { "Ledger.Amount", "Signed: + money in (credit), − money out (debit)" },
        new object?[] { "Ledger.RunningBalance", "Account balance right after this row posted" },
        new object?[] { "Ledger.Channel", "ACH = bank transfer, Card = debit card, Internal = within the CU, Fee = charged by the CU" },
        new object?[] { "Product rules", "Courtesy pay covers ACH/internal debits that overdraw, for a $35 fee (max 3/day, items ≥ $15). No member opted in to card overdraft (Reg E), so card purchases that do not fit are declined and never post." },
        new object?[] { "Members", "Profile and product settings the scenario engine needs" },
    })
    .Save(outPath);

Console.WriteLine($"Wrote {outPath}");
foreach (var m in members)
{
    var rows = ledger.Where(r => (string)r[1]! == m.Id && (string)r[2]! == "CHK").ToList();
    Console.WriteLine($"  {m.Id} {m.Name,-5} rows={rows.Count,4}  fees={rows.Count(r => (string)r[5]! == "Fee"),3}  " +
                      $"min=${rows.Min(r => (double)r[8]!),9:N0}  end=${(double)rows[^1][8]!,9:N0}");
}

static IEnumerable<object?[]> Simulate(MemberSpec m, DateOnly windowStart, DateOnly end)
{
    var rng = new Random(m.Seed);
    var rows = new List<object?[]>();
    int seq = 0;
    double chk = m.OpeningChecking, sav = m.OpeningSavings;
    int spendCutDays = 0;
    var first = m.Since > windowStart ? m.Since : windowStart;
    for (var d = first; d <= end; d = d.AddDays(1))
    {
        var items = new List<(string desc, string cat, string ch, double amt)>();
        items.AddRange(m.Scheduled(d, rng));
        items.AddRange(Card(m, d, rng, spendCutDays > 0 ? 0.6 : 1.0));
        if (spendCutDays > 0) spendCutDays--;
        var credits = items.Where(i => i.amt > 0);
        var bills = items.Where(i => i.amt < 0 && i.ch != "Card").OrderBy(i => i.amt);   // largest first
        var cards = items.Where(i => i.amt < 0 && i.ch == "Card");
        int feesToday = 0;
        foreach (var (desc, cat, ch, amt) in credits.Concat(bills).Concat(cards))
        {
            if (ch == "Card" && chk + amt < 0) continue;              // Reg E: no card opt-in → declined, never posts
            chk = Math.Round(chk + amt, 2);
            rows.Add(Row(m.Id, ++seq, "CHK", d, desc, cat, ch, amt, chk));
            if (cat == "Transfer")
            {
                sav = Math.Round(sav - amt, 2);
                rows.Add(Row(m.Id, ++seq, "SAV", d, "TRANSFER FROM CHECKING", "Transfer", "Internal", -amt, sav));
            }
            if (m.CourtesyPay && ch != "Card" && amt < 0 && chk < 0 && -amt >= 15 && feesToday < 3)
            {
                feesToday++;
                chk = Math.Round(chk - 35, 2);
                rows.Add(Row(m.Id, ++seq, "CHK", d, "COURTESY PAY FEE", "Fee", "Fee", -35.0, chk));
            }
        }
        if (m.AlertsOn && chk < 200 && rng.NextDouble() < 0.35) spendCutDays = 3;   // the member reacts to the alert
    }
    return rows;
}

static object?[] Row(string id, int seq, string acct, DateOnly d, string desc, string cat, string ch, double amt, double bal) =>
    new object?[] { $"{id}-{seq:000000}", id, acct, d, desc, cat, ch, Math.Round(amt, 2), bal };

static IEnumerable<(string, string, string, double)> Card(MemberSpec m, DateOnly d, Random rng, double damp)
{
    bool weekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    double lambda = (weekend ? m.WeekendRate : m.WeekdayRate) * damp * (d.Month == 12 && d.Day < 25 ? 1.3 : 1.0) * m.Season(d);
    int n = Poisson(rng, lambda);
    double total = m.Merchants.Sum(x => x.weight);
    for (int i = 0; i < n; i++)
    {
        double u = rng.NextDouble() * total, acc = 0;
        var pick = m.Merchants[^1];
        foreach (var x in m.Merchants) { acc += x.weight; if (u <= acc) { pick = x; break; } }
        double amt = pick.ticket * m.TicketScale * Math.Exp(0.35 * Gauss(rng));
        yield return (pick.name, pick.cat, "Card", -Math.Round(amt, 2));
    }
}

static int Poisson(Random r, double lambda) { double L = Math.Exp(-lambda), p = 1; int k = 0; do { k++; p *= r.NextDouble(); } while (p > L); return k - 1; }
static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Occlet.Forecast.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

sealed class MemberSpec
{
    public required string Id, Name, Product, Notes;
    public required DateOnly Since;
    public bool CourtesyPay = true, AlertsOn;
    public double OpeningChecking, OpeningSavings, CardDebt, CardApr;
    public double WeekdayRate = 1.15, WeekendRate = 1.7, TicketScale = 1.0;
    public int Seed;
    public Func<DateOnly, double> Season = _ => 1.0;
    public required Func<DateOnly, Random, IEnumerable<(string desc, string cat, string ch, double amt)>> Scheduled;
    public (string name, string cat, double ticket, double weight)[] Merchants = Specs.DefaultMerchants;
}

static class Specs
{
    public static readonly (string, string, double, double)[] DefaultMerchants =
    {
        ("KROGER #0421", "Groceries", 48, 0.22), ("ALDI #77", "Groceries", 35, 0.08), ("SHELL OIL", "Gas", 36, 0.15),
        ("CHIPOTLE", "Dining", 15, 0.12), ("LOCAL DINER", "Dining", 22, 0.08), ("STARBUCKS", "Coffee", 6, 0.15),
        ("AMAZON MKTPLACE", "Shopping", 27, 0.12), ("TARGET", "Shopping", 33, 0.08),
    };

    // Payroll due on a weekend posts the Friday before — which can fall in the PREVIOUS month,
    // so check this month's and next month's target dates.
    static bool Payday(DateOnly d, int dom) => d == Target(d.Year, d.Month, dom) || d == Target(d.AddMonths(1).Year, d.AddMonths(1).Month, dom);
    static DateOnly Target(int y, int m, int dom)
    {
        var t = new DateOnly(y, m, Math.Min(dom, DateTime.DaysInMonth(y, m)));
        while (t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) t = t.AddDays(-1);
        return t;
    }
    static bool LastBusinessDay(DateOnly d) => Payday(d, 31);
    static double Noise(Random r, double sd) => Math.Exp(sd * Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble()));

    public static MemberSpec Asha() => new()
    {
        Id = "A", Name = "Asha", Product = "Classic Checking", Since = new DateOnly(2019, 4, 2), Seed = 101,
        OpeningChecking = 520, OpeningSavings = 150, WeekdayRate = 1.0, WeekendRate = 1.45,
        Notes = "Occlet's Member A: paid on the 8th and 23rd; rent, phone and insurance fall before payday.",
        Scheduled = (d, r) =>
        {
            var l = new List<(string, string, string, double)>();
            if (Payday(d, 8) || Payday(d, 23)) l.Add(("ACME LOGISTICS PAYROLL DIR DEP", "Income", "ACH", 1500));
            if (d.Day == 1) l.Add(("OAKWOOD APTS RENT", "Rent", "ACH", -1100));
            if (d.Day == 3) l.Add(("VERIZON WIRELESS AUTOPAY", "Phone", "ACH", -90));
            if (d.Day == 5) l.Add(("STATE FARM INSURANCE", "Insurance", "ACH", -160));
            if (d.Day == 10) l.Add(("PLANET FITNESS", "Gym", "Card", -30));
            if (d.Day == 12) l.Add(("NETFLIX.COM", "Subscription", "Card", -15.49));
            if (d.Day == 15) l.Add(("CITY UTILITIES", "Utilities", "ACH", -Math.Round((d.Month is 12 or 1 or 2 or 7 or 8 ? 160 : 118) * Noise(r, 0.05), 2)));
            if (d.Day == 17) l.Add(("SPOTIFY USA", "Subscription", "Card", -11.99));
            if (d.Day == 20) l.Add(("CU AUTO LOAN PMT", "Loan payment", "Internal", -280));
            if (d.Day == 24) l.Add(("TRANSFER TO SAVINGS", "Transfer", "Internal", -50));
            if (d == new DateOnly(2026, 3, 4)) l.Add(("IRS TREAS 310 TAX REF", "Income", "ACH", 870));
            if (d == new DateOnly(2026, 6, 14)) l.Add(("MIDAS AUTO REPAIR", "Auto repair", "Card", -460));
            return l;
        },
    };

    public static MemberSpec Ben() => new()
    {
        Id = "B", Name = "Ben", Product = "Classic Checking", Since = new DateOnly(2021, 8, 16), Seed = 202, AlertsOn = true,
        OpeningChecking = 450, OpeningSavings = 400, WeekdayRate = 1.0, WeekendRate = 1.5, TicketScale = 0.95,
        Notes = "Hourly worker paid every other Friday (hours vary). Low-balance alert already on.",
        Scheduled = (d, r) =>
        {
            var l = new List<(string, string, string, double)>();
            if ((d.DayNumber - new DateOnly(2025, 10, 31).DayNumber) % 14 == 0) l.Add(("RIVERSIDE HOSPITAL PAYROLL", "Income", "ACH", Math.Round(1140 * Noise(r, 0.08), 2)));
            if (d.Day == 1) l.Add(("MAPLE COURT RENT", "Rent", "ACH", -1050));
            if (d.Day == 2) l.Add(("PROGRESSIVE AUTO INS", "Insurance", "ACH", -140));
            if (d.Day == 15) l.Add(("DUKE ENERGY", "Utilities", "ACH", -Math.Round((d.Month is 12 or 1 or 2 or 7 or 8 ? 135 : 95) * Noise(r, 0.06), 2)));
            if (d.Day == 22) l.Add(("SPECTRUM INTERNET", "Internet", "ACH", -65));
            if (d.Day == 28) l.Add(("T-MOBILE", "Phone", "ACH", -70));
            if (d.Day == 6) l.Add(("HULU", "Subscription", "Card", -17.99));
            if (d == new DateOnly(2026, 5, 12)) l.Add(("CITY MEDICAL GROUP", "Medical", "ACH", -300));
            return l;
        },
    };

    public static MemberSpec Chen() => new()
    {
        Id = "C", Name = "Chen", Product = "Premier Checking", Since = new DateOnly(2014, 1, 9), Seed = 303,
        OpeningChecking = 6200, OpeningSavings = 8000, WeekdayRate = 1.5, WeekendRate = 2.1, TicketScale = 1.4,
        Season = d => d.Month == 8 ? 1.4 : 1.0,
        Notes = "Salaried, paid on the last business day; comfortable cushion.",
        Scheduled = (d, r) =>
        {
            var l = new List<(string, string, string, double)>();
            if (LastBusinessDay(d)) l.Add(("GLOBEX CORP PAYROLL", "Income", "ACH", 5200));
            if (d.Day == 1) l.Add(("WELLS MORTGAGE", "Mortgage", "ACH", -1850));
            if (d.Day == 4) l.Add(("GEICO", "Insurance", "ACH", -210));
            if (d.Day == 9) l.Add(("XFINITY", "Internet", "ACH", -85));
            if (d.Day == 15) l.Add(("PG&E", "Utilities", "ACH", -Math.Round(150 * Noise(r, 0.08), 2)));
            if (d.Day == 18) l.Add(("AT&T WIRELESS", "Phone", "ACH", -120));
            if (d.Day == 3) l.Add(("TRANSFER TO SAVINGS", "Transfer", "Internal", -400));
            return l;
        },
    };

    public static MemberSpec Dee() => new()
    {
        Id = "D", Name = "Dee", Product = "Classic Checking", Since = new DateOnly(2026, 6, 20), Seed = 404,
        OpeningChecking = 380, OpeningSavings = 0, WeekdayRate = 0.9, WeekendRate = 1.4, TicketScale = 0.85,
        Notes = "New member since June 2026 — about four months of history (thin file).",
        Scheduled = (d, r) =>
        {
            var l = new List<(string, string, string, double)>();
            if (Payday(d, 15) || LastBusinessDay(d)) l.Add(("BRIGHT DENTAL PAYROLL", "Income", "ACH", 1080));
            if (d.Day == 3) l.Add(("SUNSET APARTMENTS", "Rent", "ACH", -925));
            if (d.Day == 13) l.Add(("CRICKET WIRELESS", "Phone", "ACH", -55));
            if (d.Day == 12) l.Add(("ALLSTATE", "Insurance", "ACH", -95));
            if (d.Day == 20) l.Add(("CITY WATER & POWER", "Utilities", "ACH", -Math.Round(85 * Noise(r, 0.06), 2)));
            if (d.Day == 25) l.Add(("DISNEY PLUS", "Subscription", "Card", -13.99));
            return l;
        },
    };

    public static MemberSpec Eli() => new()
    {
        Id = "E", Name = "Eli", Product = "Classic Checking", Since = new DateOnly(2022, 2, 1), Seed = 505,
        OpeningChecking = 260, OpeningSavings = 60, WeekdayRate = 1.1, WeekendRate = 1.5, CardDebt = 4200, CardApr = 0.2799,
        Merchants = new (string, string, double, double)[]
        {
            ("SHELL OIL", "Gas", 42, 0.30), ("CIRCLE K", "Gas", 30, 0.12), ("WALMART", "Groceries", 44, 0.18),
            ("TACO BELL", "Dining", 11, 0.15), ("MCDONALDS", "Dining", 9, 0.10), ("AMAZON MKTPLACE", "Shopping", 25, 0.15),
        },
        Notes = "Gig driver paid weekly (Tuesdays) with ±30% swings; $4,200 on an outside card at 27.99% APR.",
        Scheduled = (d, r) =>
        {
            var l = new List<(string, string, string, double)>();
            if (d.DayOfWeek == DayOfWeek.Tuesday) l.Add(("RIDEFLOW DRIVER PAYOUT", "Income", "ACH", Math.Round(560 * Noise(r, 0.28), 2)));
            if (d.Day == 1) l.Add(("PARKVIEW RENT", "Rent", "ACH", -980));
            if (d.Day == 5) l.Add(("BOOST MOBILE", "Phone", "ACH", -60));
            if (d.Day == 9) l.Add(("NATIONWIDE AUTO INS", "Insurance", "ACH", -185));
            if (d.Day == 18) l.Add(("CAPITAL CARD MIN PAYMENT", "Card payment", "ACH", -168));
            if (d.Day == 22) l.Add(("CITY UTILITIES", "Utilities", "ACH", -Math.Round(90 * Noise(r, 0.07), 2)));
            if (d == new DateOnly(2026, 2, 10)) l.Add(("PHONE REPAIR CENTER", "Repair", "Card", -220));
            return l;
        },
    };
}
