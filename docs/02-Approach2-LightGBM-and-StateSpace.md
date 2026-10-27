# Approach 2: LightGBM, its gaps, and a closer-fitting alternative

Approach 2 keeps everything from Approach 1 that is arithmetic:

- the ledger reader
- recurring detection
- the replay engine and product rules
- true slack
- the scenario engine
- the four decision rules and the waterfall

It replaces only the forecaster of everyday spending, with two real algorithms:

- **2a: LightGBM quantile regression**, as Occlet's design doc proposes, run through LightGBM's own native C library from C#.
- **2b: a probabilistic state-space model** (hurdle log-normal spending with a persistent weekly "mood", Kalman-filtered, credibility-pooled across members).

Both are back-tested against Approach 1's bootstrap and against "LightGBM-raw" (LightGBM without the calibration fix). A champion is chosen by Occlet's rule, and Problem 2 then runs on **every** model's frozen baseline, so we can see whether the recommendation depends on the model.

Run it with `dotnet run --project src/Occlet.Approach2`. It takes about 50 seconds and writes `output/approach2/`.

---

## 1. Is LightGBM the right fit "to the T"? Short answer: no. It's a good component, not a complete solution

LightGBM is excellent at one thing: **predicting a number for each row of a feature table**, with very little feature engineering, quickly, and explainably (TreeSHAP). Occlet's problem needs more than that.

