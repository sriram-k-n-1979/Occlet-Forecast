using Occlet.Core.Engine;
using Occlet.Core.Forecasting;

namespace Occlet.Core.Scenarios;

/// <summary>
/// Builds the levers that make sense for THIS member (Occlet's four families), plus notes on
/// levers that were skipped and why (e.g. "alerts already on").
/// </summary>
public static class LeverCatalog
{
    public static (List<Lever> levers, List<string> notes) Build(Baseline b)
    {
        var levers = new List<Lever>();
        var notes = new List<string>();
        var probe = new ScenarioState(b);

        // ── Tier 1 · Behaviour ────────────────────────────────────────────
        // Move movable bills (phone, insurance, utilities, subscriptions) that fall in a tight window
        var tight = b.Events.Where(e => e.Kind == ItemKind.Bill && Settings.MovableBillCategories.Contains(e.Category))
                            .Where(e => { int pay = probe.NextPaydayAfter(e.Day); return pay > 0 && pay - e.Day <= Settings.TightWindowDays; })
                            .ToList();
        if (tight.Count > 0)
        {
            var cats = tight.Select(e => e.Category.ToLowerInvariant()).Distinct().ToList();
            string what = string.Join(" + ", cats);
            levers.Add(new Lever
            {
                Id = "move_bills", Tier = Tier.Behaviour, Method = LeverMethod.Exact,
                Label = $"Move {what} autopay to the day after payday",
                MemberText = $"move your {(cats.Count == 1 ? cats[0] : string.Join(", ", cats.Take(cats.Count - 1)) + " and " + cats[^1])} autopay to the day after payday",
                Apply = st =>
                {
                    for (int i = 0; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind != ItemKind.Bill || !Settings.MovableBillCategories.Contains(e.Category)) continue;
                        int pay = st.NextPaydayAfter(e.Day);
                        if (pay > 0 && pay - e.Day <= Settings.TightWindowDays) st.Events[i] = st.MoveTo(e, pay + 1);
                    }
                },
            });
        }
        else notes.Add("move_bills: no movable bill falls within 5 days before a payday.");

        // Rent / mortgage: only if it is not already right after a payday; needs the landlord's agreement
        var housing = b.Events.Where(e => e.Kind == ItemKind.Bill && Settings.HousingCategories.Contains(e.Category)).ToList();
        bool housingMisaligned = housing.Any(e =>
        {
            int prev = probe.PrevPaydayOnOrBefore(e.Day), next = probe.NextPaydayAfter(e.Day);
            return next > 0 && e.Day - prev > 2;
        });
        if (housingMisaligned)
            levers.Add(new Lever
            {
                Id = "move_rent", Tier = Tier.Behaviour, Method = LeverMethod.Exact,
                Label = $"Ask to move the {housing[0].Category.ToLowerInvariant()} due date to the day after payday",
                MemberText = $"ask your landlord to move the {housing[0].Category.ToLowerInvariant()} due date to the day after payday",
                Caveat = "Needs the landlord's or lender's agreement; if they refuse, this lever is unavailable.",
                RequiresConsent = true,
                Apply = st =>
                {
                    for (int i = 0; i < st.Events.Count; i++)
                    {
                        var e = st.Events[i];
                        if (e.Kind != ItemKind.Bill || !Settings.HousingCategories.Contains(e.Category)) continue;
                        int prev = st.PrevPaydayOnOrBefore(e.Day), next = st.NextPaydayAfter(e.Day);
                        if (next > 0 && e.Day - prev > 2) st.Events[i] = st.MoveTo(e, next + 1);
                    }
                },
            });
        else if (housing.Count > 0) notes.Add("move_rent: housing payment already falls within 2 days after a payday.");

        // Low-balance alert (learned)
        if (b.Profile.LowBalanceAlert)
            notes.Add("alerts: already active — its effect is already in the member's history, so it is not offered again.");
        else
            levers.Add(new Lever
            {
                Id = "alerts", Tier = Tier.Behaviour, Method = LeverMethod.Learned,
                Label = $"Turn on low-balance alert at ${Settings.AlertThreshold:N0}",
                MemberText = $"turn on a low-balance alert at ${Settings.AlertThreshold:N0}",
                Caveat = $"Learned: assumes {Pct(Settings.AlertResponseRate)} of alerts lead to a ~${Settings.AlertCut:N0} cut (Occlet's illustrative figure).",
                Apply = st => st.Behaviour = st.Behaviour with { AlertThreshold = Settings.AlertThreshold },
            });

