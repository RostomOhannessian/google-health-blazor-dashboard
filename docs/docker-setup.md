# Docker setup

This guide runs Health Metrics in a container instead of natively with
`dotnet run`. It assumes you have completed (or at least read)
[Local development](local-development.md): that guide explains the stack,
the HTTPS-only OAuth callback, the SQLite database, and Serilog output that
this guide packages into an image. Complete
[Google Health setup](google-health-setup.md) only if you want live Google
sync instead of demo data.

## Why Docker needs two extra steps here

This app is deliberately a single-user, localhost-only dashboard: it rejects
any HTTP request whose source address is not loopback
(`src/HealthMetrics.Web/Security/LocalRequestPolicy.cs`), and its documented
OAuth callback is fixed at `https://localhost:5001/api/health/callback`
using a trusted HTTPS development certificate. Two container realities
collide with that:

1. **Published ports are not loopback.** When Docker forwards a published
   port (`-p 5001:5001`) into the container, the app sees the connection
   arriving from the Docker bridge network's gateway address, not
   `127.0.0.1`. Left alone, the loopback-only check would return
   `403 Forbidden` for every request, including from your own browser.
   `LocalRequestPolicy` now accepts an opt-in
   `LocalRequestPolicy:TrustedNetworks` CIDR allowlist for exactly this case;
   it is empty (loopback-only) unless you configure it, so normal, non-Docker
   use is unaffected.
2. **The HTTPS dev certificate lives in your Windows/macOS/Linux certificate
   store, not in the container.** Containers start from a clean Linux
   filesystem, so Kestrel has no certificate to serve HTTPS with until you
   export one and mount it in.

Both are handled by `docker-compose.yml`; you just need to provide the two
inputs described below in a local `.env` file.

## 1. Install Docker on the host machine

### Windows

1. Install [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/).
   Use the WSL2 backend when prompted (the current default).
2. Start Docker Desktop and wait for it to report it is running.
3. Verify from PowerShell:

   ```powershell
   docker version
   docker compose version
   ```

   Both commands should print a `Client` and `Server` section. If only
   `Client` appears, Docker Desktop is still starting; wait and retry.

### macOS

