# Health Metrics

Health Metrics is a .NET 10 Blazor Interactive Server dashboard that stores
Google Health data in a local SQLite database. It supports Google OAuth,
range-based sync, demo data, CSV export, sync history, and a light/dark theme.
The dashboard tracks resting heart rate, HRV, VO2 Max, nutrition, Google Health
manually entered proprietary Cardio Load and target amounts, sleep efficiency,
deep/REM sleep, and a locally calculated acute-to-chronic workload ratio (ACWR)
for the manual load series.

The home page includes a manual Cardio Load entry form, sortable daily snapshot
columns, and a separate load chart. The chart shows manual Cardio Load bars, a
user-entered target line, and the manual ACWR on a right axis; Heart & HRV remains
a separate view. Missing values remain `—` rather than being converted to zero.
The ACWR appears only when the manual series has complete 7-day acute and 28-day
chronic windows.
See the [Google Health data contract](docs/google-health-data-contract.md) for
payload mappings and the CSV field contract.

## Start here

1. Install the .NET 10 SDK and trust the HTTPS development certificate.
2. Follow [Local development](docs/local-development.md) for the stack,
   commands, database, logs, and troubleshooting.
3. Follow [Google Health setup](docs/google-health-setup.md) only when you
   want live Google authorization and synchronization.

The documented HTTPS profile is:

```text
https://localhost:5001
```

The OAuth callback is:

```text
https://localhost:5001/api/health/callback
```

Run it from the repository root:

```powershell
dotnet run --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj --launch-profile https
```

Then open `https://localhost:5001`. The HTTP companion is `http://localhost:5000`
and redirects to HTTPS. The app rejects non-loopback requests at runtime because
it is a single-user local dashboard.

## Test

```powershell
dotnet test .\HealthMetrics.slnx
```

## Repository map

| Path | Purpose |
|---|---|
| `src/HealthMetrics.Application` | Provider-neutral models and interfaces |
| `src/HealthMetrics.Infrastructure` | EF Core/SQLite, Google OAuth and REST, sync, DI |
| `src/HealthMetrics.Web` | Blazor UI and minimal API endpoints |
| `tests/HealthMetrics.Tests` | Unit, provider-backed persistence, client, and endpoint tests |
| `docs/architecture.md` | Design boundaries and data flows |
| `docs/google-health-data-contract.md` | Google Health to dashboard mapping |
| `docs/local-development.md` | Local prerequisites, commands, files, and troubleshooting |
| `docs/google-health-setup.md` | Google Cloud, Fitbit data, OAuth, and live sync setup |

Secrets belong in .NET user-secrets or environment variables, never in tracked
JSON. The app creates and migrates its local database automatically.
