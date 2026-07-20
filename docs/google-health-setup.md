# Google Health first-time setup

This walkthrough configures a local Google Health connection for Health
Metrics. It assumes you can use a terminal but have not configured Google
OAuth before. Complete [Local development](local-development.md) first:
that guide installs .NET, trusts HTTPS, explains the database, and validates
the application without requiring Google credentials.

Google Cloud Console labels change occasionally. If a label differs, use the
nearest equivalent screen and confirm the official documentation linked below.
This application uses the Google Health API, not the legacy Fitbit Web API.

## The OAuth words in plain language

* A **Google Cloud project** owns API enablement and OAuth configuration.
* A **Web client** is the OAuth client registration used by this server.
* The **client ID** identifies the application; the **client secret** proves
  that the server is the registered client. Treat the secret like a password.
* A **redirect URI** (also called callback URI) is the exact URL Google may
  use to return the browser after consent.
* **Scopes** are named permissions. This app requests read-only permissions
  for the three Google Health areas it displays.
* **Consent** is the user's decision to grant those scopes.
* An **authorization code** is the short-lived value Google sends to the
  callback. The server exchanges it; it is not a data token.
* An **access token** authorizes API calls for a short time.
* A **refresh token** lets the server obtain new access tokens without asking
  the user every time. It is encrypted in the local database.
* A **test user** is an account allowed to use an OAuth app whose audience is
  still in Testing.
* **Publishing** moves an app out of testing restrictions. Restricted scopes
  can additionally require verification and a security review.

## Before opening Google Cloud

You need:

1. A Google account that can access the intended Cloud project.
2. The .NET 10 SDK and a trusted HTTPS development certificate.
3. A clean local run on `https://localhost:5001`.
4. A decision about whether the project is personal/testing only or will later
   be submitted for production verification.

The one callback used everywhere in this repository is:

```text
https://localhost:5001/api/health/callback
```

Do not substitute port 5000, an old 7193/5265 port, `http`, a trailing slash,
an IP address, or a different path.

## 1. Put Fitbit data in the test account

