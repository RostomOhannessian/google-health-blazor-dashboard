# Architecture notes

## Project boundaries

```text
HealthMetrics.Application
  Interfaces and domain models. No EF Core, HTTP, OAuth, or UI dependencies.

HealthMetrics.Infrastructure
  EF Core persistence, Google OAuth, Google Health REST client, sync orchestration,
  background scheduling, demo data generation, and dependency injection.

HealthMetrics.Web
  Blazor Server UI and minimal API endpoints.

HealthMetrics.Tests
  Unit, integration, fixture, persistence, and endpoint tests.
```

## Authorization lifecycle

1. `/api/health/connect` creates a one-time state nonce and redirects to Google OAuth.
2. `/api/health/callback` validates state and exchanges the authorization code using `Google.Apis.Auth`.
3. The app calls Google Health identity and stores the Google user id.
4. Access and refresh tokens are encrypted with ASP.NET Core Data Protection.
5. Sync refreshes access tokens before expiry and preserves the previous refresh token when Google does not return a replacement.
6. Disconnect revokes remotely on a best-effort basis and always removes local credentials.

## Sync lifecycle

1. `GoogleHealthSyncService` asks `IHealthAuthorizationService` for a valid access token.
2. `GoogleHealthApiClient` fetches the selected date range by Google data type.
3. The client maps documented Google Health metrics and selects one sleep session
   per civil end date, preferring provider-marked main sleep and then longest
   duration.
4. Before requesting sleep, the service checks the persisted OAuth grant. Older
   connections without `googlehealth.sleep.readonly` skip only sleep, continue
   syncing already-authorized metrics, and expose reconnect guidance through
   connection status.
5. The service merges results into `daily_metric_snapshots` using
   `(UserKey, MetricDate)` idempotency and never replaces an existing provider
   value with a null from a partial response.
6. After the merge is saved, `AcwrCalculator` recalculates the persisted manual
   ratio from `CardioLoad`. The ratio clears when its complete 7/28-day window is
   unavailable. Sync never changes the manual Cardio Load or target fields.
7. `sync_history` records requested days, persisted days, duration, outcome, and
   sanitized errors.

## Dashboard and derived metrics

`MetricQueryService` returns local snapshots to the interactive Home component.
`ManualLoadEntryService` validates nullable user-entered manual Cardio Load and
weekly target amount, preserves synced fields, and recalculates only the manual
ratio. Weekly targets are associated with Monday-through-Sunday weeks and are
projected across the displayed days. The table exposes manual values and the
ratio with `—` fallbacks. Chart.js keeps Heart & HRV separate from the load view,
which sums daily manual Cardio Load by Monday-starting week and plots the weekly
target and latest available manual ratio on the right axis.
The CSV endpoint exports the persisted source and derived values, including
deep/REM minutes, with invariant-culture numeric formatting.

## Logging and observability

The app uses `Microsoft.Extensions.Logging` throughout application services and Serilog at the ASP.NET Core host boundary.

- Console logs are optimized for local development.
- Rolling JSON file logs are written under `logs/health-metrics-.log` for daily-use troubleshooting.
- API request logging is enabled for minimal API endpoints while static asset noise is kept at debug level.
- Google Health outbound HTTP calls log method, sanitized path, operation, data type, status code, elapsed time, content length, and data point count.
- Google Health request bodies are logged for rollup requests because they contain operational parameters like date range, time zone, and window size.
- Google Health response body logging is opt-in via `GoogleHealthHttpLogging:LogResponseBodies` because responses can contain personal health data. When enabled, bodies are Debug-level, truncated, and redacted.

Privacy boundaries:

- Never log OAuth authorization codes, access tokens, refresh tokens, client secrets, authorization headers, full authorization URLs, or page token values.
- Do not enable EF Core sensitive data logging by default.
- Prefer operational properties such as `RequestedDays`, `StartDate`, `EndDate`, `DataType`, `StatusCode`, `ElapsedMs`, and `Outcome` over raw payloads.

## Persistence migration

The direct cutover keeps useful metric history but intentionally does not reuse legacy credentials:

- `fitbit_connections` is dropped.
- `health_connections` is created empty for Google OAuth credentials.
- `Vo2MaxMlKgMin` is renamed to `RunVo2MaxMlKgMin`.
- retired nutrition/micronutrient fields are copied into `archived_legacy_metric_fields` before active-schema removal.

## Why REST + Google Auth library

The Google Health API is HTTP/JSON-friendly and the .NET ecosystem has a mature Google OAuth library. This project uses:

- `Google.Apis.Auth` for OAuth URL generation, code exchange, refresh, and revoke.
- typed `HttpClient` for Google Health API v4 REST calls.
- fixture-based tests instead of generated clients or live API calls.
