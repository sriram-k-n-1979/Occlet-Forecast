# Approach 1 — rudimentary bootstrap forecaster + scenario engine

Forecast origin **2026-10-27**, horizon 90 days, 2,000 simulated futures per member, seed 7. All data is illustrative.

Balances are posted-balance proxies: holds and pending authorizations are not supplied. Ticket sizes are sampled from historical within-day proportions; they are a modelling assumption. Plans marked UNVALIDATED are hypothetical comparisons, not member advice.

Score used by the decision rules: **lowest TRUE slack** = on the tightest day of each simulated future, (balance − borrowed principal still owed) ÷ (average daily spend + loan repayments); the median across futures. Goal: ≥ 2.0 days.

| Member | Forecaster | Headline slack 90d | Lowest true slack | P(overdraft) | Exp. fees 90d | Waterfall |
|---|---|---:|---:|---:|---:|---|
| Asha | Bootstrap | 18.5 | 0.0 | 49% | $79 | 0.0 days baseline → +1.0 (Move phone + insurance + subscription autopay to the day after payday) → +0.7 (Trim shopping by 25% (21% of everyday spend)) → 1.7 days |
| Ben | Bootstrap | 30.6 | 11.3 | 4% | $2 | 11.3 days baseline → 11.3 days |
| Chen | Bootstrap | 62.4 | 56.6 | 0% | $0 | 56.6 days baseline → 56.6 days |
| Dee | Bootstrap | 31.9 | 14.8 | 6% | $5 | 14.8 days baseline → 14.8 days |
| Eli | Bootstrap | 2.4 | -8.7 | 100% | $260 | -8.7 days baseline → +1.0 (Ask to move the rent due date to the day after payday) → -7.7 days |

## Back-test (rolling origins, 2 weeks apart)

| Forecaster | Horizon | Endpoints scored | In 80% band (gate 75–85%) | Below q10 (target ≈10%) | Scaled pinball (days) | Lowest-balance below q10 |
|---|---:|---:|---:|---:|---:|---:|
| Bootstrap | 30d | 48 | 79.2% ✅ | 14.6% ✅ | 1.09 | 12.5% |
| Bootstrap | 60d | 48 | 77.1% ✅ | 18.8% ❌ | 1.62 | 16.7% |
| Bootstrap | 90d | 48 | 75.0% ✅ | 22.9% ❌ | 2.05 | 14.6% |

Endpoint metrics score only days 30, 60 and 90. The following window metrics average all days 1 through H and are separate diagnostics. Origins overlap; these are not independent samples.

| Forecaster | Horizon | Window coverage | Window below q10 | Window pinball | Minimum coverage | All gates |
|---|---:|---:|---:|---:|---:|---|
| Bootstrap | 30 | 80.6% | 12.3% | 0.75 | 70.8% | FAIL |
| Bootstrap | 60 | 80.7% | 13.4% | 1.04 | 70.8% | FAIL |
| Bootstrap | 90 | 79.9% | 14.8% | 1.24 | 72.9% | FAIL |

## Asha (A) — Bootstrap

*Occlet's Member A: paid on the 8th and 23rd; rent, phone and insurance fall before payday.*

- Start balance $1,922, savings $750, average daily spend $95.05.
- Headline slack 30/60/90 d: 19.6 / 19.1 / 18.5 days. Lowest true slack: **0.0** (bad case p10 -7.1). Risk days: 19. P(any overdraft): 49%. Expected fees: $79. Outstanding unpaid bills at horizon: $1.
- Explanation: Your balance is likely lowest on Jan 07 (about $30, 0.3 days of spending). What pulls it down: rent $1,100 on Jan 01, phone $90 on Jan 03, insurance $160 on Jan 05. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or validation was not run. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $178/month.
- **Waterfall:** 0.0 days baseline → +1.0 (Move phone + insurance + subscription autopay to the day after payday) → +0.7 (Trim shopping by 25% (21% of everyday spend)) → 1.7 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/A_bootstrap_forecast.svg)

