# Google Health data contract

The dashboard intentionally exposes only fields with a Google Health API source in the current integration.

## Active fields

| Domain property | Google data type | Method | Notes |
|---|---|---|---|
| `RestingHeartRateBpm` | `daily-resting-heart-rate` | `list` | Daily value in bpm. |
| `HrvRmssdMilliseconds` | `daily-heart-rate-variability` | `list` | Daily RMSSD value in milliseconds when available. |
| `RunVo2MaxMlKgMin` | `run-vo2-max` | `dailyRollUp` | Average daily rollup value. |
| `ConsumedCaloriesKcal` | `nutrition-log` | `dailyRollUp` | Energy rollup in kcal. |
| `CarbohydratesGrams` | `nutrition-log` | `dailyRollUp` | Total carbohydrate rollup in grams. |
| `FatGrams` | `nutrition-log` | `dailyRollUp` | Total fat rollup in grams. |
| `ProteinGrams` | `nutrition-log` | `dailyRollUp` | Nutrient rollup for `PROTEIN`. |

## Date and range behavior

- The app syncs 1-90 days at a time.
- Google Health daily rollups use closed-open civil-date ranges aligned to the requested days.
- Rollup requests are chunked in 14-day windows to stay inside stricter Google Health range limits for affected data types.
- Missing data points are represented as `null`, not as `0`.

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
- API failures with sanitized error messages

No live Google calls are made in tests; fixture responses cover the supported payload shapes.