        // Trim the biggest discretionary category (learned)
        var top = b.Series.CategoryShare.Where(kv => Settings.DiscretionaryCategories.Contains(kv.Key))
                                        .OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (top.Key != null && top.Value >= 0.05)
        {
            double factor = 1 - top.Value * Settings.TrimCut;
            levers.Add(new Lever
            {
                Id = $"trim_{top.Key.ToLowerInvariant()}", Tier = Tier.Behaviour, Method = LeverMethod.Learned,
                Label = $"Trim {top.Key.ToLowerInvariant()} by {Pct(Settings.TrimCut)} ({Pct(top.Value)} of everyday spend)",
                MemberText = $"spend about {Pct(Settings.TrimCut)} less on {top.Key.ToLowerInvariant()}",
                Caveat = $"Learned: assumes {Pct(Settings.TrimAdherence)} of members stick to it.",
                Apply = st => st.Behaviour = st.Behaviour with { SpendFactorIfAdhering = st.Behaviour.SpendFactorIfAdhering * factor, Adherence = Settings.TrimAdherence },
            });
        }

        // ── Tier 2 · Deposit product ─────────────────────────────────────
        if (!b.Rules.SavingsFirst && b.StartSavings > 0)
            levers.Add(new Lever
            {
                Id = "savings_first", Tier = Tier.Product, Method = LeverMethod.Exact,
                Label = "Switch on savings-first overdraft order",
                MemberText = "let your savings cover a shortfall before any overdraft fee",
                Apply = st => st.Rules = st.Rules with { SavingsFirst = true },
            });
        else notes.Add(b.Rules.SavingsFirst ? "savings_first: already on." : "savings_first: no savings balance to draw on.");

        if (b.Rules.CourtesyPay)
            levers.Add(new Lever
            {
                Id = "no_fee_account", Tier = Tier.Product, Method = LeverMethod.Exact,
                Label = "Fee-free account without courtesy pay",
                MemberText = "switch to the fee-free account",
                Caveat = "Fees can turn into declined payments — shown side by side.",
                Apply = st => st.Rules = st.Rules with { CourtesyPay = false, NsfFee = 0 },
            });

        // ── Tier 3 · Liquidity ───────────────────────────────────────────
        levers.Add(CashLoan(b));
        var cardPay = b.Recurring.FirstOrDefault(r => r.Kind == ItemKind.Bill && r.Category == "Card payment");
        if (cardPay != null && b.Profile.ExternalCardDebt > 0) levers.Add(Refinance(b, cardPay));
        else notes.Add("refinance: no outside credit-card debt on file.");