![waterfall](charts/A_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + subscription autopay to the day after payday | Behaviour | Exact | 1.0 | +1.0 | $27 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 2.7 | +2.7 | $31 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | 0.0 | 0.0 | $76 | 0.0 |  |
| Trim shopping by 25% (21% of everyday spend) | Behaviour | Learned | 0.1 | +0.1 | $72 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 0.0 | 0.0 | $23 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.3 | +0.3 | $0 | 3.2 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -1.7 | −1.7 | $22 | 0.0 | FAIL candidate monthly surplus after payments $-173; payment $90 must fit 50% of pre-payment surplus $-83 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 1.0 | +1.0 | $46 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -8.3 | −8.3 | $254 | 0.0 |  |

Why the other levers were not added:

- `alerts` — adds only +0.00 days (< 0.1)
- `trim_shopping` — adds only +0.07 days (< 0.1)
- `move_rent` — increases outstanding unpaid obligations; specialist review required
- `savings_first` — beats the higher-priority plan by only -0.11 days (< 1.0-day margin)
- `no_fee_account` — increases outstanding unpaid obligations; specialist review required
- `cash_loan` — gate failed (cash_loan): candidate monthly surplus after payments $-173; payment $90 must fit 50% of pre-payment surplus $-83
- `income_up_10` — gain positive in only 67% of futures (< 80% confidence)

Catalogue notes: refinance: no outside credit-card debt on file.

## Ben (B) — Bootstrap

*Hourly worker paid every other Friday (hours vary). Low-balance alert already on.*

- Start balance $1,818, savings $0, average daily spend $82.16.
- Headline slack 30/60/90 d: 20.6 / 32.6 / 30.6 days. Lowest true slack: **11.3** (bad case p10 0.7). Risk days: 0. P(any overdraft): 4%. Expected fees: $2. Outstanding unpaid bills at horizon: $1.
- Explanation: Your balance is likely lowest on Jan 07 (about $981, 11.9 days of spending). What pulls it down: rent $1,050 on Jan 01, insurance $140 on Jan 02, subscription $18 on Jan 06. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or validation was not run. Goal already met at baseline (11.3 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 11.3 days baseline → 11.3 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/B_bootstrap_forecast.svg)

![waterfall](charts/B_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + internet + subscription autopay to the day after payday | Behaviour | Exact | 11.4 | +0.2 | $2 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 13.7 | +2.4 | $1 | 0.0 |  |
| Trim shopping by 25% (18% of everyday spend) | Behaviour | Learned | 11.7 | +0.4 | $2 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 11.3 | 0.0 | $0 | 0.1 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 10.7 | −0.5 | $0 | 0.0 | PASS candidate monthly surplus after payments $162; payment $90 must fit 50% of pre-payment surplus $252 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 13.3 | +2.1 | $1 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 0.0 | −11.2 | $38 | 0.0 |  |

Catalogue notes: alerts: already active — its effect is already in the member's history, so it is not offered again. savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Chen (C) — Bootstrap

*Salaried, paid on the last business day; comfortable cushion.*

- Start balance $8,956, savings $12,800, average daily spend $154.89.
- Headline slack 30/60/90 d: 59.5 / 60.9 / 62.4 days. Lowest true slack: **56.6** (bad case p10 51.9). Risk days: 0. P(any overdraft): 0%. Expected fees: $0. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Oct 29 (about $8,839, 57.1 days of spending). What pulls it down: everyday spending. Payday on Oct 30 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or validation was not run. Goal already met at baseline (56.6 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 56.6 days baseline → 56.6 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/C_bootstrap_forecast.svg)

![waterfall](charts/C_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Turn on low-balance alert at $150 | Behaviour | Learned | 56.6 | 0.0 | $0 | 0.0 |  |
| Trim shopping by 25% (22% of everyday spend) | Behaviour | Learned | 56.8 | +0.2 | $0 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 56.6 | 0.0 | $0 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 56.6 | 0.0 | $0 | 0.0 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 55.6 | −1.1 | $0 | 0.0 | PASS candidate monthly surplus after payments $569; payment $90 must fit 50% of pre-payment surplus $659 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 56.8 | +0.2 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 42.2 | −14.4 | $0 | 0.0 |  |

Catalogue notes: move_bills: no movable bill falls within 5 days before a payday. move_rent: housing payment already falls within 2 days after a payday. refinance: no outside credit-card debt on file.

## Dee (D) — Bootstrap

*New member since June 2026 — about four months of history (thin file).*

- Start balance $1,259, savings $0, average daily spend $61.27.
- Headline slack 30/60/90 d: 24.5 / 28.4 / 31.9 days. Lowest true slack: **14.8** (bad case p10 2.6). Risk days: 0. P(any overdraft): 6%. Expected fees: $5. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Nov 12 (about $932, 15.2 days of spending). What pulls it down: insurance $95 on Nov 12. Payday on Nov 13 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or validation was not run. Goal already met at baseline (14.8 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 14.8 days baseline → 14.8 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/D_bootstrap_forecast.svg)

![waterfall](charts/D_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move insurance + subscription + phone autopay to the day after payday | Behaviour | Exact | 16.5 | +1.7 | $0 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 19.8 | +5.0 | $0 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | 14.8 | 0.0 | $5 | 0.0 |  |
| Trim shopping by 25% (25% of everyday spend) | Behaviour | Learned | 15.1 | +0.3 | $4 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 14.8 | 0.0 | $0 | 0.3 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 14.0 | −0.7 | $0 | 0.0 | PASS candidate monthly surplus after payments $145; payment $90 must fit 50% of pre-payment surplus $234 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 15.1 | +0.3 | $2 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 3.1 | −11.7 | $37 | 0.0 |  |

Catalogue notes: savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Eli (E) — Bootstrap

*Gig driver paid weekly (Tuesdays) with ±30% swings; $4,200 on an outside card at 27.99% APR.*

- Start balance $502, savings $0, average daily spend $78.10.
- Headline slack 30/60/90 d: 6.2 / 5.0 / 2.4 days. Lowest true slack: **-8.7** (bad case p10 -10.5). Risk days: 48. P(any overdraft): 100%. Expected fees: $260. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Nov 01 (about $-669, -8.6 days of spending). What pulls it down: rent $980 on Nov 01. Payday on Nov 03 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or validation was not run. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $329/month.
- **Waterfall:** -8.7 days baseline → +1.0 (Ask to move the rent due date to the day after payday) → -7.7 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/E_bootstrap_forecast.svg)

![waterfall](charts/E_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + utilities autopay to the day after payday | Behaviour | Exact | -8.7 | 0.0 | $156 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | -7.7 | +1.0 | $262 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | -8.7 | 0.0 | $247 | 0.0 |  |
| Trim shopping by 25% (10% of everyday spend) | Behaviour | Learned | -8.7 | 0.0 | $257 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.2 | +8.8 | $0 | 6.2 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -13.7 | −5.0 | $115 | 0.0 | FAIL candidate monthly surplus after payments $-572; payment $90 must fit 50% of pre-payment surplus $-482 |
| Refinance $4,200 card debt (27.99% APR) into a 36-mo CU loan @ 11.99%: $168 → $139/mo | Liquidity | Exact | -8.8 | −0.1 | $248 | 0.0 | FAIL candidate monthly surplus after payments $-482; payment $139 must fit 50% of pre-payment surplus $-343 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | -8.6 | +0.1 | $198 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -9.5 | −0.8 | $424 | 0.0 |  |

Why the other levers were not added:

- `move_bills` — adds only +0.03 days (< 0.1)
- `alerts` — adds only +0.03 days (< 0.1)
- `trim_shopping` — adds only +0.03 days (< 0.1)
- `no_fee_account` — increases outstanding unpaid obligations; specialist review required
- `cash_loan` — gate failed (cash_loan): candidate monthly surplus after payments $-572; payment $90 must fit 50% of pre-payment surplus $-482
- `refinance_card` — gate failed (refinance_card): candidate monthly surplus after payments $-482; payment $139 must fit 50% of pre-payment surplus $-343
- `income_up_10` — beats the higher-priority plan by only +0.27 days (< 1.0-day margin)

Catalogue notes: savings_first: no savings balance to draw on.

