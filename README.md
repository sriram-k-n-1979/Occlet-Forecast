# Occlet Forecast

A C#/.NET prototype for daily balance forecasting, slack metrics and scenario comparisons. All input data is synthetic: five illustrative members with one year of posted transactions.

**Current result: no validated champion.** The corrected evaluation finds that none of Bootstrap, calibrated LightGBM or StateSpace passes all endpoint and path-minimum risk gates. Reports retain hypothetical scenarios for specialist inspection, but member advice is withheld for models or members without passing validation.

## Two approaches

| | Approach 1 | Approach 2 |
|---|---|---|
| Forecaster | Historical spending bootstrap | LightGBM quantile models and a state space alternative |
| Shared components | Cash replay, scenarios, affordability, metrics, reporting | Same |
| Guide | [Bootstrap walkthrough](docs/01-Approach1-Rudimentary.md) | [Model comparison walkthrough](docs/02-Approach2-LightGBM-and-StateSpace.md) |
| Results | [Approach 1 report](output/approach1/REPORT.md) | [Approach 2 report](output/approach2/REPORT.md) |

LightGBM predicts cumulative everyday spending, not ending balance directly. Known cash events are then replayed with product rules. This is an alternative to Occlet's original direct-balance model proposal.

## Build and run

Requires .NET 8 SDK. Tests and application code use no NuGet packages. LightGBM requires its pinned native library.

```powershell
./tools/get-lightgbm.ps1
dotnet build -m:1
dotnet run --no-build --project tests/Occlet.Tests
dotnet run --no-build --project src/Occlet.Approach1 -- data/members_ledger.xlsx output/approach1 --paths 2000
dotnet run --no-build --project src/Occlet.Approach2 -- data/members_ledger.xlsx output/approach2 --paths 2000 --origins 12 --dump-points
```

On Linux/macOS use `bash tools/get-lightgbm.sh` before building; macOS also needs libomp. Windows needs the Visual C++ runtime for the native DLL. Without that library, Approach 2 explicitly skips LightGBM and still runs Bootstrap and StateSpace; that is not a full model comparison.

The dependency-free regression executable returns a nonzero exit code on failure. Run it with `dotnet run`, not `dotnet test`. There are 28 regression checks covering accounting, replay, historical features and evaluation.

## Corrected comparison

Forecast origin: 27 October 2026. Final forecasts: 2,000 paths/member. Backtest: 12 origins separated by 14 days, 500 paths/window and 48 member-origin observations per horizon. Origins overlap, so those observations are not independent. The thin-history member is forecast but has no comparable backtest history.

| Forecaster | Endpoint scaled pinball at 30 / 60 / 90 days | 90-day endpoint coverage | 90-day outcomes below q10 | Accepted across all horizons |
|---|---|---:|---:|---|
| Bootstrap | 1.09 / 1.62 / 2.05 | 75.0% | 22.9% | No |
| LightGBM calibrated | 1.19 / 2.15 / 3.04 | 75.0% | 20.8% | No |
| LightGBM raw | 1.26 / 1.98 / 2.61 | 58.3% | 27.1% | Diagnostic only; fails |
| StateSpace | 1.07 / 1.54 / 1.96 | 79.2% | 18.8% | No |

Lower pinball is better. StateSpace has the lowest endpoint loss in this synthetic experiment, but its lower tail and minimum-balance calibration still fail. This is not evidence that it is ready for member recommendations or universally superior to LightGBM.

Older results averaged all days within each horizon. Current reports separate endpoint accuracy, daily-window accuracy and minimum-balance risk. Do not compare an old window score directly with a new endpoint score.

## Repairs

- Preserve payment channels: recurring card charges no longer follow ACH courtesy-pay rules.
- Credit savings when checking transfers into savings, and debit savings for the reverse transfer.
- Apply everyday card spending as whole tickets using historical within-day proportions; never partially pay an individual ticket.
- Keep declined scheduled obligations outstanding after the automatic retry limit.
- Reduce new loan principal only when its repayment actually posts.
- Apply an affordability test to both cash loans and refinancing on the edited candidate.
- Recognize last-business-day payroll and weekend-adjusted dates crossing month boundaries.
- Build each LightGBM historical feature snapshot at its own origin; keep that classification fixed when constructing future targets.
- Use forward-only calibration folds with fully observed training labels.
- Require endpoint coverage, lower-tail and path-minimum gates, with explicit abstention when none pass.
- Check the complete replay-input hash before cached scenarios are returned.

See [repair notes and verification](docs/03-Repairs-and-Validation.md).

## Layout

- `src/Occlet.Core`: ledger loading, recurring events, baseline construction, replay, scenarios, decisioning and evaluation.
- `src/Occlet.Approach1`: bootstrap console runner.
- `src/Occlet.Approach2`: native LightGBM bindings, origin-specific features, calibrated path generation, state space model and comparison runner.
- `src/Occlet.DataGen`: synthetic ledger generator; not rerun during the repairs.
- `tests/Occlet.Tests`: executable regression checks.
- `output/approach1`, `output/approach2`: refreshed reports, workbooks, charts, summary artifacts, metrics and models.
- `output/approach2/backtest_points_*.csv`: raw synthetic observations for independently checking endpoint and window metrics.

## Remaining boundaries

This remains an experimental system. Posted balances are a proxy for available balances because holds and pending authorizations are absent. Historical product settings are assumed constant in the supplied synthetic data. Estimated behavioural effects, ticket-size allocation and spending censoring are modelling assumptions, not measured causal effects.

The JSON baseline is still a summary export, not a complete reloadable scenario bundle. Full persistence/reload, institution-specific principal accounting and affordability policy, verified consent, a strict lever-count policy, held-out real cohorts and end-to-end explanations remain work before production. The code does not automatically execute payments, switch accounts or originate loans.
