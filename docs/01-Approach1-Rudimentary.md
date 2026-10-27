# Approach 1: the rudimentary solution, explained line by line

This document walks through the whole Approach 1 program in reading order, from the spreadsheet to the waterfall. Every financial term is explained the first time it appears. All data is made up.

> **The idea in one paragraph.** A member's balance moves for two kinds of reasons. Some are **known in advance**: paydays and scheduled bills. Others are **uncertain**: everyday spending on groceries, gas, coffee and surprises. Known things are added with plain arithmetic. For the uncertain part, Approach 1 imagines 2,000 possible futures by re-using real past days of the member's own spending, picked at random (a "bootstrap"). Replaying the 90 days in each future gives 2,000 balance paths. The 10th, 50th and 90th percentile on each day are the pessimistic, likely and optimistic lines (Problem 1). For Problem 2, the program copies those exact futures, changes one thing (a lever), replays them again and compares. Because the futures are identical, any difference is caused by the lever alone.

```
members_ledger.xlsx ──► LedgerLoader ──► MemberHistory (one per member)
                                             │
              RecurringDetector ◄────────────┤  paydays & bills (known future)
              EverydaySeries    ◄────────────┤  everyday spending (uncertain)
                                             ▼
                   BootstrapForecaster.SamplePaths  → 2,000 × 90 days of everyday spend
                                             ▼
                   Replay.Run (fees, declines, savings-first, alerts) → 2,000 balance paths
                                             ▼
                   SlackSummary  → q10/q50/q90, slack, TRUE slack, risk days    ← PROBLEM 1 (frozen)
                                             ▼
          LeverCatalog → ScenarioEngine (copy → edit → replay same futures)    ← PROBLEM 2
                                             ▼
                   Recommender (4 rules) → plan + waterfall + member sentence
```

Run it with `dotnet run --project src/Occlet.Approach1`. It takes about 8 seconds and writes `output/approach1/`.

---

## 0. The sample ledger: `data/members_ledger.xlsx`

`src/Occlet.DataGen/Program.cs` builds one year of transactions (28 Oct 2025 to 27 Oct 2026) for five members, each chosen to exercise a different part of the problem:

| Member | Situation | Why it is there |
|---|---|---|
| **A · Asha** | Paid $1,500 on the 8th and 23rd. Rent ($1,100, 1st), phone ($90, 3rd) and insurance ($160, 5th) all land *before* payday | Occlet's own "Member A": the problem is timing |
| **B · Ben** | Hourly worker paid **every other Friday**; the amount varies ±8%. Low-balance alert **already on** | Tests non-monthly pay and the "lever already active" edge case |
| **C · Chen** | Salaried, paid on the last business day, large cushion | The engine should recommend **nothing** |
| **D · Dee** | Joined in June 2026: about 4 months of history | A **thin file**: too short to back-test |
| **E · Eli** | Gig driver paid **weekly** with ±30% swings; $4,200 on an outside credit card at 27.99% APR | Spending exceeds income (**structural gap**), and has debt that could be refinanced |

The spreadsheet has three sheets.

- **Ledger.** One row per posted transaction:
  - `TxnID`, `MemberID`
  - `Account`: `CHK` is checking, the everyday account slack is measured on; `SAV` is savings.
  - `PostedDate`, `Description`, `Category`
  - `Channel`: `ACH` is a bank transfer, `Card` a debit card, `Internal` a move within the credit union, `Fee` a charge by the credit union.
  - `Amount`: signed, so `+` is money in (a *credit*) and `−` is money out (a *debit*).
  - `RunningBalance`: the balance right after the row posted.

  This is what a ledger is: an append-only, ordered record of money movements in which every row carries the resulting balance.
- **Members.** Each member's product settings, the things the core banking system would know:
  - **Courtesy pay** (the credit union pays a bill that overdraws the account, but charges a $35 fee, at most 3 per day, only on items ≥ $15).
  - **Card overdraft opt-in.** Under the US rule *Regulation E*, a bank may only charge overdraft fees on debit-card purchases if the member opted in; none did, so card purchases that don't fit are simply **declined**.
  - **Savings-first order** (cover a shortfall from savings before any fee), the **alert** setting, and any **outside card debt**.
- **README.** What each column means.

The generator posts each day's money in first, then bills largest-first, then card purchases. That is the same order the replay engine uses later, so simulated and real balances can be compared like for like.

---