1. Install [Docker Desktop for Mac](https://www.docker.com/products/docker-desktop/).
2. Start Docker Desktop from Applications and wait for it to report it is
   running.
3. Verify from a terminal:

   ```bash
   docker version
   docker compose version
   ```

### Linux

1. Install Docker Engine and the Compose plugin using your distribution's
   instructions from the
   [official Docker Engine install guide](https://docs.docker.com/engine/install/).
2. Add your user to the `docker` group so you do not need `sudo` for every
   command, then log out and back in:

   ```bash
   sudo usermod -aG docker $USER
   ```

3. Verify:

   ```bash
   docker version
   docker compose version
   ```

## 2. Clone the repository

```powershell
git clone <repository-url>
Set-Location .\google-health-blazor-dashboard
```

```bash
git clone <repository-url>
cd google-health-blazor-dashboard
```

## 3. Export the HTTPS development certificate for the container

This reuses the same trusted certificate `dotnet dev-certs https --trust`
already created for native runs; it just exports it to a password-protected
`.pfx` file so it can be mounted read-only into the container. Pick any
password; it never leaves your machine.

### Windows (PowerShell)

```powershell
$certDir = "$env:USERPROFILE\.aspnet\https-docker"
New-Item -ItemType Directory -Force -Path $certDir | Out-Null
dotnet dev-certs https -ep "$certDir\aspnetcore-dev-cert.pfx" -p "<choose-a-password>" --trust
```

### macOS/Linux

```bash
certDir="$HOME/.aspnet/https-docker"
mkdir -p "$certDir"
dotnet dev-certs https -ep "$certDir/aspnetcore-dev-cert.pfx" -p "<choose-a-password>" --trust
```

Approve the trust prompt if one appears. Keep the password; it goes into
`.env` next.

## 4. Create your `.env` file

Copy the tracked template and fill in the two required values:

```powershell
Copy-Item .env.example .env
```

```bash
cp .env.example .env
```

Edit `.env`:

```text
DEV_CERT_DIR=C:/Users/<you>/.aspnet/https-docker
DEV_CERT_PASSWORD=<the password you chose above>
```

Use forward slashes in `DEV_CERT_DIR` even on Windows; Docker Desktop's bind
mounts expect them. On macOS/Linux, use the absolute path from step 3, e.g.
`/home/<you>/.aspnet/https-docker`.

`.env` is already covered by `.gitignore` and is never committed. Leave the
`GoogleHealthApi__ClientId`/`ClientSecret` lines commented out for demo mode,
or see step 6 to enable live sync.

## 5. Build and run

```powershell
docker compose up -d --build
```

Watch it come up:

```powershell
docker compose logs -f health-metrics
```

Expect `Health Metrics database migrations applied successfully.`,
`Local request policy will also trust 1 configured network(s) in addition to
loopback.`, and eventually `docker compose ps` reporting the container as
`healthy`. Then open `https://localhost:5001` and accept the certificate
warning if your browser has not already trusted it. Choose **Insert demo
data (30 days)** to explore the dashboard without live Google credentials.

## 6. Optional: enable live Google sync

Reuse the same OAuth client from
[Google Health setup](google-health-setup.md) - its redirect URI is already
fixed to `https://localhost:5001/api/health/callback`, which is set
unconditionally in `docker-compose.yml`, so no changes are needed in Google
Cloud Console. Add the two secrets to `.env`:

```text
GoogleHealthApi__ClientId=<client-id>
GoogleHealthApi__ClientSecret=<client-secret>
```

Then recreate the container so it picks up the new values:

```powershell
docker compose up -d
```

## 7. Optional: narrow the trusted network

`DOCKER_TRUSTED_NETWORK` defaults to `172.16.0.0/12`, which covers Docker's
entire standard private bridge address range so it works without any
per-machine tuning. To trust only this project's actual bridge gateway
instead, find it once the stack is running:

```powershell
docker network inspect google-health-blazor-dashboard_default --format "{{(index .IPAM.Config 0).Gateway}}"
```

Set `DOCKER_TRUSTED_NETWORK` in `.env` to that exact address with a `/32`
suffix (for example `172.18.0.1/32`), then recreate the container:

```powershell
docker compose up -d
```

## Operating it

| Task | Command |
|---|---|
| Build and start (or apply `.env`/compose changes) | `docker compose up -d --build` |
| Recreate without rebuilding | `docker compose up -d` |
| Tail logs | `docker compose logs -f health-metrics` |
| Check container/health status | `docker compose ps` |
| Stop, keep data | `docker compose down` |
| Stop and remove volumes (see Safe reset) | `docker compose down -v` |
| Open a shell in the container | `docker compose exec health-metrics bash` |
| Run the test suite | not containerized; use `dotnet test .\HealthMetrics.slnx` from step 8 of [Local development](local-development.md) |

## Data and persistence

Three named Docker volumes persist across `docker compose down`/`up` and
image rebuilds:

| Volume | Container path | Contents |
|---|---|---|
| `health-metrics-data` | `/app/data` | `health-metrics.db` (the same SQLite data a native run stores) |
| `health-metrics-logs` | `/app/logs` | Rolling Serilog JSON files |
| `health-metrics-dp-keys` | `/home/app/.aspnet/DataProtection-Keys` | The Data Protection key ring that decrypts stored Google OAuth tokens |

The key ring matters: if it were not persisted, every container recreation
would silently make previously stored access/refresh tokens undecryptable,
forcing a reconnect even though the database file itself still looked
intact. All three volumes are created and populated automatically the first
time you run `docker compose up`.

### Safe reset

To remove only local health data and the stored Google connection, equivalent
to deleting `health-metrics.db` in [Local development](local-development.md#safe-reset):

```powershell
docker compose down
docker volume rm health-metrics-data
docker compose up -d
```

To remove everything, including logs and the Data Protection key ring:

```powershell
docker compose down -v
docker compose up -d
```

## Security notes

- The trusted-network allowlist only ever widens the request gate to
  Docker's own private bridge address space (or the single gateway address
  you narrow it to in step 7) - never to the public internet. It is opt-in
  and empty by default outside of this compose file.
- Do not publish the container's ports on a shared or untrusted network
  interface, and do not widen `DOCKER_TRUSTED_NETWORK` beyond what your
  Docker networking actually requires. This remains a personal,
  localhost-facing dashboard - see `SECURITY.md`.
- Never commit `.env`; it can hold your certificate password and Google
  client secret.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `403 Forbidden` / "Health Metrics only accepts localhost requests" in logs | `DOCKER_TRUSTED_NETWORK` missing or wrong | Confirm `.env` has `DOCKER_TRUSTED_NETWORK` set and recreate with `docker compose up -d` |
| Container exits immediately mentioning `Kestrel` and certificates | `DEV_CERT_DIR`/`DEV_CERT_PASSWORD` missing, wrong path, or wrong password | Re-check step 3/4; `DEV_CERT_DIR` must point at the folder containing `aspnetcore-dev-cert.pfx`, using forward slashes |
| `docker compose up` fails with "Set DEV_CERT_DIR/DEV_CERT_PASSWORD in .env" | `.env` was not created or is missing a required value | Redo step 4; both variables are required, unlike the optional Google credentials |
| Browser shows a certificate warning | The exported cert is trusted by the OS but the browser has its own store, or you exported a different certificate than the one you trust | Rerun `dotnet dev-certs https --trust` first, then redo step 3 |
| Port 5000/5001 already in use | A native `dotnet run --launch-profile https` instance is also running | Stop one of the two; both use the same documented ports |
| `redirect_uri_mismatch` during Google sign-in | Google Cloud client's redirect URI does not exactly match | It must be exactly `https://localhost:5001/api/health/callback`, same as native runs |
| Dashboard has no data after `docker compose down -v` | That command intentionally removes the data volume | Expected; see Safe reset. Use `docker compose down` (no `-v`) to keep data |
| `docker compose ps` shows `unhealthy` | The app failed to start; check logs | `docker compose logs health-metrics` and look for the first exception, same as native startup failures |

## Official references

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Docker Engine install guide](https://docs.docker.com/engine/install/)
- [Docker Compose file reference](https://docs.docker.com/reference/compose-file/)
- [Hosting ASP.NET Core images with Docker over HTTPS](https://learn.microsoft.com/aspnet/core/security/docker-https)
- [.NET Docker images non-root user](https://learn.microsoft.com/dotnet/core/compatibility/containers/8.0/app-user)
- [Local development](local-development.md)
- [Google Health setup](google-health-setup.md)
- [Repository architecture](architecture.md)
