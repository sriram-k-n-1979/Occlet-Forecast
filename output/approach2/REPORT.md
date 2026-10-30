# Approach 2 — LightGBM and state-space forecasters + scenario engine

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
| Asha | LightGBM | 18.5 | -1.7 | 56% | $93 | -1.7 days baseline → +1.8 (Move phone + insurance + subscription autopay to the day after payday) → 0.1 days |
| Ben | LightGBM | 28.9 | 9.7 | 12% | $7 | 9.7 days baseline → 9.7 days |
| Chen | LightGBM | 63.2 | 56.4 | 0% | $0 | 56.4 days baseline → 56.4 days |
| Dee | LightGBM | 38.0 | 15.6 | 4% | $3 | 15.6 days baseline → 15.6 days |
| Eli | LightGBM | 0.5 | -9.0 | 100% | $306 | -9.0 days baseline → +0.5 (Ask to move the rent due date to the day after payday) → -8.6 days |
| Asha | StateSpace | 17.2 | -1.5 | 61% | $80 | -1.5 days baseline → +1.7 (Move phone + insurance + subscription autopay to the day after payday) → +0.1 (Trim shopping by 25% (21% of everyday spend)) → 0.3 days |
| Ben | StateSpace | 27.3 | 8.9 | 3% | $2 | 8.9 days baseline → 8.9 days |
| Chen | StateSpace | 63.1 | 56.7 | 0% | $0 | 56.7 days baseline → 56.7 days |
| Dee | StateSpace | 30.5 | 14.3 | 0% | $0 | 14.3 days baseline → 14.3 days |
| Eli | StateSpace | 2.2 | -8.6 | 100% | $265 | -8.6 days baseline → +0.9 (Ask to move the rent due date to the day after payday) → -7.8 days |

## Back-test (rolling origins, 2 weeks apart)

| Forecaster | Horizon | Endpoints scored | In 80% band (gate 75–85%) | Below q10 (target ≈10%) | Scaled pinball (days) | Lowest-balance below q10 |
|---|---:|---:|---:|---:|---:|---:|
| Bootstrap | 30d | 48 | 79.2% ✅ | 14.6% ✅ | 1.09 | 12.5% |
| Bootstrap | 60d | 48 | 77.1% ✅ | 18.8% ❌ | 1.62 | 16.7% |
| Bootstrap | 90d | 48 | 75.0% ✅ | 22.9% ❌ | 2.05 | 14.6% |
| LightGBM | 30d | 48 | 77.1% ✅ | 14.6% ✅ | 1.19 | 12.5% |
| LightGBM | 60d | 48 | 70.8% ❌ | 22.9% ❌ | 2.15 | 18.8% |
| LightGBM | 90d | 48 | 75.0% ✅ | 20.8% ❌ | 3.04 | 18.8% |
| LightGBM-raw | 30d | 48 | 41.7% ❌ | 33.3% ❌ | 1.26 | 18.8% |
| LightGBM-raw | 60d | 48 | 54.2% ❌ | 31.2% ❌ | 1.98 | 31.2% |
| LightGBM-raw | 90d | 48 | 58.3% ❌ | 27.1% ❌ | 2.61 | 29.2% |
| StateSpace | 30d | 48 | 77.1% ✅ | 14.6% ✅ | 1.07 | 10.4% |
| StateSpace | 60d | 48 | 75.0% ✅ | 20.8% ❌ | 1.54 | 16.7% |
| StateSpace | 90d | 48 | 79.2% ✅ | 18.8% ❌ | 1.96 | 20.8% |

Endpoint metrics score only days 30, 60 and 90. The following window metrics average all days 1 through H and are separate diagnostics. Origins overlap; these are not independent samples.

| Forecaster | Horizon | Window coverage | Window below q10 | Window pinball | Minimum coverage | All gates |
|---|---:|---:|---:|---:|---:|---|
| Bootstrap | 30 | 80.6% | 12.3% | 0.75 | 70.8% | FAIL |
| Bootstrap | 60 | 80.7% | 13.4% | 1.04 | 70.8% | FAIL |
| Bootstrap | 90 | 79.9% | 14.8% | 1.24 | 72.9% | FAIL |
| LightGBM | 30 | 79.9% | 12.4% | 0.79 | 79.2% | PASS |
| LightGBM | 60 | 75.7% | 16.6% | 1.21 | 70.8% | FAIL |
| LightGBM | 90 | 75.0% | 18.4% | 1.61 | 70.8% | FAIL |
| LightGBM-raw | 30 | 52.8% | 22.6% | 0.86 | 52.1% | FAIL |
| LightGBM-raw | 60 | 52.6% | 25.9% | 1.21 | 47.9% | FAIL |
| LightGBM-raw | 90 | 53.4% | 27.4% | 1.51 | 50.0% | FAIL |
| StateSpace | 30 | 81.6% | 11.7% | 0.73 | 83.3% | PASS |
| StateSpace | 60 | 79.7% | 13.8% | 1.00 | 72.9% | FAIL |
| StateSpace | 90 | 78.8% | 15.5% | 1.17 | 66.7% | FAIL |