| # | What the problem needs | Why plain LightGBM falls short | How this repo closes the gap | Evidence from this run |
|---|---|---|---|---|
| G1 | **Whole daily paths.** "Lowest projected slack", risk days, P(overdraft) and *every fee rule* depend on the path (a $35 fee happens on the day the balance crosses zero) | Quantile regression gives a separate distribution **per horizon**. The q10 for day 30 and the q10 for day 31 say nothing about whether they are the same bad future. The minimum of the q50 line is not the median of the minimum | A **Gaussian copula** joins the horizons into paths: a Brownian-motion correlation plus a persistent share λ estimated from calibration data. The replay engine then applies the fee rules to whole paths | λ estimated at 0.00: the observed 7-day/90-day surprise correlation (0.27) matches pure Brownian structure (√(7/90) = 0.28) |
| G2 | **Known cash flows are exact.** Rent is $1,100 on the 1st | Asked to predict the *balance*, a tree must re-learn every payday and bill from data. It cannot extrapolate: a +10% income lever, outside the training range, would be predicted flat | **Decomposition:** LightGBM predicts only everyday spending (as a multiple of the member's normal pace). Paydays and bills are added by arithmetic, and levers are applied exactly | All exact levers work identically under every model |
| G3 | **Quantiles that never cross** | Five independently trained models can produce q10 > q50 | Sort the five predictions; force cumulative spend to rise with horizon | Zero crossings in output |
| G4 | **Honest uncertainty:** "the 10th percentile must really be the 10th" | Quantile GBMs are **over-confident** on small, overlapping time-series data. Neighbouring 90-day windows share most of their days, so any naive hold-out leaks | **Conformalised quantile regression** with **purged, time-blocked cross-validation**: errors are measured only on periods the model never trained near, and the bands are widened by exactly that much | 80% band: raw **54–56%** → calibrated **73–77%**. Below q10: raw **21–28%** → calibrated **13–19%** |
| G5 | **Works with what a credit union has** | A global tree model is data-hungry. With 5 members, static features act as **member IDs**: the top two by gain are `log_spend_scale` (25%) and `tenure_days` (22%), so the trees memorise members instead of learning behaviour | Pool all members into one model; regularise (7 leaves, ≥ 60 rows per leaf, L2 = 5); carry calibration over to buckets too short to calibrate | **≥ ~7 months of history** (origins from 20 May): LightGBM is the *most* honest model, 9.9% below q10. **≤ ~6 months:** the least, 27.6% |
| G6 | **Lever effects are causal.** "What if she takes a loan?" | A model trained on history learns that borrowers end up with *lower* slack (they were already stretched: Occlet's umbrella example) | Levers are **never** learned by the model; they are rule-based edits replayed through the same futures. "Learned" levers (alerts, trims) carry explicit, stated response assumptions | Same plan engine for all models |
| G7 | **Spending the member wanted, not what got through.** Declined card purchases never post | The label (posted spend) is *censored* on low-balance days | Shared fix: learn from days that started with ≥ $150; impute the rest from trailing healthy days | Once this was fixed, the models' 90-day bias on Eli shrank from −5 to −7.5 days of spending to −1 to −3 days |
| G8 | **Every day 1–90**, not just 30/60/90 | Occlet's draft is 9 models (3 horizons × 3 quantiles), giving three points | Horizon is a **feature**, so 5 models (q10, q25, q50, q75, q90) cover every day | — |

**Verdict.** With G1–G8 handled, LightGBM is a sound engine for the *uncertain part*: fast, explainable with TreeSHAP, and the most calibrated model here once it has enough history. It is not "to the T" for the whole problem. It is not a path model, it is not a cash-flow model, and it needs volume plus calibration before its 10th percentile can be trusted. On this 5-member sample it loses to a model built around the problem's structure.

---

## 2. The closer-fitting alternative: a probabilistic state-space model

Occlet names the **Temporal Fusion Transformer** (TFT) as the challenger. TFT and its cousin DeepAR are *generative sequence models*: they model how a series evolves and sample whole futures. That is exactly what G1 asks for. They need thousands of series and a Python/GPU training stack, so the repo implements their **classical, small-data analogue**:

```
On each day:   spend?  ~ Bernoulli(p_weekday | p_weekend)                 hurdle: many days have no spend
               amount  = exp( μ_daytype + level + x_week + ε ),  ε ~ σ·t₅  log-normal, fat tails for shocks (a car repair)
Each week:     x_week  = φ · x_lastweek + η,   η ~ N(0, σx²(1−φ²))        persistent "spending mood", AR(1)
Each future:   level   ~ N(0, se(μ)²)                                     we only estimated μ: carry that uncertainty
```

Why it fits the problem more closely:

- **Paths are native (G1).** It simulates days in order, so lowest-day slack, fees and risk days come out naturally, without a copula.
- **Its uncertainty grows with the horizon for a stated reason.** The mood x wanders week to week, and the level is only estimated. There is no tree pretending day 90 is as certain as day 9.
- **Small-data friendly (G5).** Six interpretable parameters per member (chance of spending, typical amount, within-week noise, mood persistence φ, mood size σx, level uncertainty). Thin files **borrow strength**: shape parameters are shrunk toward the median member with credibility weight n / (n + 60). Dee, with 107 usable days, gets 0.64.
- **Explainable in plain words.** "On a weekday Asha spends something 58% of the time, typically $40; her mood carries over week to week (φ = 0.70)."
- **The Kalman filter** estimates where today's mood stands from noisy weekly averages, and how sure it is.

---

## 3. Results (rolling-origin back-test: 12 origins two weeks apart, 4 members, 48 windows per horizon)

| Forecaster | Horizon | Inside 80% band (gate 75–85%) | Below q10 (target ≈ 10%) | Scaled pinball (days) |
|---|---:|---:|---:|---:|
| Bootstrap (Approach 1) | 30 / 60 / 90 | 80.0% / 80.0% / 79.5% | 12.6% / 13.8% / **14.9%** | 0.81 / 1.13 / 1.33 |
| **LightGBM** (calibrated) | 30 / 60 / 90 | 76.7% / 73.8% / 73.3% | 13.3% / 16.4% / 18.8% | 0.83 / 1.21 / 1.55 |
| LightGBM-raw (no conformal) | 30 / 60 / 90 | 56.3% / 54.7% / 54.1% | 21.4% / 24.4% / 27.5% | 0.88 / 1.25 / 1.54 |
| **StateSpace** | 30 / 60 / 90 | 80.8% / 79.1% / 78.6% | 12.0% / 13.9% / **15.1%** | **0.80 / 1.10 / 1.27** |

How to read it:

1. **The state-space model is the most accurate at every horizon** (lowest pinball loss), and as well calibrated as the bootstrap.
2. **Calibration is what makes LightGBM usable.** Raw LightGBM leaves 1 in 4 real days below its "10th percentile". The conformal step roughly halves that.
3. **LightGBM's problem is data volume, not the algorithm.** Splitting by origin:

| Below q10 at 90 days | Origins 25 Feb – 6 May (≤ ~6 months of history) | Origins 20 May – 29 Jul (≥ ~7 months) |
|---|---:|---:|
| Bootstrap | 14.5% | 15.3% |
| LightGBM | **27.6%** | **9.9%** (best) |
| StateSpace | 15.8% | 14.4% |

   With enough history LightGBM's low end is the most honest of the three. With little, it is the least. This is Occlet's data-readiness question D1 ("is there 12+ months of clean daily history?") showing up in the numbers. The April–early-May windows were hard for **every** model, because they ran into Asha's summer spending surge.

4. **By member,** LightGBM is the most accurate for Eli (0.85 vs 0.99–1.01), the gig worker with the richest behaviour signal, and the least accurate for Asha.

**Champion selection (Occlet's rule).** A model must pass the low-end gate (5–15% below q10) at every horizon. A challenger must then cut the incumbent's loss by ≥ 5%.

- **StateSpace** has a 3.4% lower average loss than bootstrap, but sits at 15.1% below q10 at 90 days, a hair outside the gate.
- **Bootstrap** sits at 14.9%, a hair inside.
- **Result:** the incumbent bootstrap is kept.

**Lesson for Occlet's Phase 1 pass bar:** with 48 windows the gate's sampling error is about ±4 percentage points (one standard error), so a hard 15.0% cut-off decides between near-identical models by noise. Gates need a tolerance band, or more members.

**Do the models agree on what to recommend?**

| Member | Bootstrap | LightGBM | StateSpace | Agree |
|---|---|---|---|---|
| Asha | move_bills + trim_shopping + move_rent | move_bills + trim_shopping + alerts + move_rent | move_bills + move_rent | core yes, details no |
| Ben | no change | no change | no change | yes |
| Chen | no change | no change | no change | yes |
| Dee | no change | no change | no change | yes |
| Eli | move_rent | move_rent | move_rent | yes |

The **exact** levers (move the bills, then the rent) are recommended under every model. The models disagree only on the small **learned** levers, which is expected, because those depend on assumed behaviour. This is a useful robustness check for a specialist: a recommendation every model agrees on is safer to make.

**Asha's waterfall under each model:**

- Bootstrap: 0.0 → +0.8 (move bills) → +0.8 (trim shopping) → +1.8 (move rent) → **3.4 days**
- LightGBM: −1.3 → +1.3 (move bills) → +0.3 (trim shopping) → +0.2 (alerts) → +1.7 (move rent) → **2.1 days**
- StateSpace: −2.1 → +2.1 (move bills) → +0.3 (move rent) → **0.3 days**. The goal is not reached, and the plan flags Asha's ~$235/month structural gap.

**LightGBM explainability (TreeSHAP, q50, next 30 days).** For Ben, the largest single driver is `alerts_on = 1`, which lowers predicted spending by 0.05 × his normal pace. That is the model picking up the alert he already has. Full tables are in the `LightGBM_SHAP_Drivers` and `LightGBM_Importance` sheets.

---

## 4. The code, line by line

### 4.1 Calling LightGBM from C#: `src/Occlet.Approach2/LightGbm/LightGbmNative.cs`

NuGet was not reachable from the build environment, and a dependency-free build is simpler for you anyway. So the program calls **LightGBM's official C API** directly: the same `lib_lightgbm` that ML.NET's LightGBM trainer and the Python package wrap. `tools/get-lightgbm.ps1` (Windows) and `tools/get-lightgbm.sh` (Linux/macOS) download version 4.5.0 from the official PyPI wheel and verify its SHA-256.

```csharp
[DllImport("lib_lightgbm", CallingConvention = CallingConvention.Cdecl)]
public static extern int LGBM_DatasetCreateFromMat(double[] data, int dataType, int nrow, int ncol, int isRowMajor,
    string parameters, IntPtr reference, out IntPtr handle);     // a feature matrix → a LightGBM dataset
… LGBM_DatasetSetField(ds, "label", y, …)                          // attach the target
… LGBM_BoosterCreate(ds, trainParams, out booster)                 // a model with the given objective
… LGBM_BoosterUpdateOneIter(booster, out finished)                 // add one tree (called 200 times)
… LGBM_BoosterPredictForMat(…, PredictNormal …)                    // predict
… LGBM_BoosterPredictForMat(…, PredictContrib …)                   // TreeSHAP contributions (explainability)
… LGBM_BoosterSaveModel(…)                                         // models/lightgbm_q10.txt … q90.txt (audit)
```

`Check(rc)` turns a non-zero return code into an exception carrying LightGBM's own error text. `IsAvailable` tests whether the library can be loaded. If it can't, Approach 2 still runs the other two models and tells you how to fetch it.

### 4.2 Features: `LightGbm/FeatureBuilder.cs`

Each training row asks: *"Standing on ORIGIN, how much everyday spending will happen over the next H days, as a multiple of this member's normal pace?"* Using a multiple (rather than dollars) is what lets five members of very different sizes share one model.

| Feature | Meaning (all computed from data on or before the origin) |
|---|---|
| `horizon_days` | H, so one model covers every horizon |
| `spend7_over_spend90`, `spend30_over_spend90` | Is spending running hot or cold recently? |
| `spend_cv30` | Volatility of daily spending (Occlet: "spending volatility") |
| `weekend_share`, `holiday_share` | Share of the window that is weekends, or 1–24 December |
| `cushion_days` | Today's balance ÷ normal daily spend (people spend less when low) |
| `overdraft_fees_90d` | Recent overdrafts (Occlet: "recent overdrafts") |
| `days_to_payday`, `known_net_flow_ratio`, `tight_windows` | Known future events: next payday, scheduled net flow in the window, bills ≤ 5 days before a payday |
| `tenure_days`, `courtesy_pay`, `direct_deposit`, `alerts_on` | Profile and product |
| `origin_day_of_month`, `origin_day_of_week` | Calendar |
| `income_cv`, `log_spend_scale` | How variable the pay is; the member's size |

`Target(origin, h)` is the everyday spend over (origin, origin+h] ÷ (normal pace × h), computed from prefix sums so each row costs O(1).

### 4.3 Training and calibration: `LightGbmQuantileForecaster.Fit`

```csharp
for (var origin = start + 59 days; origin < cutoff; origin += 2 days)          // every member, every other day
    var hs = HorizonGrid.Where(h => origin.AddDays(h) <= cutoff);              // only labels fully observed by the cutoff
```

This builds about 8,100 rows at the final cutoff: horizons 1, 2, 3, 5, 7, 10, 14, 21, 28, 35, 45, 60, 75, 90.

```csharp
// purged, time-blocked cross-validation — 4 blocks of origins
trIdx = rows whose label window ENDS before the block starts
        OR whose feature window STARTS after the block's label windows end;
models = TrainAll(trIdx);  oof[k][i] = models[k].Predict(block rows);         // honest "out-of-fold" predictions
```

The purge is the crucial line. Two 90-day windows that start a week apart share 83 days; if one is used for training and the other for calibration, the model "already knows" the answer and looks falsely accurate. Purging removes every training row that overlaps the block being scored.

```csharp
_models = TrainAll(rows);                                                     // final models on ALL rows
_cLo[b] = ConformalQuantile(oof q10 − y, 0.10);                               // how far below q10 the truth fell, 90th pct
_cHi[b] = ConformalQuantile(y − oof q90, 0.10);                               // how far above q90 …
```

This is split-conformal calibration with the finite-sample correction ⌈(n+1)(1−α)⌉/n, per horizon bucket (1–7, 8–30, 31–90 days). A bucket with too few honest errors (short histories) inherits the shorter bucket's widening rather than none.

```csharp
z = Φ⁻¹(PIT of the 7-day outcome), Φ⁻¹(PIT of the 90-day outcome)  for the same member and origin
λ = (corr − √(7/90)) / (1 − √(7/90))                                          // persistent share beyond a random walk
```

### 4.4 Turning quantiles into daily paths: `LightGbmQuantileForecaster.SamplePaths`

```csharp
var q = Adjust(5 raw predictions, h);                    // conformal offsets, then SORT → no crossing
cum[j] = q × scale × h;  cum[j] = max(cum[j], cum[j-1]); // cumulative spend can only rise with horizon
w += N(0,1)·√(h_j − h_{j−1});                            // Brownian motion over the horizon …
z = √λ·z0 + √(1−λ)·w/√h_j;                               // … plus a persistent part
c = max(InvCdf(Φ(z), cum[j]), prevC);                    // this future's cumulative spend at h_j
row[d] = (c − prevC) · weight_d / Σweights;              // spread across the days, weekends weighted heavier
```

The output has the same shape as the bootstrap's (2,000 futures × 90 days of everyday spend), so the replay engine, true slack, scenarios and decision rules are reused unchanged.

### 4.5 The state-space model: `StateSpace/StateSpaceForecaster.cs`

**`Estimate`** fits the model to the last 180 healthy days:

```csharp
pWd = share of healthy weekdays with any spend;  pWe = same for weekends   // the hurdle
μWd, μWe = mean log(amount) on spend days                                 // typical amount by day type
residual r = log y − μ_daytype;  grouped into 7-day blocks
σ²within = pooled within-week variance                                     // day-to-day noise
σ²x = var(weekly means) − average noise in a weekly mean                   // the real week-to-week mood
φ = lag-1 autocovariance of weekly means ÷ σ²x                             // how long a mood lasts
Kalman: predict m ← φm, P ← φ²P + σ²x(1−φ²); update with each week's mean  // where the mood stands today
se(μ) = √(σ²within/n_spend_days + σ²x(1+φ)/(1−φ)/n_weeks)                 // how well we know the typical amount
```

**`Fit`** computes the pooled prior (the median member's p, σ, φ, σx). **`SamplePaths`** blends each member's own estimates with it using weight n / (n + 60), then simulates:

```csharp
x = m + √P·z;  level = se(μ)·z                       // today's mood and level, both uncertain
if (d % 7 == 0) x = φ·x + σx·√(1−φ²)·z               // the mood moves each week
if (U < p_daytype) spend = min(exp(μ + level + x + σ·t₅), 1.5 × biggest day seen)
```

### 4.6 Champion selection and outputs: `src/Occlet.Approach2/Program.cs`

1. Back-test all four forecasters with the same 12 origins.
2. A model is eligible only if it passes the low-end gate at 30, 60 **and** 90 days. If the incumbent (bootstrap) fails the gate, the best eligible model wins. Otherwise a challenger must beat it by ≥ 5% in loss.
3. Re-fit every finalist on all data up to 27 Oct 2026 and run Problem 1 and Problem 2 for every member and model.
4. Save the LightGBM models, the TreeSHAP drivers, the state-space parameters and the recommendation-agreement table.

---

## 5. Outputs (`output/approach2/`)

| File or sheet | What it holds |
|---|---|
| `REPORT.md` | Champion selection, model agreement, LightGBM diagnostics, and every member under every model |
| `results.xlsx` · `Summary`, `Baseline_Paths`, `Levers_Alone`, `Plan_Waterfall`, `Decision_Log` | As in Approach 1, with a `Forecaster` column |
| `Backtest`, `Backtest_ByMember`, `Backtest_ByOrigin`, `Model_Selection` | The comparison above |
| `LightGBM_Fits` | Per cutoff: rows, conformal offsets, copula λ, calibration coverage before and after |
| `LightGBM_Importance`, `LightGBM_SHAP_Drivers` | Explainability |
| `StateSpace_Params` | Each member's fitted parameters and credibility weight |
| `Recommendation_Agreement` | Does each model recommend the same plan? |
| `models/lightgbm_q10.txt … q90.txt` | The frozen LightGBM models (open in any text editor) |
| `artifacts/*.json`, `charts/*.svg` | Frozen baselines and charts for each member and model |

## 6. What I would do with a real credit union's data

1. **Pool thousands of members.** That is where a global LightGBM earns its keep, and where `tenure_days` stops acting as a member ID.
2. Keep the **decomposition** (exact cash flows + modelled everyday spend), the **purged conformal calibration**, and the **path copula**: none of them depends on the data size.
3. Run LightGBM, the state-space model and (if the volume supports it) a TFT/DeepAR model trained in Python and served to C# via ONNX, all through the same back-test. Pick by Occlet's gate **with a tolerance band**.
4. Replace the illustrative alert and trim response rates with ones measured on members who actually turned those features on (Occlet's question D2).
