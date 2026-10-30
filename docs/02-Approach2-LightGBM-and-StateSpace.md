# Approach 2 LightGBM and state space comparison

Both alternatives implement `ISpendForecaster` and use the repaired shared cash engine. The full refreshed comparison is in [the generated report](../output/approach2/REPORT.md). No model passes all acceptance gates on this synthetic dataset.

## LightGBM

The target is cumulative everyday spending divided by origin-time spending scale and horizon length. Horizon is a feature. Five global models predict quantiles 0.10, 0.25, 0.50, 0.75 and 0.90. Salary and bills are applied separately by replay; the model does not directly predict final balance.

### Training without historical look-ahead

For each member and historical origin:

1. Cut history at that origin.
2. Detect recurring schedules using only that history.
3. Build spending features, variability, projected cash covariates and scale from the origin snapshot.
4. Construct future labels from observations, keeping the origin's recurring classification and scale fixed.
5. Require the entire target window to be observed by the training cutoff.

Future rows can no longer retroactively establish recurring bills in earlier feature rows. Labels still use imputed attempted spending where observations are censored; the ledger does not provide actual attempted transactions.

Product/profile flags remain constant in the supplied synthetic data. Real historical account changes require effective-dated profile snapshots before this pipeline can backtest them faithfully.

### Calibration and paths

Calibration folds train only on labels ending before the validation block begins. Future-side training rows have been removed. The resulting offsets are empirical temporal adjustments, not a distribution-free coverage guarantee for overlapping time-series observations.

Inference applies quantile offsets, sorts quantiles, enforces cumulative nondecreasing totals, samples a Gaussian dependence model, clips decreasing samples and allocates totals over daily segments. The grid now includes day 30 explicitly. Final coverage is evaluated after cash replay; model-output coverage alone does not validate balance or minimum-risk estimates.

Saved model output now includes `forecast_config.json` with quantile levels, horizon grid, feature names, calibration offsets, copula parameter, version and fit diagnostics. A complete load-and-replay workflow is still future work.

### SHAP boundary

SHAP contributions explain the raw middle prediction for normalised cumulative spending. They do not explain the final balance, postprocessing or a causal behavioural intervention. The existing specialist top-feature table remains a diagnostic; complete base-value accounting and event-level explanations are not implemented by these repairs.

## State space

The state space alternative samples spending occurrence, log amount, a weekly persistent state and uncertainty in the spending level. It blends selected member parameters toward a population prior. It uses capped log amounts with Student t shocks, not a literal uncapped log-normal distribution.

Its native daily paths fit the replay interface directly. Its spending cap, healthy-day selection, shrinkage and parameter estimation remain modelling assumptions. No model tuning was performed to force it to pass the repaired acceptance policy.

## Comparison protocol

- Same immutable input ledger and forecast origin, 27 October 2026.
- Five synthetic members; four eligible for the 12 historical origins.
- Origins 14 days apart; 500 simulated paths per member/origin.
- Latest forecasts use 2,000 paths per member and seed 7.
- Raw LightGBM is an ablation, not an eligible production finalist.
- Score endpoints separately from daily-window and path-minimum diagnostics.
- Accept a model only when endpoint coverage, endpoint lower-tail frequency and minimum-balance gates pass at every required horizon.
- A qualified challenger must improve endpoint loss by at least 5% to replace a qualified bootstrap incumbent.
- Abstain if no model qualifies. Withhold member advice for failing or unvalidated member cohorts.

The 48 observations at each horizon overlap in time and do not constitute 48 independent experiments. Five synthetic members cannot establish deployment readiness or a general minimum-history threshold.

## Current interpretation

StateSpace has the lowest endpoint pinball loss at all three horizons, followed by Bootstrap. Calibrated LightGBM improves interval coverage relative to raw LightGBM, but does not consistently improve loss. All models miss the lower-tail target at longer horizons, and minimum-risk gates also fail.

The correct result is **no validated champion**, not automatic promotion of the model with the lowest average loss. Detailed values and raw backtest CSVs are committed under `output/approach2`.
