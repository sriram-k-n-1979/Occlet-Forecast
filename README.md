# Occlet Forecast: slack under scenarios, in C#/.NET

This repo solves the two problems in Occlet's *Modeling Slack Under Scenarios* design, for a credit union's members:

- **Problem 1 · Baseline forecast ("if nothing changes").** Forecast each member's daily available balance for 90 days as three lines: pessimistic (q10), likely (q50) and optimistic (q90). Derive slack from them, explain the dip in plain words, and freeze the result as a versioned, hashed artifact.
- **Problem 2 · Scenario engine ("what if…?").** Copy the frozen baseline, apply levers (behaviour → product → liquidity → income), replay the same futures, compute **true slack** (borrowed money excluded), and let four decision rules choose a plan and show its **waterfall**.

Two approaches share one engine:

| | Approach 1: rudimentary | Approach 2: forecasting algorithms |
|---|---|---|
| Everyday-spending forecaster | **Bootstrap**: re-use real past days at random, with a monthly "spending mood" | **LightGBM** quantile regression (Occlet's proposal), plus calibration and path fixes, **and** a **state-space** model (hurdle log-normal + AR(1) mood + Kalman + credibility pooling) |
| Problem 2 engine, rules, waterfall | shared | shared |
| Walkthrough | [docs/01-Approach1-Rudimentary.md](docs/01-Approach1-Rudimentary.md) | [docs/02-Approach2-LightGBM-and-StateSpace.md](docs/02-Approach2-LightGBM-and-StateSpace.md), including **why LightGBM is not a fit "to the T"** and the gaps it has |
| Results | [output/approach1/REPORT.md](output/approach1/REPORT.md) | [output/approach2/REPORT.md](output/approach2/REPORT.md) |

**All data is synthetic and illustrative**: five made-up members (Asha, Ben, Chen, Dee, Eli) with one year of transactions in [data/members_ledger.xlsx](data/members_ledger.xlsx).

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download). No NuGet packages are used: Excel I/O, charts and the LightGBM bindings use only the .NET base library.

```powershell
# Windows (PowerShell), from the repo root
./tools/get-lightgbm.ps1                       # once: fetches LightGBM 4.5.0's native DLL (SHA-256 verified) into native/win-x64
dotnet build
dotnet run --project src/Occlet.DataGen        # (optional) regenerate data/members_ledger.xlsx
dotnet run --project src/Occlet.Approach1      # ~8 s  → output/approach1
dotnet run --project src/Occlet.Approach2      # ~50 s → output/approach2
```

```bash
# Linux / macOS (macOS also needs: brew install libomp)
bash tools/get-lightgbm.sh
dotnet build && dotnet run --project src/Occlet.Approach1 && dotnet run --project src/Occlet.Approach2
```

Options: `-- <ledger.xlsx> <outDir> --paths 2000` for both programs; `--no-backtest` (Approach 1); `--origins 12 --dump-points` (Approach 2). Without the LightGBM library, Approach 2 still runs the bootstrap and state-space models and tells you how to fetch it.

**Windows notes.**
- If PowerShell blocks the script, run `powershell -ExecutionPolicy Bypass -File tools/get-lightgbm.ps1`.
- LightGBM's DLL needs the **Microsoft Visual C++ 2015–2022 Redistributable (x64)**, which provides `MSVCP140.dll`, `VCOMP140.dll` and `VCRUNTIME140.dll`. It is already present on most machines with Visual Studio. If Approach 2 reports `DllNotFoundException`, install it from Microsoft.

## What it found (origin 27 Oct 2026)

| Member | Situation | Lowest true slack (median future) | Plan (Approach 1) |
|---|---|---:|---|
| Asha | Bills before payday | 0.0 days | 0.0 → +0.8 (move phone/insurance/subscription autopay) → +0.8 (trim shopping) → +1.8 (ask to move rent) → **3.4 days** |
| Ben | Biweekly pay, alerts already on | 11.3 | No change needed |
| Chen | Comfortable | 56.7 | No change needed |
| Dee | Thin file (4 months) | 14.8 | No change needed |
| Eli | Weekly gig pay, spending > income | −8.7 | −8.7 → +0.9 (ask to move rent) → −7.8 · **structural gap ≈ $329/month**; loan blocked by ability-to-repay; fee-free account blocked (would decline ~6 bills) |