        // ── Tier 4 · Income ──────────────────────────────────────────────
        levers.Add(new Lever
        {
            Id = "income_up_10", Tier = Tier.Income, Method = LeverMethod.Exact,
            Label = "Income +10% from day 15 (e.g. extra hours)",
            MemberText = "add about 10% to your pay (for example extra hours)",
            Apply = st =>
            {
                for (int i = 0; i < st.Events.Count; i++)
                    if (st.Events[i].IsPayday && st.Events[i].Day >= 15) st.Events[i] = st.Events[i] with { Amount = st.Events[i].Amount * 1.10 };
            },
        });
        levers.Add(new Lever
        {
            Id = "stress_income_-20", Tier = Tier.Income, Method = LeverMethod.Exact, Surfaced = false,
            Label = "STRESS TEST: income −20% (member-agreed, never recommended)",
            MemberText = "", Caveat = "Stress test only.",
            Apply = st =>
            {
                for (int i = 0; i < st.Events.Count; i++)
                    if (st.Events[i].IsPayday) st.Events[i] = st.Events[i] with { Amount = st.Events[i].Amount * 0.80 };
            },
        });
        return (levers, notes);
    }

    private static string Pct(double x) => $"{x * 100:0}%";

    public static double Payment(double principal, double apr, int months)
    {
        double r = apr / 12;
        return principal * r / (1 - Math.Pow(1 + r, -months));
    }

    /// <summary>Monthly surplus = recurring income − recurring bills − typical everyday spending.</summary>
    public static double MonthlySurplus(Baseline b) =>
        b.Recurring.Where(r => r.Kind == ItemKind.Income).Sum(r => r.MonthlyAmount)
        + b.Recurring.Where(r => r.Kind == ItemKind.Bill).Sum(r => r.MonthlyAmount)
        - b.Everyday.Average(p => p.Average()) * 30.4;

    private static Lever CashLoan(Baseline b)
    {
        double principal = Settings.CashLoanPrincipal, pay = Payment(principal, Settings.CashLoanApr, Settings.CashLoanMonths);
        return new Lever
        {
            Id = "cash_loan", Tier = Tier.Liquidity, Method = LeverMethod.Exact,
            Label = $"${principal:N0} personal loan, {Settings.CashLoanMonths} mo @ {Settings.CashLoanApr * 100:0.00}% (${pay:N0}/mo)",
            MemberText = $"take a ${principal:N0} credit-union loan",
            Caveat = "Borrowed principal is excluded from TRUE slack; repayments count as spending.",
            Apply = st =>
            {
                st.Events.Add(new FutureEvent(0, b.DateAt(0), "loan_proceeds", "CU PERSONAL LOAN PROCEEDS", "Loan proceeds", "Internal", principal, 0, ItemKind.Income, "scenario", PrincipalChange: principal));
                double owed = principal, r = Settings.CashLoanApr / 12;
                int paid = 0;
                for (int d = 0; d < b.Horizon; d++)
                {
                    var date = b.DateAt(d);
                    if (date.Day == 10 && d >= 14 && paid < Settings.CashLoanMonths)
                    {
                        double principalPaid = Math.Min(owed, pay - owed * r);
                        owed -= principalPaid; paid++;
                        st.Events.Add(new FutureEvent(d, date, $"loan_pmt@{date:yyyy-MM-dd}", "CU PERSONAL LOAN PMT", "Loan payment", "Internal", -pay, 0, ItemKind.Bill, "scenario", PrincipalChange: -principalPaid));
                    }
                }
                st.ExtraDailySpend += pay / 30.4;
            },
            Gate = st => Affordability(st, pay),
        };
    }

    /// <summary>Prototype affordability policy on the complete candidate, not just payment reduction.</summary>
    private static (bool pass, string why) Affordability(ScenarioState st, double payment)
    {
        double months = st.Baseline.Horizon / 30.4;
        double net = st.Events.Where(e => e.Category is not "Loan proceeds" and not "Transfer").Sum(e => e.Amount) / months;
        // Do not assume unmeasured alert/trim responses will make a loan affordable.
        double spending = st.Baseline.Everyday.Average(path => path.Average()) * 30.4;
        double afterPayment = net - spending;
        double beforePayment = afterPayment + payment;
        bool pass = afterPayment > 0 && payment <= Settings.AtrShareOfSurplus * beforePayment;
        return (pass, $"candidate monthly surplus after payments ${afterPayment:N0}; payment ${payment:N0} must fit {Settings.AtrShareOfSurplus:P0} of pre-payment surplus ${beforePayment:N0}");
    }

    private static Lever Refinance(Baseline b, RecurringItem card)
    {
        double debt = b.Profile.ExternalCardDebt, oldPay = -card.Amount;
        double newPay = Payment(debt, Settings.RefiApr, Settings.RefiMonths);
        return new Lever
        {
            Id = "refinance_card", Tier = Tier.Liquidity, Method = LeverMethod.Exact,
            Label = $"Refinance ${debt:N0} card debt ({b.Profile.ExternalCardApr * 100:0.00}% APR) into a {Settings.RefiMonths}-mo CU loan @ {Settings.RefiApr * 100:0.00}%: ${oldPay:N0} → ${newPay:N0}/mo",
            MemberText = $"move your ${debt:N0} credit-card balance to a lower-rate credit-union loan",
            Caveat = "Debt conversion (Form 2): no new cash, so nothing is added to the balance; only the payment changes.",
            Apply = st =>
            {
                for (int i = 0; i < st.Events.Count; i++)
                    if (st.Events[i].Description == card.Description)
                        st.Events[i] = st.Events[i] with { Description = "CU CONSOLIDATION LOAN PMT", Category = "Loan payment", Amount = -newPay, AmountCv = 0 };
                st.ExtraDailySpend += (newPay - oldPay) / 30.4;   // negative: the payment falls
            },
            Gate = st => newPay >= oldPay
                ? (false, "refinance does not lower the payment") : Affordability(st, newPay),
        };
    }
}
