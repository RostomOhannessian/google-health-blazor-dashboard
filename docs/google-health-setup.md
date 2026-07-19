# Google Health setup

This app uses the **Google Health API** and Google OAuth 2.0. It does not use the legacy Fitbit OAuth endpoints.

## Google Cloud project

1. Open the [Google Cloud Console](https://console.cloud.google.com/).
2. Create or select a project for this app.
3. Enable the **Google Health API**.
4. Configure the OAuth consent screen.
5. Add yourself under **Test users** while the app is in testing mode.

## OAuth client

Create an OAuth client:

- **Application type:** Web application
- **Authorized redirect URI:** `https://localhost:5001/api/health/callback`

Store the client ID and secret locally:

```powershell
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:ClientId" "<client-id>"
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:ClientSecret" "<client-secret>"
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:RedirectUri" "https://localhost:5001/api/health/callback"
```

## Scopes

The active app uses read-only Google Health scopes:

```text
https://www.googleapis.com/auth/googlehealth.health_metrics_and_measurements.readonly
https://www.googleapis.com/auth/googlehealth.activity_and_fitness.readonly
https://www.googleapis.com/auth/googlehealth.nutrition.readonly
```

Only request additional scopes when a feature needs them. Google Health scopes are restricted, so a public production app can require verification and security review.

## Testing-mode refresh tokens

When the OAuth consent screen is in **Testing** mode, Google refresh tokens can expire after a short period. This is expected and can surface as `invalid_grant` during background sync. Move the consent screen to production before relying on unattended daily sync.

## Local run

```powershell
dotnet run --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj
```

Open `https://localhost:5001`, click **Connect Google Health**, grant consent, then sync a 7/30/90-day range.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `invalid_state` after callback | The OAuth state expired or the callback was opened outside the original browser flow. Start Connect again. |
| `invalid_grant` on sync | Refresh token expired/revoked, often from Google testing mode. Reconnect. |
| `insufficient_scope` | The Google Cloud OAuth client does not include one of the configured scopes. Add the scope and reconnect with consent. |
| Empty metric columns | The user's device/account did not provide that Google Health data type for the selected date. |

