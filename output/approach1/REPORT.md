# Approach 1 — rudimentary bootstrap forecaster + scenario engine

Forecast origin **2026-10-27**, horizon 90 days, 2,000 simulated futures per member, seed 7. All data is illustrative.

Score used by the decision rules: **lowest TRUE slack** = on the tightest day of each simulated future, (balance − borrowed principal still owed) ÷ (average daily spend + loan repayments); the median across futures. Goal: ≥ 2.0 days.

| Member | Forecaster | Headline slack 90d | Lowest true slack | P(overdraft) | Exp. fees 90d | Waterfall |
|---|---|---:|---:|---:|---:|---|
| Asha | Bootstrap | 18.3 | 0.0 | 50% | $84 | 0.0 days baseline → +0.8 (Move phone + insurance + subscription autopay to the day after payday) → +0.8 (Trim shopping by 25% (21% of everyday spend)) → +1.8 (Ask to move the rent due date to the day after payday) → 3.4 days |
| Ben | Bootstrap | 30.6 | 11.3 | 7% | $5 | 11.3 days baseline → 11.3 days |
| Chen | Bootstrap | 62.4 | 56.7 | 0% | $0 | 56.7 days baseline → 56.7 days |
| Dee | Bootstrap | 31.9 | 14.8 | 6% | $5 | 14.8 days baseline → 14.8 days |
| Eli | Bootstrap | 2.0 | -8.7 | 100% | $268 | -8.7 days baseline → +0.9 (Ask to move the rent due date to the day after payday) → -7.8 days |

## Back-test (rolling origins, 2 weeks apart)

| Forecaster | Horizon | Days scored | In 80% band (gate 75–85%) | Below q10 (target ≈10%) | Scaled pinball (days) | Lowest-balance below q10 |
|---|---:|---:|---:|---:|---:|---:|
| Bootstrap | 30d | 1,440 | 80% ✅ | 13% ✅ | 0.81 | 12% |
| Bootstrap | 60d | 2,880 | 80% ✅ | 14% ✅ | 1.13 | 17% |
| Bootstrap | 90d | 4,320 | 79% ✅ | 15% ✅ | 1.33 | 15% |

## Asha (A) — Bootstrap

*Occlet's Member A: paid on the 8th and 23rd; rent, phone and insurance fall before payday.*

- Start balance $1,922, savings $750, average daily spend $95.05.
- Headline slack 30/60/90 d: 19.6 / 18.9 / 18.3 days. Lowest true slack: **0.0** (bad case p10 -7.4). Risk days: 19. P(any overdraft): 50%. Expected fees: $84.
- Explanation: Your balance is likely lowest on Jan 07 (about $0, 0.0 days of spending). What pulls it down: rent $1,100 on Jan 01, phone $90 on Jan 03, insurance $160 on Jan 05. Payday on Jan 08 brings it back.
- **Decision:** Goal met in tier 'Behaviour' after 3 lever(s). Structural gap: typical spending exceeds income by $178/month.
- **Waterfall:** 0.0 days baseline → +0.8 (Move phone + insurance + subscription autopay to the day after payday) → +0.8 (Trim shopping by 25% (21% of everyday spend)) → +1.8 (Ask to move the rent due date to the day after payday) → 3.4 days
- **Member sentence:** “If you move your phone, insurance and subscription autopay to the day after payday, spend about 25% less on shopping and ask your landlord to move the rent due date to the day after payday (if your landlord agrees), your tightest day over the next 90 days goes from about 0.0 to 3.4 days of spending in hand, and expected overdraft fees drop from about $84 to $31.”

![forecast](charts/A_bootstrap_forecast.svg)