## Champion selection

| Forecaster | Avg scaled pinball (days) | Low-end gate at 30/60/90d | Endpoint coverage passes | Minimum gates pass | Champion |
|---|---:|---|---:|---:|---|
| Bootstrap | 1.58 | FAIL | 3/3 | 0/3 |  |
| LightGBM | 2.12 | FAIL | 2/3 | 1/3 |  |
| StateSpace | 1.52 | FAIL | 3/3 | 1/3 |  |

**NONE** — No model passes endpoint coverage, lower-tail and path-minimum gates at every horizon. Abstain; results are experimental.

## Do the models agree on the recommendation?

| Member | Bootstrap | LightGBM | StateSpace | Agree |
|---|---|---|---|---|
| Asha | move_bills + trim_shopping | move_bills | move_bills + trim_shopping | NO |
| Ben | (no change) | (no change) | (no change) | YES |
| Chen | (no change) | (no change) | (no change) | YES |
| Dee | (no change) | (no change) | (no change) | YES |
| Eli | move_rent | move_rent | move_rent | YES |

## LightGBM diagnostics (final fit)

- 8,670 training rows, 6,390 calibration rows, 5 members (one global model per quantile).
- Calibration rows inside the raw q10–q90 band: 48% → after conformal adjustment 80%. Spend above q90 (spending diagnostic, not replayed balance calibration): 30% → 10%.
- Copula: observed correlation of 7-day vs 90-day surprises 0.40 → persistent share λ = 0.17.
- Top features by gain (q50 model): `log_spend_scale` 21%, `tenure_days` 19%, `spend_cv30` 13%, `income_cv` 12%, `cushion_days` 6%, `spend30_over_spend90` 6%.

## Asha (A) — Bootstrap

*Occlet's Member A: paid on the 8th and 23rd; rent, phone and insurance fall before payday.*

- Start balance $1,922, savings $750, average daily spend $95.05.
- Headline slack 30/60/90 d: 19.6 / 19.1 / 18.5 days. Lowest true slack: **0.0** (bad case p10 -7.1). Risk days: 19. P(any overdraft): 49%. Expected fees: $79. Outstanding unpaid bills at horizon: $1.
- Explanation: Your balance is likely lowest on Jan 07 (about $30, 0.3 days of spending). What pulls it down: rent $1,100 on Jan 01, phone $90 on Jan 03, insurance $160 on Jan 05. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $178/month.
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
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (11.3 ≥ 2.0 days) — no lever needed.
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
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (56.6 ≥ 2.0 days) — no lever needed.
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
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (14.8 ≥ 2.0 days) — no lever needed.
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
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $329/month.
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

## Asha (A) — LightGBM

*Occlet's Member A: paid on the 8th and 23rd; rent, phone and insurance fall before payday.*

- Start balance $1,922, savings $750, average daily spend $95.05.
- Headline slack 30/60/90 d: 19.5 / 19.1 / 18.5 days. Lowest true slack: **-1.7** (bad case p10 -8.7). Risk days: 19. P(any overdraft): 56%. Expected fees: $93. Outstanding unpaid bills at horizon: $1.
- Explanation: Your balance is likely lowest on Jan 07 (about $13, 0.1 days of spending). What pulls it down: rent $1,100 on Jan 01, phone $90 on Jan 03, insurance $160 on Jan 05. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $244/month.
- **Waterfall:** -1.7 days baseline → +1.8 (Move phone + insurance + subscription autopay to the day after payday) → 0.1 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/A_lightgbm_forecast.svg)