## 1. Settings: `src/Occlet.Core/Settings.cs`

Every number that is a business decision lives here, because Occlet's design doc lists many of them as open questions for the credit union.

```csharp
public const int Horizon = 90;                       // forecast 90 days; report 30, 60 and 90
public static int Paths { get; set; } = 2000;        // 2,000 simulated futures per member
public static int Seed { get; set; } = 7;            // fixed seed → identical answer every run (auditability)
public const int SpendWindowDays = 90;               // "average daily spend" window (slack's denominator)
public const double HealthyBalance = 150.0;          // a day that STARTS below $150 may have declined card purchases
public const int TightWindowDays = 5;                // Occlet: a bill ≤ 5 days before payday is a "tight window"
public const double SlackGoalDays = 2.0;             // goal: ≥ 2 days of spending in hand on the tightest day
public const double MinBehaviourGain = 0.1;          // a behaviour lever must add ≥ 0.1 day to be worth suggesting
public const double ArbitrationMarginDays = 1.0;     // rule 3: a lower-priority lever must add ≥ 1 day of TRUE slack ...
public const double HighConfidence = 0.80;           // ... and be positive in ≥ 80% of the futures
```

The learned-lever assumptions (35% of alerts lead to a roughly $40 trim; half of members stick to a 25% category cut) are Occlet's illustrative figures. In production they would be measured on similar members.

---

## 2. Reading the ledger: `Ledger/LedgerLoader.cs`, `Ledger/Models.cs`, `Io/Xlsx.cs`

**`XlsxReader.ReadSheet`.** An `.xlsx` file is a zip of XML files, so the .NET base library is enough and **no NuGet package** is needed.

```csharp
using var zip = ZipFile.OpenRead(path);                         // open the .xlsx as a zip
var workbook = Load(zip, "xl/workbook.xml");                    // list of sheets
string relId = (string)sheet.Attribute(R + "id")!;              // the sheet's relationship id …
string target = …"xl/_rels/workbook.xml.rels"…                  // … tells us which XML file holds it
… sharedStrings.xml …                                           // Excel stores each distinct text once; cells point to it
cells[col] = type switch { "s" => shared[int.Parse(v)], "inlineStr" => …, "b" => …, _ => v };
```