Rolling-origin back-test (12 origins, 48 windows per horizon):

| | 80% band (gate 75–85%), 30/60/90 d | Below q10 (target ≈ 10%), 30/60/90 d | Scaled pinball, 30/60/90 d |
|---|---|---|---|
| Bootstrap | 80 / 80 / 79% | 13 / 14 / 15% | 0.81 / 1.13 / 1.33 |
| LightGBM (calibrated) | 77 / 74 / 73% | 13 / 16 / 19% | 0.83 / 1.21 / 1.55 |
| LightGBM-raw | 56 / 55 / 54% | 21 / 24 / 28% | 0.88 / 1.25 / 1.54 |
| State-space | 81 / 79 / 79% | 12 / 14 / 15% | **0.80 / 1.10 / 1.27** |

The headline: LightGBM needs calibration and history. Raw, 1 in 4 real days falls below its 10th percentile. With conformal calibration and **≥ 7 months** of pooled history it is the most honest model (9.9% below q10); with less it is the least (27.6%). On this small sample, the state-space model is the most accurate and is as well calibrated as the bootstrap. Every model recommends the same **exact** levers.

## Repository layout

```
data/members_ledger.xlsx                 sample ledger (Ledger, Members, README sheets)
src/Occlet.Core/                         shared engine
  Io/Xlsx.cs                             .xlsx reader/writer (System.IO.Compression + XML)
  Ledger/                                transactions, member profiles, daily balances, UpTo(cutoff)
  Forecasting/Recurring.cs               payday & bill detection (weekly, biweekly, twice-monthly, monthly) + projection
  Forecasting/Everyday.cs                everyday-spend series, censoring fix, average daily spend
  Forecasting/BootstrapForecaster.cs     APPROACH 1 forecaster
  Engine/Replay.cs                       replay engine: courtesy pay, NSF, Reg E declines, savings-first, alerts
  Engine/Slack.cs                        q10/q50/q90, slack, TRUE slack, risk days
  Engine/Baseline.cs                     build + freeze (SHA-256) + explain + artifact JSON
  Scenarios/                             levers, lever catalogue, scenario engine (copy → edit → replay)
  Decisioning/Recommender.cs             the four rules + waterfall + member sentence
  Evaluation/Backtest.cs                 rolling-origin back-test and scoring
  Reporting/                             results.xlsx, SVG charts, REPORT.md
src/Occlet.Approach1/Program.cs          Approach 1 console app
src/Occlet.Approach2/                    Approach 2 console app
  LightGbm/LightGbmNative.cs             P/Invoke bindings to LightGBM's C API
  LightGbm/FeatureBuilder.cs             features (Occlet's families) + targets
  LightGbm/LightGbmQuantileForecaster.cs quantile models + purged conformal calibration + copula paths + TreeSHAP
  StateSpace/StateSpaceForecaster.cs     hurdle log-normal AR(1) + Kalman + credibility pooling
src/Occlet.DataGen/Program.cs            generates the sample ledger
tools/get-lightgbm.ps1 | .sh             fetch LightGBM's native library from PyPI (checksum-verified)
output/approach1, output/approach2       generated results (REPORT.md, results.xlsx, charts, artifacts, models)
```

## The four decision rules (as implemented)

1. **Behaviour first.** Try behaviour levers; stop as soon as the tightest day has ≥ 2 days of true slack. Levers the member controls come before ones needing someone else's consent (the landlord).
2. **Then product, then liquidity, then income**, only if still needed.
3. **A lower-priority lever only wins with a clear margin and high confidence:** it must add ≥ 1.0 day of true slack on top of the higher-priority plan, and be positive in ≥ 80% of simulated futures. Loans must also pass **ability-to-repay**. A lever that turns fees into declined bills (> 0.25 per 90 days) is never auto-adopted.
4. **Always show the waterfall.** Each step is the change in the score, so the steps add up to the total.