Google Health API exposes the Fitbit user's data; creating a Cloud project does
not create health data. Follow the first steps of Google's
[first API call codelab](https://developers.google.com/health/codelabs/make-your-first-api-call):

1. Install the Fitbit app from the Apple App Store or Google Play.
2. Open it and choose **Sign in with Google**.
3. Use the Google account that owns the Fitbit data, and plan to use that same
   account as this OAuth app's test user.
4. Sync a Fitbit device, or add sample data in the Fitbit app. For a simple
   test, the codelab manually logs a 15-minute Walk and then syncs the app.

The **Cloud project administrator** is the account that can create the project,
enable Google Health API, and configure OAuth. The **end-user/test account** is
the Google account whose Fitbit data is read after consent. They can be
different people: project administrator access does not grant access to the
administrator's Fitbit data, and the data-bearing end-user must be added under
Audience → Test users and used when signing in to this app. If they are the
same person, use that one account for both roles.

The app's supported metric values still depend on the data type, source, and
selected date range. Demo data remains available when live Fitbit data is not
ready.

## 2. Create or select a Cloud project

1. Open the [Google Cloud Console](https://console.cloud.google.com/).
2. Select the project picker at the top, then choose an existing project or
   create a new project.
3. Record the project name only for your own reference. Never put credentials
   in this repository.
4. Confirm that billing/organization policy does not prevent API enablement.

## 3. Enable the Google Health API

1. Open **APIs & Services → Library**.
2. Search for **Google Health API** (the product may be described as the
   Google Health API v4).
3. Open the API and choose **Enable**.
4. In **APIs & Services → Enabled APIs & services**, confirm it appears.

If the API cannot be found or enabled, the Cloud account may lack permission,
the product may not be available to the project/account, or an organization
policy may block it. Do not try to replace these scopes with unrelated
Fitbit or Google Fitness scopes; they are different APIs.

## 4. Configure Google Auth Platform

Google's current OAuth configuration is organized under **Google Auth
Platform**. Open **Google Auth Platform → Branding** (older projects may show
an **OAuth consent screen** entry under APIs & Services).

### Branding

Complete the required application information:

1. Choose the appropriate user type/audience if prompted.
2. Enter an application name recognizable to your test users.
3. Select a support email.
4. Add developer contact information when requested.
5. Save and continue.

Use a name and support address you control. Do not upload production branding
or claim an organization you do not represent.

### Audience

Open the **Audience** page:

1. Keep the app in **Testing** while developing locally.
2. Add the Google account you will use under **Test users**.
3. Save the audience settings.

The signed-in account must be listed as a test user before it can complete
consent for a testing app. Other Google accounts will commonly receive an
access-blocked or unverified-app message.

### Data Access

Open **Data Access** and add exactly these six scopes:

```text
openid
email
https://www.googleapis.com/auth/googlehealth.settings.readonly
https://www.googleapis.com/auth/googlehealth.health_metrics_and_measurements.readonly
https://www.googleapis.com/auth/googlehealth.activity_and_fitness.readonly
https://www.googleapis.com/auth/googlehealth.nutrition.readonly
```

Why each is present:

* `openid` and `email` identify the connected Google account for the local
  connection status card.
* `googlehealth.settings.readonly` reads the user's Google Health time zone,
  which is needed to request daily rollups in the correct civil-time window.
* `googlehealth.health_metrics_and_measurements.readonly` covers measurements
  such as resting heart rate and heart-rate variability.
* `googlehealth.activity_and_fitness.readonly` covers the activity/fitness
  data used for run VO2 Max.
* `googlehealth.nutrition.readonly` covers nutrition rollups for calories,
  carbohydrates, fat, and protein.

They are all read-only. Do not add a scope just to make a consent screen look
complete. Additional or restricted scopes can change verification and review
requirements. The configured scopes and the authorization request must stay
in agreement; disconnect/reconnect after changing them so Google can ask for
new consent.

### Clients

Open **Clients** and choose **Create client** (or **Create OAuth client**):

1. Select **Web application** as the application type.
2. Give it a descriptive name such as `Health Metrics local`.
3. Under **Authorized redirect URIs**, add exactly:

   ```text
   https://localhost:5001/api/health/callback
   ```

4. Create the client.
5. Copy the client ID and client secret into a password manager or directly
   into user-secrets. Do not commit either value.

The Google client does not need the HTTP companion port. OAuth uses the HTTPS
profile only.

## 5. Store credentials locally

From the repository root in PowerShell:

```powershell
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:ClientId" "<client-id>"
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:ClientSecret" "<client-secret>"
dotnet user-secrets --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj set "GoogleHealthApi:RedirectUri" "https://localhost:5001/api/health/callback"
```

macOS/Linux:

```bash
dotnet user-secrets --project ./src/HealthMetrics.Web/HealthMetrics.Web.csproj set "GoogleHealthApi:ClientId" "<client-id>"
dotnet user-secrets --project ./src/HealthMetrics.Web/HealthMetrics.Web.csproj set "GoogleHealthApi:ClientSecret" "<client-secret>"
dotnet user-secrets --project ./src/HealthMetrics.Web/HealthMetrics.Web.csproj set "GoogleHealthApi:RedirectUri" "https://localhost:5001/api/health/callback"
```

`<client-id>` and `<client-secret>` are placeholders; do not include angle
brackets when entering real values. Do not use `dotnet user-secrets list` to
check them: that command prints all configured values, including
`GoogleHealthApi:ClientSecret`.

An alternative for CI or a shell session is environment variables:

```text
GoogleHealthApi__ClientId=<client-id>
GoogleHealthApi__ClientSecret=<client-secret>
GoogleHealthApi__RedirectUri=https://localhost:5001/api/health/callback
```

Prefer a secret manager in shared environments. Never put the client secret,
authorization code, access token, refresh token, or a full authorization URL
in source control, screenshots, issue reports, or logs.

## 6. Run, connect, and sync

Start the canonical profile:

```powershell
dotnet run --project .\src\HealthMetrics.Web\HealthMetrics.Web.csproj --launch-profile https
```

Open `https://localhost:5001` and accept the trusted local certificate if
needed.

### Connect

1. Choose **Connect Google Health**.
2. Sign in with the test user added under Audience.
3. Read the consent screen and allow the three read-only scopes.
4. Google returns to the exact callback URI.
5. The app validates its one-time state value, exchanges the code, obtains the
   Google user identity, and stores encrypted tokens locally.

### Sync

After the dashboard says **Connected**, choose **Sync last 7 days** (or a
30/90-day range). The server requests supported Google Health data, merges
daily rows, and records a sync-history entry. A date with no provider value
is still allowed to display dashes. Google Health data availability depends
on the account, connected devices, source apps, date range, and granted
scopes; missing values are not automatically an application error.

### Demo data and export

If live consent is unavailable, choose **Insert demo data (30 days)** to
exercise cards, the chart, range buttons, sync-history display, and CSV export.
Demo data is synthetic and stays in the local database. The dashboard's
**Export CSV** link exports the selected range.

### Reconnect and disconnect

Choose **Reconnect** after changing scopes or when a refresh token is revoked.
Choose **Disconnect** to revoke remotely on a best-effort basis and always
remove local credentials. A database reset also removes the local encrypted
connection, so reconnect afterward.

## Testing mode and production

While the app is in **Testing**, Google refresh tokens can expire after about
seven days. A later sync may report `invalid_grant`; reconnecting is expected
during development. This is independent of the seven-day metric range.

Google also limits an unverified testing app to 100 test users. Testing mode
is appropriate for personal development and a small controlled group, not
for an unattended public service.

Before production:

1. Complete Branding, Audience, and Data Access accurately.
2. Publish the app when the intended audience and data handling are ready.
3. Follow Google's verification process for sensitive/restricted scopes.
4. Complete any required security assessment for restricted Google Health
   access.
5. Use a production HTTPS domain and production redirect URI rather than the
   localhost URI.
6. Review token encryption, access control, logging, retention, deletion, and
   incident response.

Do not publish merely to avoid a testing expiry; production authorization
creates obligations and may require review.

## Security and privacy

This app handles personal health data. Keep the Cloud project and client
credentials private. Use the smallest read-only scope set. Do not enable
response-body logging in shared environments: Google responses can contain
health information. The app intentionally avoids logging tokens, secrets,
authorization codes, authorization headers, full authorization URLs, and page
tokens. Protect the local database and rolling logs as personal data.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Browser says the certificate is unsafe | Dev certificate is missing/untrusted | Run `dotnet dev-certs https --trust`; on Linux import it into the browser trust store |
| App opens on 7193, 5265, or another port | An old launch profile/process is being used | Stop old processes and run `--launch-profile https`; use `https://localhost:5001` |
| `redirect_uri_mismatch` | Scheme, port, path, or trailing slash differs | Make Cloud Console, user-secrets, and the launch profile exactly `https://localhost:5001/api/health/callback` |
| `access_denied` or access blocked | Account is not a test user, or app is not approved | Add the signed-in account under Audience → Test users and save |
| Google Health API is unavailable | API not enabled or project permission/policy blocks it | Enable it under APIs & Services → Library and verify the selected project |
| A scope is missing from consent | Data Access and app configuration differ | Add the three exact scopes, then reconnect to request fresh consent |
| `invalid_client` | Wrong client type or copied credential | Use a Web application client and replace local values from the same client |
| `invalid_grant` during sync | Testing refresh token expired/revoked | Reconnect; testing refresh tokens commonly expire after about seven days |
| `invalid_state` after callback | Callback was delayed, duplicated, or opened directly | Start Connect again in the same browser; do not bookmark the callback |
| Callback returns to the dashboard but no connection exists | Code exchange or identity lookup failed | Inspect the terminal/log file for the sanitized error and reconnect after fixing credentials |
| Connected but all fields are dashes | Account/device lacks that data type or date | Try a date with known data; missing provider values are normal |
| Sync returns no days | Selected range has no Google records or access was not granted | Check consent scopes and source-device data; demo seed can validate the UI |
| Dashboard reports a SQLite translation error | Stale binaries or an unrelated query regression | Stop the app, rebuild, run the SQLite query tests, and inspect the first exception |
| Database is locked | Two hosts are using the same SQLite file | Stop every local host before retrying; do not delete an open database |
| Scoped CSS or layout is missing | Build assets are stale or the fingerprinted CSS failed | Clean/rebuild, inspect the root `*.styles.css` link, and request it directly |
| Reconnect dialog appears repeatedly | Server circuit or HTTPS host is unavailable | Check server logs, port availability, certificate trust, and browser network errors |
| User-secrets command says no project ID | The command targeted the wrong project | Use the `HealthMetrics.Web.csproj` path shown above; it contains `UserSecretsId` |

## Official references

* [Google Cloud Console](https://console.cloud.google.com/)
* [Google Health API home](https://developers.google.com/health)
* [Google Health API getting started](https://developers.google.com/health/get-started)
* [Google Health API setup](https://developers.google.com/health/setup)
* [Google Health API first-call codelab](https://developers.google.com/health/codelabs/make-your-first-api-call)
* [Google OAuth 2.0 for web server applications](https://developers.google.com/identity/protocols/oauth2/web-server)
* [Google Auth Platform configuration](https://support.google.com/cloud/answer/15549257)
* [OAuth consent screen and publishing](https://support.google.com/cloud/answer/15549945)
* [OAuth app verification](https://support.google.com/cloud/answer/13463073)
* [.NET local development guide](local-development.md)
* [Repository architecture](architecture.md)
* [Repository Google Health data contract](google-health-data-contract.md)
