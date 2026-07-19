# Fitbit Metrics Demo (.NET + Blazor)

Recruiter-facing sample project that demonstrates a production-style .NET stack:
- **Blazor frontend**
- **ASP.NET Core backend APIs/services**
- **Entity Framework Core + SQLite local database**
- **Fitbit Web API OAuth 2.0 integration**

## Current tracked metrics
- Resting heart rate
- HRV (daily RMSSD, when Fitbit provides it)
- VO2 max / cardio score (when Fitbit provides it)
- Consumed calories
- Macronutrients (carbs, fat, protein, fiber)
- Micronutrients available from Fitbit nutrition summary (sodium and any additional exposed fields)

## Solution structure
- `src/FitbitMetrics.Web` — Blazor app + API endpoints
- `src/FitbitMetrics.Application` — domain models + service interfaces
- `src/FitbitMetrics.Infrastructure` — EF Core, Fitbit API client, OAuth and sync services
- `tests/FitbitMetrics.Tests` — focused persistence tests

## Local setup
1. Create a Fitbit app in the Fitbit developer portal.
2. Configure OAuth callback URL to `https://localhost:5001/api/fitbit/callback`.
3. Set secrets for the web project:

```powershell
dotnet user-secrets --project .\src\FitbitMetrics.Web\FitbitMetrics.Web.csproj set "FitbitApi:ClientId" "<your-client-id>"
dotnet user-secrets --project .\src\FitbitMetrics.Web\FitbitMetrics.Web.csproj set "FitbitApi:ClientSecret" "<your-client-secret>"
dotnet user-secrets --project .\src\FitbitMetrics.Web\FitbitMetrics.Web.csproj set "FitbitApi:RedirectUri" "https://localhost:5001/api/fitbit/callback"
```

4. Run the app:

```powershell
dotnet run --project .\src\FitbitMetrics.Web\FitbitMetrics.Web.csproj
```

5. Open the home page and click **Connect Fitbit**, then **Sync last 7 days**.

## Notes
- This demo uses a single local demo user (`demo-user`) by design.
- Missing metric fields are preserved as `null` when Fitbit does not provide data for that day/account/device.
