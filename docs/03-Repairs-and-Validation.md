# Repairs and repeated model comparison

Repair date: 30 October 2026. Starting repository commit: `da3908f41f6a8c4509b745fbe473c925c7629a28`.

## Outcome

The repaired code builds successfully and all 28 regression checks pass. The full model comparison ran with Bootstrap, calibrated LightGBM, raw LightGBM and StateSpace. StateSpace has the smallest average endpoint pinball loss, but **none passes all acceptance gates**. The application now abstains instead of promoting the least-bad model.

These are correctness repairs and a repeated experiment, not a claim of production readiness or improved predictive performance on real people.

## Changes completed

| Area | Repair | Verification |
|---|---|---|
| Payment handling | Keep event channel, category and identity; route scheduled cards through card rules | A $90 card charge against $50 is declined without the former $35 fee |
| Transfers | Credit/debit the linked savings balance when an internal transfer posts | Checking plus savings conserves cash; later savings coverage succeeds |
| Everyday purchases | Sample historical ticket proportions once per baseline and accept/decline whole tickets | Single and multiple ticket cases; fractional-cent splits cannot overspend the daily total |
| Fees and failed payments | Share fee caps across channels; preserve unpaid obligations after retries stop | Fee-cap, failed-retry and successful-retry tests |
| Debt state | Reduce new loan principal only on successful repayment | Missed repayments retain both debt and unpaid obligation |
| Affordability | Apply the candidate-state policy to refinancing and cash loans after all edits | Eli's cheaper refinance no longer passes solely because its payment falls |
| Payroll | Preserve last-business-day schedules and cross-month weekend shifts | December payroll on the 31st; next-month salary advanced into the current month |
| Integrity | Hash replay metadata and ticket splits, check hash before cache hits | Mutated event uncertainty and cached-input mutations detected |
| Historical features | Build snapshots at each origin, separating future labels | Future transactions cannot alter earlier feature rows |
| Targets | Freeze recurring classification and denominator at origin | Target tests confirm later recurrence detection does not rewrite the target definition |
| Calibration | Train each validation fold on earlier fully observed labels only | Native LightGBM retraining and comparison completed |
| Evaluation | Separate endpoint, daily-window and path-minimum measures | A last-day failure cannot be diluted by 89 correct earlier days |
| Selection | Require all horizon gates; abstain on failure; block advice for missing/failing member validation | Coverage, minimum risk, missing-horizon, improvement-margin and advice-blocking tests |
| Reporting | Refresh reports, workbooks, raw observations and model metadata | Explicit experimental status and separate metric definitions |

## Repeated experiment

- Same synthetic ledger; the data generator was not rerun.
- Forecast origin 27 October 2026, final horizon 90 days.
- 2,000 paths per latest member forecast; seed 7.
- Backtest: 12 origins, 14-day spacing, 500 paths per member/origin, 48 endpoint observations per horizon.
- Five members in the final forecast, four with enough history for those backtest windows.
- .NET SDK 8.0.427 and the repository's pinned native LightGBM 4.5.0 on Windows x64.
- The regression suite is a package-free executable; `dotnet run --project tests/Occlet.Tests` returns nonzero on failure.

| Forecaster | Endpoint pinball 30 / 60 / 90 days | Mean endpoint pinball | 90-day coverage | 90-day below q10 | All gates pass |
|---|---|---:|---:|---:|---|
| Bootstrap | 1.09 / 1.62 / 2.05 | 1.58 | 75.0% | 22.9% | No |
| LightGBM | 1.19 / 2.15 / 3.04 | 2.12 | 75.0% | 20.8% | No |
| LightGBM-raw | 1.26 / 1.98 / 2.61 | 1.95 | 58.3% | 27.1% | No |
| StateSpace | 1.07 / 1.54 / 1.96 | 1.52 | 79.2% | 18.8% | No |

Pinball is scaled into days of spending; smaller is better. The lower-tail target is approximately 10%, with the existing 5â€“15% prototype tolerance. Interval coverage must be 75â€“85%. The same coverage and lower-tail checks now apply to path minima. All required horizons must pass.

The result is not equivalent to the old README comparison: old headline metrics averaged every day within a horizon. Current endpoint metrics score only the specified horizon's final day. Daily-window metrics are still exported separately, and neither is used to conceal minimum-balance failures.

Origins overlap, and 48 endpoint observations are not 48 independent trials. The five synthetic members cannot justify population-level model ranking, a universal minimum-history requirement or production approval. Thresholds were not loosened or tuned after seeing the results.

Asha illustrates the practical change: the old bootstrap report showed a 3.4-day plan reaching the goal. The repaired replay and obligation checks produce an experimental 1.7-day plan that does not reach the two-day goal. This change is not simply a forecasting-score adjustment.

Independent output checks reconciled endpoint coverage and lower-tail rates against the raw CSVs for all 12 model/horizon combinations. All 20 member/model report entries withhold unvalidated advice, and both workbooks label final plans as experimental.

## Reproduce

```powershell
./tools/get-lightgbm.ps1
dotnet build -m:1
dotnet run --no-build --project tests/Occlet.Tests
dotnet run --no-build --project src/Occlet.Approach1 -- data/members_ledger.xlsx output/approach1 --paths 2000
dotnet run --no-build --project src/Occlet.Approach2 -- data/members_ledger.xlsx output/approach2 --paths 2000 --origins 12 --dump-points
```

The comparison output must say `lib_lightgbm loaded` and show all four forecasters. Otherwise the native model comparison has not run.

Ledger SHA-256: `5f7437b37a539b2bb642039d0b31368ac6dec0ae1270207dc4e359043e6a29d2`.

## Outputs

- [Full comparison](../output/approach2/REPORT.md)
- [Machine-readable metrics](../output/approach2/metrics.json)
- [Approach 1 results](../output/approach1/REPORT.md)
- [Workbook](../output/approach2/results.xlsx)
- Raw `backtest_points_*.csv` files retain the synthetic observations used for both endpoint and window scoring.
- `models/forecast_config.json` records the feature schema, horizon grid, calibration offsets and dependence parameter alongside the saved trees.

## Deliberately not claimed as solved

The repair scope is shared accounting/replay, historical feature construction, evaluation and rerunning the comparison. The broader review also identified product decisions and integration work that remain:

- Available balance still needs actual holds, pending authorizations and institution definitions; current output explicitly says posted-balance proxy.
- Product flags in the synthetic data are constant. Effective-dated product history is required for real retrospective account changes.
- Behavioural effects, the $150 censoring heuristic and ticket-size proportions remain assumptions. The change makes ticket payment semantics explicit; it does not reveal unobserved declined purchases.
- The saved baseline JSON remains a summary, not a complete independent replay bundle with a tested loader.
- The current true-slack convention subtracts outstanding newly disbursed principal. Institution policy must confirm that definition and account scope. Missed-payment interest and penalties are not accrued by this prototype.
- Consent, strict lever-count constraints and exhaustive plan search were not added in this repair pass. Generated plans remain conditional experiments.
- SHAP remains a raw spending-model diagnostic, not a complete explanation of the final balance or causal intervention.
- Real held-out cohorts, further calibration and end-to-end production controls are still needed before recommending actions to members.

No thresholds were changed to force a successful champion, and no financial actions are executed by this repository.
