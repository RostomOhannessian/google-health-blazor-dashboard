# Google Health data contract

The dashboard intentionally exposes only fields with a Google Health API source in the current integration.

## Active fields

| Domain property | Google data type | Method | Notes |
|---|---|---|---|
| `RestingHeartRateBpm` | `daily-resting-heart-rate` | `list` | Daily value in bpm. |
| `HrvRmssdMilliseconds` | `daily-heart-rate-variability` | `list` | Daily RMSSD value in milliseconds when available. |
| `DailyVo2MaxMlKgMin` | `daily-vo2-max` | `list` | Daily cardio-fitness VO2 Max in ml/kg/min. |
| `RunVo2MaxMlKgMin` | `run-vo2-max` | `dailyRollUp` | Average daily rollup value. |
| `CardioLoad` | `daily-cardio-load`, `cardio-load`, or `training-load` | `list` | Optional provider cardio/training load score. The client tries these candidate data types in order. |
| `TargetLoadMin` | `daily-target-load` or `target-load` | `list` | Lower bound from the provider target-load object or a direct min/recommended-min field. |
| `TargetLoadMax` | `daily-target-load` or `target-load` | `list` | Upper bound from the provider target-load object or a direct max/recommended-max field. |
| `SleepEfficiency` | `sleep` | `list` | Provider sleep efficiency, normalized to a percentage. When omitted, the client derives sleep minutes divided by sleep-period minutes. |
| `DeepSleepMinutes` | `sleep` | `list` | Minutes for the `DEEP` stage from `SleepSummary.stagesSummary` or stage intervals. |
| `RemSleepMinutes` | `sleep` | `list` | Minutes for the `REM` stage from `SleepSummary.stagesSummary` or stage intervals. |
| `ConsumedCaloriesKcal` | `nutrition-log` | `dailyRollUp` | Energy rollup in kcal. |
| `CarbohydratesGrams` | `nutrition-log` | `dailyRollUp` | Total carbohydrate rollup in grams. |
| `FatGrams` | `nutrition-log` | `dailyRollUp` | Total fat rollup in grams. |
| `ProteinGrams` | `nutrition-log` | `dailyRollUp` | Nutrient rollup for `PROTEIN`. |

## Date and range behavior

- The app syncs 1-90 days at a time.
- Google Health daily rollups use closed-open civil-date ranges aligned to the requested days.
- Rollup requests are chunked in 14-day windows to stay inside stricter Google Health range limits for affected data types.
- Missing data points are represented as `null`, not as `0`.
- Overnight sleep is assigned to the date of its civil end/wake time using
  `sleep.interval.civil_end_time`. For each date, a provider-marked main sleep
  session is preferred; if none is marked, the longest session is used.

## Local training-strain calculation

`Acwr` is not read from Google. After every successful sync (and after demo
seeding), the local service recalculates each persisted snapshot:

- Acute load is the simple average of cardio load for the current date and the
  six preceding calendar dates.
- Chronic load is the simple average for the current date and the 27 preceding
  calendar dates.
- The ratio is `acute / chronic`, rounded to two decimal places, only when all
  28 required daily cardio-load values exist and chronic load is greater than
  zero. Otherwise `Acwr` is `null`.

The UI classifies a non-null ratio as **Undertraining** below 0.8, **Optimal
Zone** from 0.8 through 1.3, **Overreaching** above 1.3 through 1.5, or **High
Danger Zone** above 1.5. A missing ratio is displayed as `—`.

## CSV contract

`GET /api/metrics/export` emits one invariant-culture row per persisted day with
this header and column order:

```text
Date,RestingHR_bpm,HRV_RMSSD_ms,DailyVO2Max_ml_kg_min,RunVO2Max_ml_kg_min,CardioLoad,TargetLoadMin,TargetLoadMax,ACWR,SleepEfficiency_pct,DeepSleep_min,RemSleep_min,Calories_kcal,Carbs_g,Fat_g,Protein_g
```

Null fields are emitted as empty CSV cells. The table and cards use an em dash
for the same missing values.

The public Google discovery document used by this integration does not
currently publish stable cardio-load, training-load, or target-load schemas.
Those data types are therefore optional flexible candidates; the client keeps
the field null when none is available. Unsupported candidate responses (400 or
404) advance to the next candidate; a candidate-specific 403 skips the optional
metric without failing confirmed metrics. Authentication failures, throttling,
and server errors still fail the fetch so operational problems are not hidden.
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
- optional cardio/target-load candidate data types
- sleep sessions, main-sleep selection, civil end dates, and stage summaries
- API failures with sanitized error messages

No live Google calls are made in tests; fixture responses cover the supported payload shapes.