**`LedgerLoader.Load`** maps columns by header name (so column order doesn't matter), parses dates (Excel stores them as serial numbers such as `46322`), and creates one `MemberHistory` per member. Every member's history runs to the **extract date**, the last date anywhere in the ledger, even if that member had no transaction that day.

**`MemberHistory`** is the most-used object in the code.

```csharp
Transactions = transactions.Where(t => t.Date <= asOf).OrderBy(t => t.Date).ToList(); // stable sort keeps posting order
_chk = EndOfDay("CHK");                                     // one balance per calendar day …
while (j < rows.Count && rows[j].Date <= day) current = rows[j++].RunningBalance;   // … = last row of the day
result[i] = current;                                        // quiet days carry yesterday's balance forward
public MemberHistory UpTo(DateOnly cutoff) => new(Profile, Transactions, cutoff …);  // ← no peeking at the future
```

`UpTo` matters a great deal. Every forecast, and every back-test, first cuts the history at its origin date, so a model can never use information it would not have had on that day.

---

## 3. The known future: `Forecasting/Recurring.cs`

**`RecurringDetector.Detect`** finds paydays and bills automatically by asking, for each `Description`, whether it repeats on a steady rhythm for a steady amount.

```csharp
var rows = h.Checking.Where(t => t.Category != "Fee" && …);     // fees are an OUTPUT of the rules, never an input
if (occ.Count < 3) continue;                                     // need at least 3 occurrences
if (cv > 0.40) continue;                                         // amount too irregular to call "scheduled"
```

Then it tries four rhythms in order:

1. **Twice a month on fixed days** (Asha's 8th and 23rd). In ≥ 80% of complete months the item appears exactly twice, and each "rank" (first or second pay of the month) sits on a stable day (spread ≤ 2.5 days). Only **complete** calendar months are counted; a half month at either end would make an item look "missing". This rule produces two monthly items.
2. **Weekly** (Eli): the median gap is 6–8 days and ≥ 75% of gaps are within a day of 7.
3. **Every two weeks** (Ben): the median gap is 13–15 days and ≥ 75% of gaps are within a day of 14. Twice-monthly is tested first because its gaps (15, 16, 14 …) would otherwise pass as biweekly; biweekly pay drifts through the month, so it fails test 1.
4. **Monthly** on a fixed day: the median gap is 27–34 days and the day of month is stable.

```csharp
double amount = cv < 0.03 ? Stats.Median(amounts.TakeLast(6)) : recent.Average();
```

For fixed amounts, the recent median catches a price change. For varying amounts (hours worked, gig payouts) it uses the **mean**, because the simulation draws amounts around it and must preserve the average. An item that has not appeared for 1.5 periods is treated as cancelled and dropped.

`LevelCv` records how uncertain the *estimated average* is: the standard error, CV ÷ √n. Eli's payouts swing ±25%, so even the "typical payout" is uncertain. Every future gets one shared shock for it (§6).

**`Project`** lays the items onto the next 90 days:

```csharp
if (it.IsPayday) while (d.DayOfWeek is Saturday or Sunday) d = d.AddDays(-1);   // payroll due on a weekend posts Friday
int k = (int)Math.Ceiling((first.DayNumber - it.LastSeen.DayNumber) / (double)it.PeriodDays);  // next weekly/biweekly date
return events.OrderBy(e => e.Day).ThenByDescending(e => e.Amount);               // money in first, big bills first
```

Each future event gets a stable key such as `OAKWOOD APTS RENT@2026-11-01`. Moving a bill keeps its key, which keeps its random amount draw the same across scenarios.

---

## 4. The uncertain part: `Forecasting/Everyday.cs`

**Everyday spending** is every checking debit that is not a recurring bill, not a fee and not a transfer to the member's own savings.

```csharp
if (t.Amount >= 0 || t.Category is "Fee" or "Transfer" || recurringDescriptions.Contains(t.Description)) continue;
posted[i] += -t.Amount;
```

**The censoring problem.** On days when money runs low, card purchases are *declined*, so the ledger records less than the member wanted to spend. Learning from those days would make the forecast too optimistic: it would assume a member spends less exactly when they are short.

```csharp
double startOfDay = … h.CheckingEod(d.AddDays(-1));
healthy[i] = startOfDay >= Settings.HealthyBalance;      // started the day with ≥ $150 → ordinary purchases fit
```

A subtlety found while testing: filtering on the *end*-of-day balance looks tempting but is wrong. Heavy-spending days are exactly the days that end low, so they would be dropped and spending would be under-stated. The start-of-day balance is fixed before the day's spending happens, so filtering on it causes no such bias. Un-healthy days get an **imputed** value: the average of the previous 60 days' healthy days of the same type (weekday or weekend).

**`SpendStats.AverageDailySpend`** is slack's denominator: every checking outflow (rent, bills, card, fees) except transfers to one's own savings, averaged over 90 days. Asha's is $95.05 a day.

---

## 5. The rudimentary forecaster: `Forecasting/BootstrapForecaster.cs`

```csharp
for (int i = from; i < s.Length; i++)                            // last 180 days
{
    if (!s.Healthy[i]) continue;                                 // only uncensored days
    (EverydaySeries.IsWeekend(s.DateAt(i)) ? weekend : weekday).Add(s.Posted[i]);
}
```

There are two pools of real past days, weekdays and weekends, because people spend differently on weekends.

```csharp
for (int end = from + 29; end < s.Length; end += 7)              // every week …
    moods.Add(mean of the 30 days ending there / overall mean);  // … how heavy was that month?
```

The **spending moods** fix the one weakness of a plain bootstrap. Picking days independently makes the 90-day total far too predictable, because highs and lows cancel out. Real people have heavy months and light months, so each future draws one mood multiplier from the member's own history.

```csharp
double mood = moods[rng.Next(moods.Count)];
row[d] = pool[rng.Next(pool.Count)] * mood;                      // one real past day × this future's mood
```

That is the entire forecaster: no model and no training. It is also the benchmark Approach 2 must beat.

---

## 6. The replay engine: `Engine/Replay.cs`

This is the heart of both problems: pure arithmetic, one simulated future at a time, one day at a time. It encodes the **deposit product's rules**.

```csharp
foreach (var e in byDay[d]) { double amt = Draw(e, p, inp); if (amt >= 0) bal += amt; else bills.Add((-amt, 0)); }
```

**1) Money in first, then collect the day's bills.** `Draw` gives a variable item (Eli's payout) a different but repeatable amount in each future:

