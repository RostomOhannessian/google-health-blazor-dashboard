# Local development

This guide gets a developer who knows C# but has not used this stack before
from a clean checkout to a working local dashboard. It covers the repository
tools and operating model, not C# syntax or IDE navigation.

## What you are running

* **.NET 10 SDK and runtime**: the SDK compiles, restores, tests, and launches
  the application; the runtime executes the compiled web host. Install the
  SDK, which includes the matching runtime.
* **ASP.NET Core**: the web host supplies HTTPS, middleware, dependency
  injection, configuration, static files, and minimal API endpoints.
* **Blazor Interactive Server**: Razor components render HTML on the server
  and then maintain a server circuit for button clicks and live UI updates.
* **Razor components and CSS isolation**: `.razor` files contain markup and
  component code. A neighboring `.razor.css` file is compiled into a
  fingerprinted stylesheet whose selectors and rendered markup receive the
  same scope attribute.
* **Dependency injection and options**: services are registered in
  `Program.cs` and `ServiceCollectionExtensions`. Configuration sections are
  bound to validated options classes. The tracked `appsettings.json` contains
  non-secret placeholders, so the default configuration passes validation and
  the dashboard and demo mode can start without Google credentials. Replace
  those placeholders with real OAuth client values only when using live
  Connect and sync.
* **EF Core and migrations**: EF Core maps the models to tables, and the
  checked-in migrations describe schema changes. The host calls `Migrate()` at
  startup; no separate database server is needed.
* **SQLite**: `health-metrics.db` is a file in the web project's content root.
  Development tests use a separate database name or an in-memory SQLite
  connection.
* **User-secrets**: the .NET Secret Manager stores local credentials outside
  the repository. `UserSecretsId` is stable in the web project, so commands
  work from every machine.
* **HTTPS development certificates**: Kestrel uses the local ASP.NET Core
  certificate. This repository deliberately registers and consistently uses
  `https://localhost:5001` for its OAuth callback. The HTTP-only profile cannot
  complete this app's callback; this is the repository's callback choice, not a
  claim that Google universally requires HTTPS callbacks on localhost.
* **Bootstrap**: the checked-in CSS and JavaScript provide responsive layout,
  cards, buttons, tables, accessibility states, and Bootstrap light/dark
  variables.
* **Chart.js**: the dashboard's `wwwroot/charts.js` creates the Heart & HRV and
  Manual Load & Training Strain views through JavaScript interop. Both views
  retain full local history, open on the selected newest day window, and expose
  a horizontal scrollbar for older dates. The load view renders Monday-starting
  weekly Cardio Load totals, a weekly target line, and the latest weekly ACWR.
  The main table likewise keeps full history behind a fixed four-week-height
  vertical viewport of about 32 rows and has an optional weekly-summary toggle.
  Both chart views recolor axes, grids, legends, and tooltips when the theme
  changes.
* **Serilog**: the host writes structured local console output and rolling
  JSON files under `logs/health-metrics-.log`.
* **xUnit**: tests cover models, persistence, Google Health fixtures, and HTTP
  endpoint behavior. The SQLite query tests use the real SQLite provider so
  unsupported SQL translation is caught.
* **Google Health REST/OAuth**: the app uses Google OAuth Authorization Code
  flow for consent and refresh tokens, then calls Google Health REST endpoints.
  See [Google Health setup](google-health-setup.md) for credentials.

## Solution and request flow

The solution is deliberately layered:

```text
HealthMetrics.Application  -> interfaces and domain models only
HealthMetrics.Infrastructure -> EF Core, SQLite, Google OAuth/REST, sync, DI
HealthMetrics.Web           -> Blazor components, static assets, minimal APIs
HealthMetrics.Tests         -> unit, integration, and provider-backed tests
```

The dependency direction is Web -> Infrastructure -> Application. Application
does not know about EF Core, HTTP, Google, or the UI.

For a browser request, ASP.NET Core serves `App.razor`, which loads Bootstrap,
global CSS, the generated scoped CSS bundle, Chart.js, and the Blazor script.
`Routes.razor` selects `MainLayout` and the page. The theme button in
`MainLayout` is static markup handled by native JavaScript in `wwwroot/theme.js`
and local storage; clicking it does not require an Interactive Server circuit.
The Home page explicitly uses `@rendermode InteractiveServer`, so its buttons,
data loading, sync operations, and chart interop run through a server circuit.

The Google path is:

1. `/api/health/connect` creates a short-lived, one-time state value.
2. The browser goes to Google's authorization page.
3. Google returns an authorization code to
   `https://localhost:5001/api/health/callback`.
4. The callback validates state, exchanges the code, fetches the Google user
   identity, and encrypts tokens with ASP.NET Core Data Protection.
5. A sync obtains a valid access token, requests the configured date range,
   merges `(UserKey, MetricDate)` rows, recalculates complete-window ACWR values
   from local manual Cardio Load history, and records sync
   history.
