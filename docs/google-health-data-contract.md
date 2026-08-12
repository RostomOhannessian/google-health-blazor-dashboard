# Google Health data contract

The dashboard distinguishes Google Health provider fields from the separate
manual proprietary Cardio Load series. This document defines the Google source
contract and the locally persisted fields that accompany it.

## Active fields

| Domain property | Google data type | Method | Notes |
|---|---|---|---|
| `RestingHeartRateBpm` | `daily-resting-heart-rate` | `list` | Daily value in bpm. |
| `HrvRmssdMilliseconds` | `daily-heart-rate-variability` | `list` | Daily RMSSD value in milliseconds when available. |
| `DailyVo2MaxMlKgMin` | `daily-vo2-max` | `list` | Daily cardio-fitness VO2 Max in ml/kg/min. |
| `RunVo2MaxMlKgMin` | `run-vo2-max` | `dailyRollUp` | Average daily rollup value. |
| `SleepEfficiency` | `sleep` | `list` | Provider sleep efficiency, normalized to a percentage. When omitted, the client derives sleep minutes divided by sleep-period minutes. |
| `DeepSleepMinutes` | `sleep` | `list` | Minutes for the `DEEP` stage from `SleepSummary.stagesSummary` or stage intervals. |
| `RemSleepMinutes` | `sleep` | `list` | Minutes for the `REM` stage from `SleepSummary.stagesSummary` or stage intervals. |
| `ConsumedCaloriesKcal` | `nutrition-log` | `dailyRollUp` | Energy rollup in kcal. |
| `CarbohydratesGrams` | `nutrition-log` | `dailyRollUp` | Total carbohydrate rollup in grams. |
| `FatGrams` | `nutrition-log` | `dailyRollUp` | Total fat rollup in grams. |
| `ProteinGrams` | `nutrition-log` | `dailyRollUp` | Nutrient rollup for `PROTEIN`. |
| `EstimatedAlcoholGrams` | — | local calculation | Remaining energy after carbohydrate (4 kcal/g), fat (9 kcal/g), and protein (4 kcal/g), divided by 7 kcal/g. Complete nutrition rows with less than 70 kcal remaining are stored as `0`; incomplete rows remain `null`. |

## Date and range behavior

- Manual sync and metric queries support 1-366 days at a time. Fixed dashboard
  ranges are exact inclusive UTC calendar ranges through today: 7/30/90 days
  include today plus the preceding days, and year-to-date runs from January 1
  through today.
- The selected range is used unchanged for sync and export. The dashboard table
  and charts load all persisted local history, initially show the selected
  newest window, and provide scrollbars for older days. The table uses a fixed
  compact fixed viewport of about 12 rows, independent of the selected range.
  Partial weeks remain visible. Weekly table summary rows are optional and
  enabled by default.
- The automatic daily sync remains configurable from 1-90 days.
- Google Health daily rollups use closed-open civil-date ranges aligned to the requested days.
- Rollup requests are chunked in 14-day windows to stay inside stricter Google Health range limits for affected data types.
- Missing data points are represented as `null`, not as `0`.
- Overnight sleep is assigned to the date of its civil end/wake time using
  `sleep.interval.civil_end_time`. For each date, a provider-marked main sleep
  session is preferred; if none is marked, the longest session is used.

## Manual Cardio Load and training-strain calculation

Google Health does not publish `daily-cardio-load`, `cardio-load`,
`training-load`, `daily-target-load`, or `target-load` data types. The
integration never requests those speculative endpoints. Google sync never
writes `CardioLoad`, `TargetLoad`, or `Acwr`; those nullable fields are a
distinct proprietary manual series edited in the dashboard.

Manual weekly target amounts are user-entered and may be cleared. Each target
applies from Monday through Sunday; saving a target writes it to each of the
seven daily records in the selected week, creating missing records as needed.
The dashboard projects it across that week for display. Targets are not
automatically calculated, and are not a Google, Fitbit, or other provider
recommendation.

After every successful sync, manual save, and demo seed, the app recalculates
the persisted manual ACWR:

- `Acwr` uses only `CardioLoad`.

- The ratio is not read from Google. Acute load is the simple average for the
  current date and the six preceding calendar dates.
- Chronic load is the simple average for the current date and the 27 preceding
  calendar dates.
- The ratio is `acute / chronic`, rounded to two decimal places, only when all
  28 required daily Cardio Load values exist and chronic load is greater than
  zero. Otherwise the ratio is `null`.

The load chart uses the full locally stored manual history, groups daily Cardio
Load into Monday-through-Sunday weeks, plots the weekly sum against the weekly
target, and uses the latest available daily ACWR in each week.

The UI classifies a non-null ratio as **Undertraining** below 0.8, **Optimal
Zone** from 0.8 through 1.3, **Overreaching** above 1.3 through 1.5, or **High
Danger Zone** above 1.5. A missing ratio is displayed as `—`.

## CSV contract

`GET /api/metrics/export` emits one invariant-culture row per persisted day with
this header and column order:

```text
Date,RestingHR_bpm,HRV_RMSSD_ms,DailyVO2Max_ml_kg_min,RunVO2Max_ml_kg_min,ManualCardioLoad,ManualTargetLoad,ManualACWR,SleepEfficiency_pct,DeepSleep_min,RemSleep_min,Calories_kcal,Carbs_g,Fat_g,Protein_g,AlcoholEstimate_g
```

Null fields are emitted as empty CSV cells. The table and cards use an em dash
for the same missing values. `ManualTargetLoad` is the weekly target projected
onto each returned day in its Monday-through-Sunday week.

Sleep uses the documented `sleep`,
`SleepMetadata`, `SleepSummary`, `stagesSummary`, and `SleepStage` shapes.

## Removed legacy fields

The legacy Fitbit prototype displayed several nutrition and micronutrient fields that are not part of the initial tightened Google Health contract:

- fiber
- sodium
- potassium
- calcium
- iron

The migration archives those retired fields in `archived_legacy_metric_fields` before removing them from the active dashboard schema.

## Response parsing

`GoogleHealthApiClient` isolates Google JSON parsing from the application model. It handles:

- `dataPoints`, `dailyRollupDataPoints`, and `rollupDataPoints`
- `nextPageToken`
- the canonical `healthUserId` returned by `users/me/identity`
- nested union-style `value` payloads
- `YYYY-MM-DD` dates and civil date objects
- numeric strings or JSON numbers
- sleep sessions, main-sleep selection, civil end dates, and stage summaries
- API failures with sanitized error messages

No live Google calls are made in tests; fixture responses cover the supported payload shapes.