![waterfall](charts/A_lightgbm_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + subscription autopay to the day after payday | Behaviour | Exact | 0.1 | +1.8 | $38 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 0.4 | +2.1 | $44 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | -1.5 | +0.1 | $90 | 0.0 |  |
| Trim shopping by 25% (21% of everyday spend) | Behaviour | Learned | -1.3 | +0.4 | $87 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 0.0 | +1.7 | $31 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.2 | +1.9 | $0 | 3.3 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -3.4 | −1.7 | $29 | 0.0 | FAIL candidate monthly surplus after payments $-239; payment $90 must fit 50% of pre-payment surplus $-149 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 0.1 | +1.8 | $59 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -8.9 | −7.2 | $253 | 0.0 |  |

Why the other levers were not added:

- `alerts` — adds only +0.01 days (< 0.1)
- `trim_shopping` — adds only +0.10 days (< 0.1)
- `move_rent` — increases outstanding unpaid obligations; specialist review required
- `savings_first` — beats the higher-priority plan by only -0.08 days (< 1.0-day margin)
- `no_fee_account` — increases outstanding unpaid obligations; specialist review required
- `cash_loan` — gate failed (cash_loan): candidate monthly surplus after payments $-239; payment $90 must fit 50% of pre-payment surplus $-149
- `income_up_10` — gain positive in only 74% of futures (< 80% confidence)

Catalogue notes: refinance: no outside credit-card debt on file.

## Ben (B) — LightGBM

*Hourly worker paid every other Friday (hours vary). Low-balance alert already on.*

- Start balance $1,818, savings $0, average daily spend $82.16.
- Headline slack 30/60/90 d: 20.2 / 31.9 / 28.9 days. Lowest true slack: **9.7** (bad case p10 -1.5). Risk days: 6. P(any overdraft): 12%. Expected fees: $7. Outstanding unpaid bills at horizon: $3.
- Explanation: Your balance is likely lowest on Jan 07 (about $901, 11.0 days of spending). What pulls it down: rent $1,050 on Jan 01, insurance $140 on Jan 02, subscription $18 on Jan 06. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (9.7 ≥ 2.0 days) — no lever needed. Structural gap: typical spending exceeds income by $83/month.
- **Waterfall:** 9.7 days baseline → 9.7 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/B_lightgbm_forecast.svg)