6. Metric queries read only the local user and the dashboard renders the
   returned snapshots.

## Install prerequisites

Install the **.NET 10 SDK** from the official
[.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0).
Verify the installation:

```text
dotnet --info
```

Expected output includes an SDK version beginning with `10.0` and a matching
`Microsoft.AspNetCore.App` 10.0 runtime. If the command is not found, restart
the terminal after installation and ensure the .NET installation directory is
on `PATH`.

### Windows

Use the x64 .NET 10 SDK installer. In PowerShell, trust the development
certificate:

```powershell
dotnet dev-certs https --trust
```

Approve the Windows trust prompt. The repository uses Windows paths and
PowerShell examples below.

### macOS

Install the SDK package or Homebrew's current .NET 10 SDK. Run:

```bash
dotnet dev-certs https --trust
```

Approve the Keychain prompt. If a browser still warns, remove stale
ASP.NET Core certificates from Keychain Access and repeat the commands.

### Linux

Install the SDK using the package instructions for the distribution at the
[official .NET Linux guide](https://learn.microsoft.com/dotnet/core/install/linux).
Run:

```bash
dotnet dev-certs https --trust
```

Some distributions do not provide a browser trust store automatically. The
command prints the certificate location or export instructions; install the
certificate in the browser/OS trust store used by your browser. Running HTTP
only is not a substitute for this app's Google OAuth flow because its
registered callback is HTTPS.

### Certificate troubleshooting

Use `--clean` only when a stale or corrupt development certificate remains
after the normal `--trust` step:

```text
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

Warning: `--clean` removes ASP.NET Core development certificates used by other
projects on this machine. It is not a normal first setup step.

## Clone, restore, build, and test

From the repository root:

```powershell
git clone <repository-url>
Set-Location .\google-health-blazor-dashboard
dotnet restore .\HealthMetrics.slnx
dotnet tool restore
dotnet build .\HealthMetrics.slnx
dotnet test .\HealthMetrics.slnx
```

On macOS/Linux, use:

```bash
git clone <repository-url>
cd google-health-blazor-dashboard
dotnet restore ./HealthMetrics.slnx
dotnet tool restore
dotnet build ./HealthMetrics.slnx
dotnet test ./HealthMetrics.slnx
```

Expected results are a successful restore, `Build succeeded`, and a test
summary with zero failed tests. Build output is under each project's `bin`
directory; intermediate files are under `obj`. Neither belongs in a commit.

Run the normal profile:

```powershell
dotnet run --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj --launch-profile https
```

```bash
dotnet run --project ./src/HealthMetrics.Web/HealthMetrics.Web.csproj --launch-profile https
```

Open `https://localhost:5001`. The profile also listens on
`http://localhost:5000`; ASP.NET Core redirects HTTP requests to HTTPS. Stop
the host with `Ctrl+C`.

## Configuration and local files

Configuration precedence, from lowest to highest, is:
`appsettings.json` < `appsettings.{Environment}.json` < optional untracked
`appsettings.Local.json` < user-secrets (in Development) < environment
variables < command-line arguments. Later providers override earlier ones. The
repository ignores `appsettings.Local.json`, but prefer user-secrets or
environment variables for credentials whenever available and do not store
secrets there unnecessarily. Never put secrets in tracked files.

The web project has a stable secrets ID. Set Google values with:

```powershell
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:ClientId" "<client-id>"
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:ClientSecret" "<client-secret>"
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:RedirectUri" "https://localhost:5001/api/health/callback"
```

Portable shells use the same commands with `./src/HealthMetrics.Web/...`.
Environment variables use double underscores for nested keys:

```text
GoogleHealthApi__ClientId
GoogleHealthApi__ClientSecret
GoogleHealthApi__RedirectUri
```

The database and logs are local personal data:

* `src/HealthMetrics.Web/health-metrics.db` is created/migrated on first run.
* `src/HealthMetrics.Web/health-metrics.dev.db` is used by the Development
  settings.
* `src/HealthMetrics.Web/logs/health-metrics-YYYYMMDD.log` contains structured
  operational logs.
* user-secrets are outside the repository; their exact platform-specific
  location is managed by the .NET SDK.

The browser stores only the explicit theme choice under
`healthmetrics-theme`. On first visit the OS `prefers-color-scheme` is used.
The light/dark button stores a deliberate choice and updates Chart.js without
discarding data.

## Common tasks

| Task | Command or action |
|---|---|
| See SDK/runtime details | `dotnet --info` |
| Restore packages | `dotnet restore .\HealthMetrics.slnx` |
| Build | `dotnet build .\HealthMetrics.slnx` |
| Run all tests | `dotnet test .\HealthMetrics.slnx` |
| Run one test class | `dotnet test .\tests\HealthMetrics.Tests --filter FullyQualifiedName~MetricQueryServiceTests` |
| Run the HTTPS app | `dotnet run --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj --launch-profile https` |
| Review secret configuration | Do not use `dotnet user-secrets list`: it prints all configured values, including `ClientSecret`. |
| Add a migration | `dotnet ef migrations add <Name> --project .\src\HealthMetrics.Infrastructure --startup-project .\src\HealthMetrics.Web` |
| Apply migrations manually | `dotnet ef database update --project .\src\HealthMetrics.Infrastructure --startup-project .\src\HealthMetrics.Web` |
| Try demo mode | Open the dashboard and choose **Insert demo data (30 days)** |
| Export metrics | Choose **Export CSV** on the dashboard |

Demo rows include deterministic manual Cardio Load and Monday-starting weekly
target values, sleep efficiency, deep/REM minutes, and enough history for the
latest ACWR values. Use the chart toggle to switch between heart/recovery
trends and weekly manual load; use YTD to view or sync from January 1; click
any snapshot header to sort, including the manual target, ACWR, and
sleep-efficiency columns. The CSV includes the manual source and locally
persisted derived values.

Do not add a migration for a query translation fix. The sync-history ordering
uses its generated identity intentionally and does not change the schema.

## Safe reset

Stop the app first. To remove only local data, delete the relevant database
file and rerun the app; startup recreates it from migrations. PowerShell:

```powershell
Remove-Item .\src\HealthMetrics.Web\health-metrics.dev.db -ErrorAction SilentlyContinue
Remove-Item .\src\HealthMetrics.Web\health-metrics.dev.db-shm -ErrorAction SilentlyContinue
Remove-Item .\src\HealthMetrics.Web\health-metrics.dev.db-wal -ErrorAction SilentlyContinue
Remove-Item .\src\HealthMetrics.Web\health-metrics.db -ErrorAction SilentlyContinue
Remove-Item .\src\HealthMetrics.Web\health-metrics.db-shm -ErrorAction SilentlyContinue
Remove-Item .\src\HealthMetrics.Web\health-metrics.db-wal -ErrorAction SilentlyContinue
```

macOS/Linux:

```bash
rm -f ./src/HealthMetrics.Web/health-metrics.dev.db \
  ./src/HealthMetrics.Web/health-metrics.dev.db-shm \
  ./src/HealthMetrics.Web/health-metrics.dev.db-wal \
  ./src/HealthMetrics.Web/health-metrics.db \
  ./src/HealthMetrics.Web/health-metrics.db-shm \
  ./src/HealthMetrics.Web/health-metrics.db-wal
```

This removes local health history and encrypted tokens. Reconnect Google after
the reset. Do not delete the checked-in `Persistence/Migrations` directory.
To reset all credentials for this project separately, run this exact
PowerShell command:

```powershell
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj clear
```

Portable shells use:

```bash
dotnet user-secrets --project ./src/HealthMetrics.Web/HealthMetrics.Web.csproj clear
```

This removes every user-secret for the web project; do not paste secret values
into tickets or logs.

## Troubleshooting

* **`dotnet` is not found**: install the SDK, restart the terminal, and rerun
  `dotnet --info`; an IDE runtime alone is not enough.
* **HTTPS certificate warning**: rerun `dotnet dev-certs https --trust` and
  confirm the browser trusts the certificate. On Linux, install it in the
  browser trust store manually if required.
* **Port 5001 is busy**: stop the other process or use a temporary local URL
  only for non-OAuth work. The Google client must still register the exact
  canonical callback.
* **Redirect URI mismatch**: use exactly
  `https://localhost:5001/api/health/callback` in Google Cloud, user-secrets,
  and the HTTPS launch profile. Scheme, port, path, and trailing slash matter.
* **Startup says Google options are invalid**: a configuration value is
  missing or blank; restore the tracked non-secret placeholders or set all
  three values with user-secrets, then restart the host. The placeholders are
  sufficient for startup but not for live Google Connect.
* **Database is locked**: stop all app instances, remove no files while a
  process is running, and retry. SQLite permits one writer at a time.
* **Scoped layout CSS is missing**: clean and rebuild, then inspect the root
  document's fingerprinted `*.styles.css` link in browser developer tools. A
  successful stylesheet response should contain scoped selectors such as
  `.page[...]`.
* **Blazor circuit disconnects**: check the terminal and rolling log file,
  confirm the HTTPS host is still running, then use the reconnect dialog.
* **No metric values**: an empty field can be normal; the Google account or
  device may not provide that data type for the selected dates. See the data
  contract.
* **Tests fail after a change**: run the smallest filtered test first, then
  `dotnet test .\HealthMetrics.slnx`; inspect the first failure rather than
  deleting migrations or package files.

For architecture and field-level details, see
[architecture](architecture.md) and the
[Google Health data contract](google-health-data-contract.md). For consent,
scopes, and production requirements, see
[Google Health setup](google-health-setup.md).