```csharp
return e.Amount * level * Math.Exp(sigma * inp.EventZ[p][k] - sigma * sigma / 2);
//              └ shared "typical amount" shock  └ this occurrence's own swing (mean-preserving log-normal)
```

```csharp
bills.Sort((a, b) => b.amount.CompareTo(a.amount));               // 2) bills, largest first
if (r.SavingsFirst) PullFromSavings(…);                           //    savings-first: top up from savings first
if (r.CourtesyPay) { bal -= amt; if (shortfall && …) bal -= r.OdFee; }   // paid even if it overdraws … for a fee
else if (shortfall) { BillDeclines++; retry.Add((amt, attempt + 1)); }   // no courtesy pay: declined, re-presented tomorrow
```

**2) Bills.** This is where **overdraft** (the balance goes below $0 because the payment was allowed) and **NSF** (non-sufficient funds: the payment is refused) are encoded. A declined ACH bill is re-presented up to twice, as real billers do.

```csharp
double fits = Math.Max(bal, 0), paid = Math.Min(spend, fits);
res.CardDeclined[p] += spend - paid;                              // 3) Reg E: a card purchase that doesn't fit is declined
```

**3) Everyday spending.** Card purchases that don't fit are declined, with no fee.

```csharp
if (bh.AlertThreshold is double th && bal < th && cutDays == 0 && inp.AlertU[p][d] < bh.AlertResponseRate)
{ cutLeft = bh.AlertCut; cutDays = bh.AlertDays; }               // 4) learned lever: some members react to an alert
```

**4) Alerts.** If an alert is on and the balance drops below $150, in 35% of futures the member trims $40 over the next 3 days. The random draw `AlertU` is made once in the baseline, so every scenario sees the same luck.

---

## 7. From paths to slack: `Engine/Slack.cs`

```csharp
for (int d = 0; d < H; d++) { … Array.Sort(col); q10[d] = Quantile(col, 0.10); q50[d] = …; q90[d] = …; }
```

On each day, the 2,000 balances are sorted and the 10th, 50th and 90th percentiles taken. Because each day is sorted, **the three lines can never cross**, which is one of Occlet's stated problems with independently trained quantile models.

```csharp
lo = Math.Min(lo, b - (owed?[d] ?? 0));       // TRUE slack numerator: balance minus borrowed principal still owed
pathLowest[p] = lo / denom;                    // denom = average daily spend + loan repayment per day
LowestTrueSlack = Stats.Median(pathLowest);    // ← the decision score
```

Two definitions matter here.

- **Slack** = balance ÷ average daily spend: how many days the money would last.
- **True slack** removes borrowed money that is still owed and counts the loan repayment as spending, so a loan cannot fake an improvement.

The decision score is the **lowest true slack**: for each future, the true slack on its single tightest day, then the median across futures. This is deliberately a *path* measure. "How close to empty do you get?" depends on the whole path, and reading the minimum of the q50 line would understate the danger.

The summary also reports:

- **Headline slack** at 30, 60 and 90 days (q50 balance ÷ spend).
- **Risk days**: days where even the q10 line is below $0.
- **P(any overdraft)**.
- **Expected fees, declines and savings pulled.**

---

## 8. Problem 1 assembled and frozen: `Engine/Baseline.cs`

```csharp
var ctx = Context(member, origin, horizon);                     // history UpTo(origin), recurring items, projected events
var everyday = forecaster.SamplePaths(ctx, horizon, P, rng);    // the uncertain part (2,000 × 90)
var alertU = …; var leverU = …; var eventZ = …; var itemLevel = …;   // ALL other randomness, drawn once
b.Freeze(result, SlackSummary.From(result, ads));               // FrozenHash = SHA-256 of every input above
```

The random generator is seeded from (seed, member, origin, forecaster) with a stable hash, so runs are **identical**; I checked this by running twice and comparing the report byte for byte. `FrozenHash` fingerprints every input, and the scenario engine refuses to run if it changes, which makes "frozen" a checked property rather than a promise.

**`Explainer.Dip`** writes the member-facing explanation by naming the bills in the six days before the lowest point of the likely line, and the next payday:

> *Asha:* "Your balance is likely lowest on Jan 07 (about $0, 0.0 days of spending). What pulls it down: rent $1,100 on Jan 01, phone $90 on Jan 03, insurance $160 on Jan 05. Payday on Jan 08 brings it back."

