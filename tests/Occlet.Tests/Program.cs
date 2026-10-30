using Occlet.Core;
using Occlet.Core.Engine;
using Occlet.Core.Forecasting;
using Occlet.Core.Ledger;
using Occlet.Core.Scenarios;
using Occlet.Core.Evaluation;
using Occlet.Core.Decisioning;
using Occlet.Approach2.LightGbm;

int failures = 0, passed = 0;
void Test(string name, Action check) { try { check(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Eq(double actual, double expected) { if (Math.Abs(actual - expected) > 1e-7 || !double.IsFinite(actual)) throw new Exception($"expected {expected}, got {actual}"); }
void Yes(bool ok, string why = "assertion failed") { if (!ok) throw new Exception(why); }
var origin = new DateOnly(2026, 1, 31);
var rules = new ProductRules(true, 35, 3, 15, false, 0, false);
FutureEvent Ev(int day, string cat, string channel, double amount, double principal = 0) =>
    new(day, origin.AddDays(day + 1), cat + day, cat, cat, channel, amount, 0, amount >= 0 ? ItemKind.Income : ItemKind.Bill, "test", principal);
ReplayResult Run(double start, double savings, FutureEvent[] events, int days, ProductRules? policy = null, double[]? spending = null, double[][]? fractions = null) =>
    Replay.Run(new Replay.Input(start, savings, events, days, new[] { spending ?? new double[days] }, new[] { new double[days] }, new[] { 1.0 },
        new[] { Array.Empty<double>() }, new Dictionary<string, int>(), policy ?? rules, Behaviour.None, CardFractions: fractions == null ? null : new[] { fractions }));

Test("Recurring card uses card authorization rather than courtesy pay", () => {
    var r = Run(50, 0, new[] { Ev(0, "Subscription", "Card", -90) }, 1);
    Eq(r.Paths[0][0], 50); Eq(r.FeeDollars[0], 0); Eq(r.CardDeclined[0], 90); Eq(r.UnpaidBills[0][0], 90);
});
Test("ACH courtesy pay still pays and charges its fee", () => {
    var r = Run(50, 0, new[] { Ev(0, "Rent", "ACH", -90) }, 1);
    Eq(r.Paths[0][0], -75); Eq(r.FeeDollars[0], 35);
});
Test("Checking to savings preserves funds and funds later coverage", () => {
    var r = Run(200, 0, new[] { Ev(0, "Transfer", "Internal", -100), Ev(1, "Rent", "ACH", -150) }, 2, rules with { CourtesyPay = false, SavingsFirst = true });
    Eq(r.Paths[0][0] + r.SavingsPaths[0][0], 200); Eq(r.Paths[0][1], 0); Eq(r.SavingsPaths[0][1], 50); Eq(r.BillDeclines[0], 0);
});
Test("Savings to checking cannot invent money", () => {
    var r = Run(50, 100, new[] { Ev(0, "Transfer", "Internal", 70), Ev(1, "Transfer", "Internal", 80) }, 2);
    Eq(r.Paths[0][0], 120); Eq(r.SavingsPaths[0][0], 30); Eq(r.Paths[0][1] + r.SavingsPaths[0][1], 150);
});
Test("Insufficient savings are not drained by a declined authorization", () => {
    var r = Run(20, 30, new[] { Ev(0, "Card", "Card", -80) }, 1, rules with { SavingsFirst = true });
    Eq(r.Paths[0][0], 20); Eq(r.SavingsPaths[0][0], 30);
});
Test("A single everyday card ticket cannot be partially paid", () => {
    var r = Run(50, 0, Array.Empty<FutureEvent>(), 1, spending: new[] { 80.0 }); Eq(r.Paths[0][0], 50); Eq(r.CardDeclined[0], 80);
});
Test("Ticket proportions preserve per-purchase acceptance", () => {
    var r = Run(50, 0, Array.Empty<FutureEvent>(), 1, spending: new[] { 80.0 }, fractions: new[] { new[] { 0.5, 0.5 } });
    Eq(r.Paths[0][0], 10); Eq(r.CardDeclined[0], 40);
});
Test("Rounding ticket splits cannot exceed the daily total", () => {
    var r = Run(1, 0, Array.Empty<FutureEvent>(), 1, spending: new[] { 0.03 }, fractions: new[] { Enumerable.Repeat(1.0 / 6, 6).ToArray() });
    Eq(r.Paths[0][0], .97);
});
Test("Invalid ticket proportions are rejected", () => {
    bool caught = false; try { Run(10, 0, Array.Empty<FutureEvent>(), 1, fractions: new[] { new[] { .2, .2 } }); }
    catch (ArgumentException) { caught = true; } Yes(caught);
});
Test("Fee cap applies across scheduled and everyday card payments", () => {
    var r = Run(0, 0, new[] { Ev(0, "Rent", "ACH", -20), Ev(0, "Card", "Card", -20) }, 1,
        rules with { CardOverdraftOptIn = true, MaxFeesPerDay = 2 }, new[] { 20.0 });
    Eq(r.FeeCount[0], 2); Eq(r.FeeDollars[0], 70); Eq(r.Paths[0][0], -130);
});
Test("Unpaid obligations survive the three-attempt retry limit", () => {
    var r = Run(0, 0, new[] { Ev(0, "Rent", "ACH", -100) }, 5, rules with { CourtesyPay = false });
    Eq(r.BillDeclines[0], 3); Eq(r.UnpaidBills[0][4], 100);
});
Test("Successful retry removes the obligation exactly once", () => {
    var r = Run(0, 0, new[] { Ev(0, "Rent", "ACH", -100), Ev(1, "Income", "ACH", 150) }, 3, rules with { CourtesyPay = false });
    Eq(r.BillDeclines[0], 1); Eq(r.UnpaidBills[0][2], 0); Eq(r.Paths[0][2], 50);
});
Test("Missed loan repayment leaves principal outstanding", () => {
    var r = Run(0, 0, new[] { Ev(0, "Loan proceeds", "Internal", 100, 100), Ev(0, "Rent", "ACH", -100), Ev(1, "Loan payment", "Internal", -30, -25) }, 3, rules with { CourtesyPay = false });
    Eq(r.OutstandingPrincipal[0][2], 100); Eq(r.UnpaidBills[0][2], 30);
});
Test("Paid loan repayment reduces principal and true slack uses it", () => {
    var r = Run(0, 0, new[] { Ev(0, "Loan proceeds", "Internal", 100, 100), Ev(1, "Loan payment", "Internal", -30, -25) }, 2);
    Eq(r.OutstandingPrincipal[0][1], 75); Eq(r.Paths[0][1], 70); Eq(SlackSummary.From(r, 10).LowestTrueSlack, -0.5);
});

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var members = LedgerLoader.Load(Path.Combine(root, "data/members_ledger.xlsx"));
Test("Last business day payroll remains month end", () => {
    var c = members.Single(m => m.Id == "C"); var rec = RecurringDetector.Detect(c);
    Yes(rec.Single(x => x.IsPayday).LastBusinessDay);
    Yes(RecurringDetector.Project(rec, c.LastDate, 90).Any(x => x.IsPayday && x.Date == new DateOnly(2026, 12, 31)));
    Yes(!RecurringDetector.Project(rec, c.LastDate, 90).Any(x => x.IsPayday && x.Date == new DateOnly(2026, 12, 30)));
});
Test("Next-month payday advanced into current month is included", () => {
    var item = new RecurringItem("pay", "pay", "Income", "ACH", ItemKind.Income, Cadence.Monthly, 30, 1, origin, 100, 0, 12);
    var events = RecurringDetector.Project(new[] { item }, new DateOnly(2026, 1, 29), 2);
    Yes(events.Any(x => x.Date == new DateOnly(2026, 1, 30)));
});
Test("Refinance is not affordable solely because payment falls", () => {
    var e = members.Single(m => m.Id == "E"); var b = BaselineBuilder.Build(e, e.LastDate, new BootstrapForecaster(), paths: 100);
    var (catalog, _) = LeverCatalog.Build(b); var result = new ScenarioEngine(b).Run(new[] { catalog.Single(l => l.Id == "refinance_card") });
    Yes(!result.GatesPass);
});
Test("Frozen hash includes event uncertainty and checks cache hits", () => {
    var m = members[0]; var b = BaselineBuilder.Build(m, m.LastDate, new BootstrapForecaster(), paths: 10); var list = (List<FutureEvent>)b.Events;
    var original = list[0]; list[0] = original with { AmountCv = 0.4 }; Yes(b.ComputeHash() != b.FrozenHash); list[0] = original;
    var engine = new ScenarioEngine(b); engine.Run(Array.Empty<Lever>()); b.Everyday[0][0] += 1;
    bool caught = false; try { engine.Run(Array.Empty<Lever>()); } catch (InvalidOperationException) { caught = true; } Yes(caught);
});
Test("Identity scenario reproduces every baseline path", () => {
    var m = members[0]; var b = BaselineBuilder.Build(m, m.LastDate, new BootstrapForecaster(), paths: 25); var r = new ScenarioEngine(b).Run(Array.Empty<Lever>());
    for (int p = 0; p < b.Paths; p++) for (int d = 0; d < b.Horizon; d++) { Eq(r.Result.Paths[p][d], b.Result.Paths[p][d]); Eq(r.Result.SavingsPaths[p][d], b.Result.SavingsPaths[p][d]); }
});

var tx = new List<Transaction>(); var first = new DateOnly(2025, 11, 1); var cutoff = new DateOnly(2026, 3, 31); double balance = 10000;
for (var date = first; date <= cutoff; date = date.AddDays(1)) {
    balance -= 5; tx.Add(new("g" + date, "X", "CHK", date, "food" + date, "Groceries", "Card", -5, balance));
    if (date.Year == 2026 && date.Day == 15) { balance -= 100; tx.Add(new("r" + date, "X", "CHK", date, "RENT", "Rent", "ACH", -100, balance)); }
}
var history = new MemberHistory(new MemberProfile { MemberId = "X", Name = "X", MemberSince = first }, tx, cutoff);
Test("Future transactions cannot change earlier feature rows", () => {
    var exact = FeatureBuilder.FromHistory(history.UpTo(origin)); var later = FeatureBuilder.FromHistory(history);
    var x = exact.Rows(origin, new[] { 30 }); var y = later.Rows(origin, new[] { 30 });
    for (int i = 0; i < x.Length; i++) Eq(x[i], y[i]); Eq(exact.Scale(origin), later.Scale(origin));
});
Test("Target classification remains fixed at prediction origin", () => {
    var fb = FeatureBuilder.FromHistory(history.UpTo(origin)); var target = fb.Targets(history, new[] { 28 })[0];
    Eq(target, (28 * 5.0 + 100) / (fb.Scale(origin) * 28));
});
Test("Labels beyond observed data are rejected", () => {
    bool caught = false; try { FeatureBuilder.FromHistory(history).Targets(history, new[] { 30 }); } catch (ArgumentException) { caught = true; } Yes(caught);
});
Test("Endpoint and daily-window evaluation cannot be confused", () => {
    var points = Enumerable.Range(0, 90).Select(d => new BacktestPoint("test", "X", origin, 90, d, d == 89 ? -100 : 0, -1, 0, 1, 1));
    var metric = Backtester.Score(points, Array.Empty<BacktestWindow>()).Single();
    Eq(metric.Coverage80, 0); Eq(metric.BelowQ10, 1); Eq(metric.WindowCoverage80, 89.0 / 90); Eq(metric.EndpointsScored, 1); Yes(!metric.PassesAllGates);
});
BacktestMetric Good(string model, int horizon, double loss = 1) => new(model, horizon, 48, 48 * horizon, .8, .1, .1, loss, 1, .8, .1, true, true, 48, .8, .1, loss, true);
Test("Champion abstains if coverage fails even when lower tail passes", () => {
    var rows = Settings.ReportHorizons.Select(h => Good("Bootstrap", h) with { PassesCoverageGate = false }); Yes(ModelSelection.Select(rows) == null);
});
Test("Champion abstains if minimum risk fails", () => {
    var rows = Settings.ReportHorizons.Select(h => Good("Bootstrap", h) with { PassesMinimumGate = false }); Yes(ModelSelection.Select(rows) == null);
});
Test("Missing horizon cannot pass acceptance", () => { Yes(!ModelSelection.IsEligible(new[] { Good("Bootstrap", 30) })); });
Test("Qualified challenger must meet improvement margin", () => {
    var rows = Settings.ReportHorizons.Select(h => Good("Bootstrap", h)).Concat(Settings.ReportHorizons.Select(h => Good("Other", h, .97))); Yes(ModelSelection.Select(rows) == "Bootstrap");
    rows = Settings.ReportHorizons.Select(h => Good("Bootstrap", h)).Concat(Settings.ReportHorizons.Select(h => Good("Other", h, .9))); Yes(ModelSelection.Select(rows) == "Other");
});
Test("Blocked plan cannot emit member advice", () => {
    var m = members[0]; var b = BaselineBuilder.Build(m, m.LastDate, new BootstrapForecaster(), paths: 5); var engine = new ScenarioEngine(b);
    var r = engine.Run(Array.Empty<Lever>()); var plan = new Plan { Baseline = r, Final = r }; plan.BlockRecommendation("test"); Yes(plan.MemberSentence().StartsWith("No validated recommendation"));
});
Console.WriteLine($"{passed} passed; {failures} failed");
return failures == 0 ? 0 : 1;
