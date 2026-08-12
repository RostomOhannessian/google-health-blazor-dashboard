# syntax=docker/dockerfile:1

# ---- Build stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy the repo-wide MSBuild props and just the project files first so
# `dotnet restore` is cached in its own layer and only reruns when a
# .csproj (or Directory.Build.props) actually changes.
COPY Directory.Build.props ./
COPY src/HealthMetrics.Application/HealthMetrics.Application.csproj src/HealthMetrics.Application/
COPY src/HealthMetrics.Infrastructure/HealthMetrics.Infrastructure.csproj src/HealthMetrics.Infrastructure/
COPY src/HealthMetrics.Web/HealthMetrics.Web.csproj src/HealthMetrics.Web/
RUN dotnet restore src/HealthMetrics.Web/HealthMetrics.Web.csproj

# Now copy the rest of the referenced projects' source and publish.
# The test project is intentionally never copied into the image.
COPY src/HealthMetrics.Application/ src/HealthMetrics.Application/
COPY src/HealthMetrics.Infrastructure/ src/HealthMetrics.Infrastructure/
COPY src/HealthMetrics.Web/ src/HealthMetrics.Web/
RUN dotnet publish src/HealthMetrics.Web/HealthMetrics.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- Runtime stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl backs the HEALTHCHECK below; the base image does not include it.
# /app/data and /app/logs are pre-created and owned by the built-in non-root
# "app" user ($APP_UID) so named volumes mounted there inherit writable
# ownership instead of defaulting to root. /https is the mount point for a
# host-exported HTTPS dev certificate (see docs/docker-setup.md).
# The Data Protection key ring (used to encrypt stored Google OAuth tokens)
# defaults to /home/app/.aspnet/DataProtection-Keys; it must also be a
# volume, or every container recreation silently invalidates already-stored
# tokens and forces a reconnect.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /app/logs /https /home/app/.aspnet/DataProtection-Keys \
    && chown -R $APP_UID:$APP_UID /app/data /app/logs /https /home/app

COPY --from=build /app/publish .

USER $APP_UID

# Matches the repository's documented https://localhost:5001 (+ http
# companion on 5000) profile used everywhere in the docs and Google OAuth
# client configuration. The SQLite database lives on the /app/data volume so
# it survives container recreation; appsettings.json's relative "logs/" path
# resolves under WORKDIR (/app/logs), which is also a volume.
ENV ASPNETCORE_URLS=https://+:5001;http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__HealthMetricsDb="Data Source=/app/data/health-metrics.db"

EXPOSE 5000 5001

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --insecure https://localhost:5001/api/health/status || exit 1

ENTRYPOINT ["dotnet", "HealthMetrics.Web.dll"]