**`ArtifactWriter.Write`** saves `artifacts/baseline_<member>_bootstrap.json`. It contains:

- model version, feature hash, ledger hash, frozen-inputs hash, origin and seed
- every recurring item and projected event
- the daily q10/q50/q90 path, the metrics, and the explanation

This is the versioned baseline artifact Occlet's doc asks Phase 2 to consume.

---

## 9. Problem 2: levers and the scenario engine (`Scenarios/`)

**`LeverCatalog.Build`** proposes only levers that make sense for *this* member, and notes why others were skipped.

| Lever | Tier | Method | Edit it makes |
|---|---|---|---|
| `move_bills` | Behaviour | Exact | Movable bills (phone, insurance, utilities, subscriptions) due ≤ 5 days before a payday move to the day after it |
| `move_rent` | Behaviour | Exact | Rent moves to the day after payday. **Needs the landlord's consent**, so it is tried after the member's own levers |
| `alerts` | Behaviour | Learned | Turns the alert on (not offered if already on, as for Ben: its effect is already in his history) |
| `trim_<category>` | Behaviour | Learned | The biggest discretionary category −25%, in 50% of futures |
| `savings_first` | Product | Exact | Savings covers a shortfall before any fee (offered only if there is savings) |
| `no_fee_account` | Product | Exact | No courtesy pay: bills that don't fit are **declined** instead |
| `cash_loan` | Liquidity | Exact | $1,000 now, 12 monthly payments; gated by **ability-to-repay** |
| `refinance_card` | Liquidity | Exact | Outside card debt → a cheaper credit-union loan (debt conversion: no new cash) |
| `income_up_10` | Income | Exact | +10% pay from day 15 |
| `stress_income_-20` | Income | Exact | Stress test only; **never surfaced** as a recommendation |

**Ability-to-repay** is the lending check that a borrower can afford the payment. Here the monthly payment must fit in half of the monthly surplus (recurring income − recurring bills − typical spending).

**`ScenarioEngine.Run`** is Occlet's skeleton exactly:

```csharp
if (_b.ComputeHash() != _b.FrozenHash) throw …;             // the baseline must be untouched
var st = new ScenarioState(_b);                              // COPY of the events, rules and behaviour
foreach (var lever in levers) { lever.Apply(st); … Gate … }  // edit the copy; run gates such as ability-to-repay
var r = Replay.Run(… _b.Everyday, _b.AlertU, … st.Rules, st.Behaviour …);   // replay the SAME futures
SlackSummary.From(r, _b.Ads, st.ExtraDailySpend, st.Owed);  // TRUE slack with loan bookkeeping
```

The Phase 1 forecaster is never called again. `PairedGain` compares two scenarios future by future, which gives both the gain and the **confidence** (the share of futures in which it is positive).

**The borrowed-slack trap, in this run (Asha):**

| | Headline slack 90d | True slack 90d |
|---|---:|---:|
| Baseline | 18.3 days | 18.3 days |
| + $1,000 loan | **26.0 days** | **16.7 days** |

The loan looks like +7.7 days and is really −1.7. It also fails ability-to-repay, because Asha's typical spending already exceeds her income by about $178 a month. This is Occlet's 15 → 40 → 13.6 example, reproduced by the engine.

---

## 10. The decision rules and the waterfall: `Decisioning/Recommender.cs`

These are your four rules, implemented literally:

```csharp
if (baseline.Summary.LowestTrueSlack >= goal) return "Goal already met — no lever needed";
foreach (var tier in Order)                                       // Behaviour → Product → Liquidity → Income   (rule 2)
{
    if (current.Summary.LowestTrueSlack >= goal) break;           // stop as soon as the goal is met            (rule 1)
    foreach (bool consentStage in new[] { false, true })          // the member's own levers before consent-needed ones
        while (…goal not met…)
        {
            evaluate every remaining lever stacked on the current plan;
            accept the best one that passes Judge(…), add it to the plan, repeat;
        }
}
```

**`Judge`** encodes the safeguards, checked in this order:

1. A **gate** failed (ability-to-repay) → rejected.
2. The lever adds more than 0.25 **declined bills** per 90 days → rejected as "needs a member conversation, not auto-adoption". This is how `no_fee_account` is stopped from "fixing" fees by bouncing the rent.
3. **Behaviour** tier: it must add ≥ 0.1 day.
4. **Lower tiers (rule 3):** it must beat what the higher-priority levers already achieve by **≥ 1.0 day of true slack**, and be positive in **≥ 80% of futures**.

