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
3. The service merges results into `daily_metric_snapshots` using `(UserKey, MetricDate)` idempotency.
4. `sync_history` records requested days, persisted days, duration, outcome, and sanitized errors.

## Logging and observability

The app uses `Microsoft.Extensions.Logging` throughout application services and Serilog at the ASP.NET Core host boundary.

- Console logs are optimized for local development.
- Rolling JSON file logs are written under `logs\health-metrics-.log` for daily-use troubleshooting.
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