![waterfall](charts/A_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + subscription autopay to the day after payday | Behaviour | Exact | 0.8 | +0.8 | $30 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 2.7 | +2.7 | $37 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | 0.0 | 0.0 | $82 | 0.0 |  |
| Trim shopping by 25% (21% of everyday spend) | Behaviour | Learned | 0.0 | 0.0 | $77 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 0.0 | 0.0 | $28 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.0 | 0.0 | $0 | 3.3 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -1.7 | −1.7 | $24 | 0.0 | FAIL payment $90/mo vs half of monthly surplus $0 (surplus $-178) |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 0.8 | +0.8 | $50 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -8.5 | −8.5 | $287 | 0.0 |  |

Why the other levers were not added:

- `alerts` — adds only +0.00 days (< 0.1)
- `trim_shopping` — adds only +0.00 days (< 0.1)

Catalogue notes: refinance: no outside credit-card debt on file.

## Ben (B) — Bootstrap

*Hourly worker paid every other Friday (hours vary). Low-balance alert already on.*

- Start balance $1,818, savings $0, average daily spend $82.16.
- Headline slack 30/60/90 d: 20.6 / 32.6 / 30.6 days. Lowest true slack: **11.3** (bad case p10 0.4). Risk days: 0. P(any overdraft): 7%. Expected fees: $5.
- Explanation: Your balance is likely lowest on Jan 07 (about $981, 11.9 days of spending). What pulls it down: rent $1,050 on Jan 01, insurance $140 on Jan 02, subscription $18 on Jan 06. Payday on Jan 08 brings it back.
- **Decision:** Goal already met at baseline (11.3 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 11.3 days baseline → 11.3 days
- **Member sentence:** “You keep about 11.3 days of spending in hand even on your tightest day over the next 90 days — no change needed.”

![forecast](charts/B_bootstrap_forecast.svg)

![waterfall](charts/B_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + internet + subscription autopay to the day after payday | Behaviour | Exact | 11.4 | +0.2 | $2 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 13.7 | +2.4 | $1 | 0.0 |  |
| Trim shopping by 25% (18% of everyday spend) | Behaviour | Learned | 11.7 | +0.4 | $5 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 11.3 | 0.0 | $0 | 0.3 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 10.7 | −0.5 | $1 | 0.0 | FAIL payment $90/mo vs half of monthly surplus $9 (surplus $18) |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 13.3 | +2.1 | $1 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -0.6 | −11.9 | $64 | 0.0 |  |

Catalogue notes: alerts: already active — its effect is already in the member's history, so it is not offered again. savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Chen (C) — Bootstrap

*Salaried, paid on the last business day; comfortable cushion.*

- Start balance $8,956, savings $12,800, average daily spend $154.89.
- Headline slack 30/60/90 d: 59.5 / 60.9 / 62.4 days. Lowest true slack: **56.7** (bad case p10 52.1). Risk days: 0. P(any overdraft): 0%. Expected fees: $0.
- Explanation: Your balance is likely lowest on Oct 29 (about $8,839, 57.1 days of spending). What pulls it down: everyday spending. Payday on Oct 30 brings it back.
- **Decision:** Goal already met at baseline (56.7 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 56.7 days baseline → 56.7 days
- **Member sentence:** “You keep about 56.7 days of spending in hand even on your tightest day over the next 90 days — no change needed.”

![forecast](charts/C_bootstrap_forecast.svg)

![waterfall](charts/C_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Turn on low-balance alert at $150 | Behaviour | Learned | 56.7 | 0.0 | $0 | 0.0 |  |
| Trim shopping by 25% (22% of everyday spend) | Behaviour | Learned | 56.8 | +0.1 | $0 | 0.0 |  |
| Switch on savings-first overdraft order | Product | Exact | 56.7 | 0.0 | $0 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 56.7 | 0.0 | $0 | 0.0 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 55.6 | −1.1 | $0 | 0.0 | PASS payment $90/mo vs half of monthly surplus $96 (surplus $193) |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 56.8 | +0.1 | $0 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 42.2 | −14.4 | $0 | 0.0 |  |

Catalogue notes: move_bills: no movable bill falls within 5 days before a payday. move_rent: housing payment already falls within 2 days after a payday. refinance: no outside credit-card debt on file.

## Dee (D) — Bootstrap

*New member since June 2026 — about four months of history (thin file).*

- Start balance $1,259, savings $0, average daily spend $61.27.
- Headline slack 30/60/90 d: 24.5 / 28.4 / 31.9 days. Lowest true slack: **14.8** (bad case p10 2.6). Risk days: 0. P(any overdraft): 6%. Expected fees: $5.
- Explanation: Your balance is likely lowest on Nov 12 (about $932, 15.2 days of spending). What pulls it down: insurance $95 on Nov 12. Payday on Nov 13 brings it back.
- **Decision:** Goal already met at baseline (14.8 ≥ 2.0 days) — no lever needed.
- **Waterfall:** 14.8 days baseline → 14.8 days
- **Member sentence:** “You keep about 14.8 days of spending in hand even on your tightest day over the next 90 days — no change needed.”

![forecast](charts/D_bootstrap_forecast.svg)

![waterfall](charts/D_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move insurance + subscription + phone autopay to the day after payday | Behaviour | Exact | 16.5 | +1.7 | $0 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | 19.8 | +5.0 | $0 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | 14.8 | 0.0 | $5 | 0.0 |  |
| Trim shopping by 25% (25% of everyday spend) | Behaviour | Learned | 15.1 | +0.3 | $4 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 14.8 | 0.0 | $0 | 0.3 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | 14.0 | −0.7 | $0 | 0.0 | PASS payment $90/mo vs half of monthly surplus $96 (surplus $192) |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | 15.1 | +0.3 | $2 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | 3.1 | −11.7 | $37 | 0.0 |  |

Catalogue notes: savings_first: no savings balance to draw on. refinance: no outside credit-card debt on file.

## Eli (E) — Bootstrap

*Gig driver paid weekly (Tuesdays) with ±30% swings; $4,200 on an outside card at 27.99% APR.*

- Start balance $502, savings $0, average daily spend $78.10.
- Headline slack 30/60/90 d: 6.0 / 4.7 / 2.0 days. Lowest true slack: **-8.7** (bad case p10 -10.5). Risk days: 48. P(any overdraft): 100%. Expected fees: $268.
- Explanation: Your balance is likely lowest on Nov 01 (about $-669, -8.6 days of spending). What pulls it down: rent $980 on Nov 01. Payday on Nov 03 brings it back.
- **Decision:** Goal not met; best partial plan shown. Structural gap: typical spending exceeds income by $329/month.
- **Waterfall:** -8.7 days baseline → +0.9 (Ask to move the rent due date to the day after payday) → -7.8 days
- **Member sentence:** “If you ask your landlord to move the rent due date to the day after payday (if your landlord agrees), your tightest day over the next 90 days goes from about -8.7 to -7.8 days of spending in hand. Because your usual spending runs about $329 a month above your income, that is still short of the 2-day goal — worth a budget conversation with a specialist.”

![forecast](charts/E_bootstrap_forecast.svg)

![waterfall](charts/E_bootstrap_waterfall.svg)

| Lever | Tier | Method | Lowest true slack | Δ vs baseline | Fees 90d | Bill declines | Gate |
|---|---|---|---:|---:|---:|---:|---|
| Move phone + insurance + utilities autopay to the day after payday | Behaviour | Exact | -8.7 | 0.0 | $162 | 0.0 |  |
| Ask to move the rent due date to the day after payday | Behaviour | Exact | -7.8 | +0.9 | $269 | 0.0 |  |
| Turn on low-balance alert at $150 | Behaviour | Learned | -8.7 | 0.0 | $258 | 0.0 |  |
| Trim shopping by 25% (10% of everyday spend) | Behaviour | Learned | -8.7 | 0.0 | $265 | 0.0 |  |
| Fee-free account without courtesy pay | Product | Exact | 0.0 | +8.7 | $0 | 6.4 |  |
| $1,000 personal loan, 12 mo @ 13.99% ($90/mo) | Liquidity | Exact | -14.0 | −5.3 | $121 | 0.0 | FAIL payment $90/mo vs half of monthly surplus $0 (surplus $-329) |
| Refinance $4,200 card debt (27.99% APR) into a 36-mo CU loan @ 11.99%: $168 → $139/mo | Liquidity | Exact | -8.8 | −0.1 | $256 | 0.0 | PASS new payment $139/mo vs current $168/mo |
| Income +10% from day 15 (e.g. extra hours) | Income | Exact | -8.6 | +0.1 | $205 | 0.0 |  |
| STRESS TEST: income −20% (member-agreed, never recommended) *(not surfaced)* | Income | Exact | -9.6 | −0.9 | $429 | 0.0 |  |

Why the other levers were not added:

- `move_bills` — adds only +0.04 days (< 0.1)
- `alerts` — adds only +0.03 days (< 0.1)
- `trim_shopping` — adds only +0.03 days (< 0.1)
- `no_fee_account` — turns fees into ~5.9 declined bill payments per 90 days — needs a member conversation, not auto-adoption
- `cash_loan` — gate failed (cash_loan): payment $90/mo vs half of monthly surplus $0 (surplus $-329)
- `refinance_card` — beats the higher-priority plan by only -0.05 days (< 1.0-day margin)
- `income_up_10` — beats the higher-priority plan by only +0.33 days (< 1.0-day margin)

Catalogue notes: savings_first: no savings balance to draw on.