**`Plan.Waterfall()`** (rule 4) prints every step, in Occlet's deck format. The step sizes are the differences in the score, so they add up exactly to the total.

**Asha (this run):**

> 0.0 days baseline → +0.8 (move phone + insurance + subscription autopay to the day after payday) → +0.8 (trim shopping by 25%) → +1.8 (ask to move the rent due date to the day after payday) → **3.4 days**

The decision log shows why this order came out. In round 1, moving the bills was the only behaviour lever that added ≥ 0.1 day; trimming shopping added **0.0 on its own**. Once the bills had moved, trimming added +0.8. **Levers interact**, which is why the engine re-evaluates every remaining lever on top of the plan each round instead of adding up single-lever results. Moving rent needs the landlord, so it was tried only after the member's own levers, and it reached the 2-day goal.

**Member sentence:** "If you move your phone, insurance and subscription autopay to the day after payday, spend about 25% less on shopping and ask your landlord to move the rent due date to the day after payday (if your landlord agrees), your tightest day over the next 90 days goes from about 0.0 to 3.4 days of spending in hand, and expected overdraft fees drop from about $84 to $31."

**Eli:** every behaviour lever except rent adds under 0.1 day, so only `move_rent` is accepted (+0.9). Everything after it is stopped by a rule:

- `no_fee_account` would turn fees into about 6 declined bills.
- `cash_loan` fails ability-to-repay (surplus −$329 a month).
- `refinance_card` and `income_up_10` don't clear the 1-day margin.

The plan flags a **structural gap**: "your usual spending runs about $329 a month above your income … worth a budget conversation with a specialist." Moving dates cannot fix a deficit, and the engine says so instead of forcing a lever.

**Ben, Chen and Dee** already have ≥ 2 days on their tightest day, so rule 1 stops immediately: "no change needed".

---

## 11. Is the forecast honest? `Evaluation/Backtest.cs`

```csharp
var origin = end.AddDays(-H - k * stepDays);             // pretend "today" was 90 + 14k days ago (12 origins)
forecaster.Fit(universe.UpTo(origin), origin);           // only data up to that day
var b = BaselineBuilder.Build(m, origin, forecaster, …); // forecast …
double y = m.CheckingEod(origin.AddDays(d + 1));         // … and compare with what really happened
```

| Horizon | Real days inside the 80% band (gate 75–85%) | Real days below q10 (target ≈ 10%) | Scaled pinball loss |
|---:|---:|---:|---:|
| 30 days | 80% | 12.6% | 0.81 days |
| 60 days | 80% | 13.8% | 1.13 days |
| 90 days | 79% | 14.9% | 1.33 days |

*Pinball loss* is the standard score for quantile forecasts; it is scaled here by each member's daily spend, so it reads "days of spending". The bootstrap passes Occlet's coverage gate at every horizon, but its low end runs slightly optimistic (15% of real days below the 10th percentile at 90 days, at the edge of the 5–15% band). Chen's large cushion and Dee's short history mean the honesty test rests mostly on Asha, Ben and Eli.

---

## 12. Outputs (`output/approach1/`)

| File | What it holds |
|---|---|
| `REPORT.md` | Per-member summary, waterfall, member sentence, every lever's effect, why each rejected lever was rejected |
| `results.xlsx` | Sheets: `Summary`, `Baseline_Paths` (daily q10/q50/q90 + plan + known events), `Recurring_Detected`, `Levers_Alone`, `Plan_Waterfall`, `Decision_Log`, `Catalog_Notes`, `Backtest` |
| `charts/*_forecast.svg` | 90-day fan chart: baseline band, baseline likely line, recommended plan |
| `charts/*_waterfall.svg` | The waterfall, with the 2-day goal line |
| `artifacts/*.json` | The frozen, hashed baseline artifact for each member |

## Known limits of Approach 1

- It can only imagine spending days the member has already had. A first-ever $2,000 expense is impossible in its world (the "spending mood" softens this, but does not remove it).
- The learned levers use Occlet's illustrative response rates, not measured ones.
- `move_bills` / `move_rent` move each bill to "the day after the next payday". For biweekly pay that date drifts month to month, while a real biller needs one fixed day; a production version would choose the single best fixed day.
- The data is synthetic: the same simple rules generated it, which flatters every model. Real ledgers will be messier.
