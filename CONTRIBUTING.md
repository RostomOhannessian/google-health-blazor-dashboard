# Contributing

Thanks for your interest in improving Health Metrics.

## Before you open a pull request

1. Start from the latest `master`.
2. Keep changes focused and document any user-visible behavior changes.
3. Never commit secrets, local databases, or log files.

## Local validation

From the repository root:

```powershell
dotnet restore .\HealthMetrics.slnx
dotnet build .\HealthMetrics.slnx
dotnet test .\HealthMetrics.slnx
```

If you are changing live Google Health integration behavior, also verify the
relevant setup and operational notes in `docs/google-health-setup.md` and
`docs/local-development.md`.

## Configuration

- Keep OAuth credentials in .NET user-secrets or environment variables.
- Do not replace the tracked placeholder values in `appsettings.json` with real
  secrets.

## Pull requests

- Describe the problem and the chosen fix clearly.
- Include screenshots for UI changes when they help reviewers.
- Call out any follow-up work or known limitations explicitly.