![waterfall](charts/B_lightgbm_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + internet + subscription autopay to the day after payday | Behaviour | Exact | 9.8 | +0.2 | $7 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 12.6 | +3.0 | $3 | 0.0 |  |
| Trim shopping by 25% (18% of everyday spend) | Behaviour | Learned | 10.1 | +0.4 | $6 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 9.7 | 0.0 | $0 | 0.4 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 9.2 | −0.4 | $0 | 0.0 | FAIL candidate monthly surplus after payments $61; payment $90 must fit 50% of pre-payment surplus $151 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 12.3 | +2.6 | $2 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 0.0 | −9.6 | $48 | 0.0 |  |

Catalogue notes: alerts: already active — its effect is already in the member's history, so it is not offered again. savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Chen (C) — LightGBM

*Salaried, paid on the last business day; comfortable cushion.*

- Start balance $8,956, savings $12,800, average daily spend $154.89.
- Headline slack 30/60/90 d: 59.7 / 61.6 / 63.2 days. Lowest true slack: **56.4** (bad case p10 45.8). Risk days: 0. P(any overdraft): 0%. Expected fees: $0. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Oct 29 (about $8,834, 57.0 days of spending). What pulls it down: everyday spending. Payday on Oct 30 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (56.4 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 56.4 days baseline → 56.4 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/C_lightgbm_forecast.svg)

![waterfall](charts/C_lightgbm_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Turn on low-balance alert at $150 | Behaviour | Learned | 56.4 | 0.0 | $0 | 0.0 |  |
| Trim shopping by 25% (22% of everyday spend) | Behaviour | Learned | 56.6 | +0.2 | $0 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 56.4 | 0.0 | $0 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 56.4 | 0.0 | $0 | 0.0 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 55.3 | −1.1 | $0 | 0.0 | PASS candidate monthly surplus after payments $496; payment $90 must fit 50% of pre-payment surplus $585 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 56.7 | +0.3 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 42.6 | −13.7 | $0 | 0.0 |  |

Catalogue notes: move_bills: no movable bill falls within 5 days before a payday. move_rent: housing payment already falls within 2 days after a payday. refinance: no outside credit-card debt on file.

## Dee (D) — LightGBM

*New member since June 2026 — about four months of history (thin file).*

- Start balance $1,259, savings $0, average daily spend $61.27.
- Headline slack 30/60/90 d: 27.3 / 32.8 / 38.0 days. Lowest true slack: **15.6** (bad case p10 3.6). Risk days: 0. P(any overdraft): 4%. Expected fees: $3. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Nov 12 (about $1,020, 16.6 days of spending). What pulls it down: insurance $95 on Nov 12. Payday on Nov 13 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (15.6 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 15.6 days baseline → 15.6 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/D_lightgbm_forecast.svg)

![waterfall](charts/D_lightgbm_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move insurance + subscription + phone autopay to the day after payday | Behaviour | Exact | 17.3 | +1.7 | $0 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 19.7 | +4.1 | $0 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | 15.6 | 0.0 | $3 | 0.0 |  |
| Trim shopping by 25% (25% of everyday spend) | Behaviour | Learned | 16.0 | +0.4 | $2 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 15.6 | 0.0 | $0 | 0.2 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 14.9 | −0.8 | $0 | 0.0 | PASS candidate monthly surplus after payments $185; payment $90 must fit 50% of pre-payment surplus $275 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 16.3 | +0.7 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 6.3 | −9.3 | $33 | 0.0 |  |

Catalogue notes: savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Eli (E) — LightGBM

*Gig driver paid weekly (Tuesdays) with ±30% swings; $4,200 on an outside card at 27.99% APR.*

- Start balance $502, savings $0, average daily spend $78.10.
- Headline slack 30/60/90 d: 5.2 / 3.3 / 0.5 days. Lowest true slack: **-9.0** (bad case p10 -10.9). Risk days: 58. P(any overdraft): 100%. Expected fees: $306. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Nov 01 (about $-691, -8.8 days of spending). What pulls it down: rent $980 on Nov 01. Payday on Nov 03 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $571/month.
- **Waterfall:** -9.0 days baseline → +0.5 (Ask to move the rent due date to the day after payday) → -8.6 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/E_lightgbm_forecast.svg)

![waterfall](charts/E_lightgbm_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + utilities autopay to the day after payday | Behaviour | Exact | -9.0 | 0.0 | $201 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | -8.6 | +0.5 | $306 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | -9.0 | 0.0 | $292 | 0.0 |  |
| Trim shopping by 25% (10% of everyday spend) | Behaviour | Learned | -9.0 | 0.0 | $303 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.1 | +9.1 | $0 | 7.9 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -16.4 | −7.4 | $191 | 0.0 | FAIL candidate monthly surplus after payments $-813; payment $90 must fit 50% of pre-payment surplus $-724 |
| Refinance $4,200 card debt (27.99% APR) into a 36-mo CU loan @ 11.99%: $168 → $139/mo | Liquidity | Exact | -9.1 | −0.1 | $295 | 0.0 | FAIL candidate monthly surplus after payments $-724; payment $139 must fit 50% of pre-payment surplus $-584 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | -8.9 | +0.1 | $242 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -10.0 | −1.0 | $446 | 0.0 |  |

Why the other levers were not added:

- `move_bills` — adds only +0.05 days (< 0.1)
- `alerts` — adds only +0.04 days (< 0.1)
- `trim_shopping` — adds only +0.02 days (< 0.1)
- `no_fee_account` — increases outstanding unpaid obligations; specialist review required
- `cash_loan` — gate failed (cash_loan): candidate monthly surplus after payments $-813; payment $90 must fit 50% of pre-payment surplus $-724
- `refinance_card` — gate failed (refinance_card): candidate monthly surplus after payments $-724; payment $139 must fit 50% of pre-payment surplus $-584
- `income_up_10` — beats the higher-priority plan by only +0.37 days (< 1.0-day margin)

Catalogue notes: savings_first: no savings balance to draw on.

## Asha (A) — StateSpace

*Occlet's Member A: paid on the 8th and 23rd; rent, phone and insurance fall before payday.*

- Start balance $1,922, savings $750, average daily spend $95.05.
- Headline slack 30/60/90 d: 19.1 / 18.1 / 17.2 days. Lowest true slack: **-1.5** (bad case p10 -6.5). Risk days: 20. P(any overdraft): 61%. Expected fees: $80. Outstanding unpaid bills at horizon: $1.
- Explanation: Your balance is likely lowest on Jan 05 (about $-47, -0.5 days of spending). What pulls it down: rent $1,100 on Jan 01, phone $90 on Jan 03, insurance $160 on Jan 05. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $235/month.
- **Waterfall:** -1.5 days baseline → +1.7 (Move phone + insurance + subscription autopay to the day after payday) → +0.1 (Trim shopping by 25% (21% of everyday spend)) → 0.3 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/A_statespace_forecast.svg)

![waterfall](charts/A_statespace_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + subscription autopay to the day after payday | Behaviour | Exact | 0.2 | +1.7 | $24 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 0.7 | +2.2 | $29 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | -1.4 | +0.1 | $76 | 0.0 |  |
| Trim shopping by 25% (21% of everyday spend) | Behaviour | Learned | -1.2 | +0.4 | $72 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 0.0 | +1.5 | $17 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.2 | +1.7 | $0 | 3.4 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -4.0 | −2.4 | $15 | 0.0 | FAIL candidate monthly surplus after payments $-230; payment $90 must fit 50% of pre-payment surplus $-140 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 0.2 | +1.7 | $42 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -8.8 | −7.2 | $275 | 0.0 |  |

Why the other levers were not added:

- `alerts` — adds only +0.10 days (< 0.1)
- `trim_shopping` — behaviour lever adds +0.38 days
- `move_rent` — increases outstanding unpaid obligations; specialist review required
- `savings_first` — beats the higher-priority plan by only -0.31 days (< 1.0-day margin)
- `no_fee_account` — increases outstanding unpaid obligations; specialist review required
- `cash_loan` — gate failed (cash_loan): candidate monthly surplus after payments $-230; payment $90 must fit 50% of pre-payment surplus $-140
- `income_up_10` — gain positive in only 74% of futures (< 80% confidence)

Catalogue notes: refinance: no outside credit-card debt on file.

## Ben (B) — StateSpace

*Hourly worker paid every other Friday (hours vary). Low-balance alert already on.*

- Start balance $1,818, savings $0, average daily spend $82.16.
- Headline slack 30/60/90 d: 19.5 / 30.5 / 27.3 days. Lowest true slack: **8.9** (bad case p10 0.6). Risk days: 0. P(any overdraft): 3%. Expected fees: $2. Outstanding unpaid bills at horizon: $1.
- Explanation: Your balance is likely lowest on Jan 07 (about $784, 9.5 days of spending). What pulls it down: rent $1,050 on Jan 01, insurance $140 on Jan 02, subscription $18 on Jan 06. Payday on Jan 08 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (8.9 ≥ 2.0 days) — no lever needed. Structural gap: typical spending exceeds income by $73/month.
- **Waterfall:** 8.9 days baseline → 8.9 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/B_statespace_forecast.svg)

![waterfall](charts/B_statespace_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + internet + subscription autopay to the day after payday | Behaviour | Exact | 9.1 | +0.2 | $2 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 12.7 | +3.8 | $0 | 0.0 |  |
| Trim shopping by 25% (18% of everyday spend) | Behaviour | Learned | 9.6 | +0.7 | $1 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 8.9 | 0.0 | $0 | 0.1 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 8.5 | −0.4 | $0 | 0.0 | FAIL candidate monthly surplus after payments $71; payment $90 must fit 50% of pre-payment surplus $161 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 12.3 | +3.4 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -1.6 | −10.5 | $46 | 0.0 |  |

Catalogue notes: alerts: already active — its effect is already in the member's history, so it is not offered again. savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Chen (C) — StateSpace

*Salaried, paid on the last business day; comfortable cushion.*

- Start balance $8,956, savings $12,800, average daily spend $154.89.
- Headline slack 30/60/90 d: 59.8 / 61.3 / 63.1 days. Lowest true slack: **56.7** (bad case p10 51.9). Risk days: 0. P(any overdraft): 0%. Expected fees: $0. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Oct 29 (about $8,854, 57.2 days of spending). What pulls it down: everyday spending. Payday on Oct 30 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (56.7 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 56.7 days baseline → 56.7 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/C_statespace_forecast.svg)

![waterfall](charts/C_statespace_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Turn on low-balance alert at $150 | Behaviour | Learned | 56.7 | 0.0 | $0 | 0.0 |  |
| Trim shopping by 25% (22% of everyday spend) | Behaviour | Learned | 56.9 | +0.2 | $0 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 56.7 | 0.0 | $0 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 56.7 | 0.0 | $0 | 0.0 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 55.6 | −1.1 | $0 | 0.0 | PASS candidate monthly surplus after payments $607; payment $90 must fit 50% of pre-payment surplus $697 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 56.8 | +0.2 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 42.9 | −13.8 | $0 | 0.0 |  |

Catalogue notes: move_bills: no movable bill falls within 5 days before a payday. move_rent: housing payment already falls within 2 days after a payday. refinance: no outside credit-card debt on file.

## Dee (D) — StateSpace

*New member since June 2026 — about four months of history (thin file).*

- Start balance $1,259, savings $0, average daily spend $61.27.
- Headline slack 30/60/90 d: 24.3 / 27.3 / 30.5 days. Lowest true slack: **14.3** (bad case p10 8.7). Risk days: 0. P(any overdraft): 0%. Expected fees: $0. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Nov 12 (about $930, 15.2 days of spending). What pulls it down: insurance $95 on Nov 12. Payday on Nov 13 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal already met at baseline (14.3 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 14.3 days baseline → 14.3 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/D_statespace_forecast.svg)

![waterfall](charts/D_statespace_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move insurance + subscription + phone autopay to the day after payday | Behaviour | Exact | 16.0 | +1.7 | $0 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 19.7 | +5.4 | $0 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | 14.3 | 0.0 | $0 | 0.0 |  |
| Trim shopping by 25% (25% of everyday spend) | Behaviour | Learned | 14.6 | +0.4 | $0 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 14.3 | 0.0 | $0 | 0.0 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 13.5 | −0.8 | $0 | 0.0 | PASS candidate monthly surplus after payments $136; payment $90 must fit 50% of pre-payment surplus $226 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 15.0 | +0.7 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 1.7 | −12.6 | $29 | 0.0 |  |

Catalogue notes: savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Eli (E) — StateSpace

*Gig driver paid weekly (Tuesdays) with ±30% swings; $4,200 on an outside card at 27.99% APR.*

- Start balance $502, savings $0, average daily spend $78.10.
- Headline slack 30/60/90 d: 6.2 / 4.6 / 2.2 days. Lowest true slack: **-8.6** (bad case p10 -11.1). Risk days: 48. P(any overdraft): 100%. Expected fees: $265. Outstanding unpaid bills at horizon: $0.
- Explanation: Your balance is likely lowest on Nov 01 (about $-661, -8.5 days of spending). What pulls it down: rent $980 on Nov 01. Payday on Nov 03 brings it back.
- **Decision:** UNVALIDATED: Model or member validation gates failed, or insufficient backtest history. Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $390/month.
- **Waterfall:** -8.6 days baseline → +0.9 (Ask to move the rent due date to the day after payday) → -7.8 days
- **Member sentence:** “No validated recommendation is available. A specialist must review the forecast; scenario results are experimental only.”

![forecast](charts/E_statespace_forecast.svg)

![waterfall](charts/E_statespace_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + utilities autopay to the day after payday | Behaviour | Exact | -8.6 | 0.0 | $165 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | -7.8 | +0.9 | $268 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | -8.6 | 0.0 | $253 | 0.0 |  |
| Trim shopping by 25% (10% of everyday spend) | Behaviour | Learned | -8.6 | 0.0 | $263 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.1 | +8.8 | $0 | 6.6 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -14.3 | −5.7 | $130 | 0.0 | FAIL candidate monthly surplus after payments $-633; payment $90 must fit 50% of pre-payment surplus $-543 |
| Refinance $4,200 card debt (27.99% APR) into a 36-mo CU loan @ 11.99%: $168 → $139/mo | Liquidity | Exact | -8.7 | −0.1 | $254 | 0.0 | FAIL candidate monthly surplus after payments $-543; payment $139 must fit 50% of pre-payment surplus $-403 |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | -8.6 | +0.1 | $205 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -9.6 | −1.0 | $427 | 0.0 |  |

Why the other levers were not added:

- `move_bills` — adds only +0.04 days (< 0.1)
- `alerts` — adds only +0.03 days (< 0.1)
- `trim_shopping` — adds only +0.03 days (< 0.1)
- `no_fee_account` — increases outstanding unpaid obligations; specialist review required
- `cash_loan` — gate failed (cash_loan): candidate monthly surplus after payments $-633; payment $90 must fit 50% of pre-payment surplus $-543
- `refinance_card` — gate failed (refinance_card): candidate monthly surplus after payments $-543; payment $139 must fit 50% of pre-payment surplus $-403
- `income_up_10` — beats the higher-priority plan by only +0.29 days (< 1.0-day margin)

Catalogue notes: savings_first: no savings balance to draw on.

