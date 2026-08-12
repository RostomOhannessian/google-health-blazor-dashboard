# Security policy

## Supported use

This project is intended to run as a personal localhost-only dashboard. Do not
deploy it as a public internet-facing service without additional hardening,
secret management, and infrastructure controls.

There is no user authentication: the app assumes a single trusted operator and
treats "the request reached us from this machine" as the entire authorization
model. Everything below exists to keep that assumption true.

## Trust model

**Request gate.** By default the app rejects any HTTP request whose source
address is not loopback. Running under Docker (see `docs/docker-setup.md`)
additionally requires a `LocalRequestPolicy:TrustedNetworks` CIDR allowlist,
because a published container port is reached through the Docker bridge
gateway rather than loopback. The allowlist is empty unless configured, and
`docker-compose.yml` sets it to Docker's private bridge range so the container
works out of the box.

The allowlist cannot be widened to a public network. Entries are validated at
startup and any range that reaches outside private (RFC 1918), loopback,
link-local, or IPv6 unique-local space is dropped with a logged warning. A
value such as `0.0.0.0/0` or `172.0.0.0/8` is refused rather than honored.

**Host header.** `AllowedHosts` is restricted to `localhost`, `127.0.0.1`, and
`[::1]`. This blocks DNS rebinding, where an attacker-controlled hostname
resolves to `127.0.0.1` so a malicious page can read the local API as
same-origin. A wildcard here would defeat the loopback gate entirely.

**Port publishing.** `docker-compose.yml` publishes ports on `127.0.0.1` only.
Publishing on `0.0.0.0` would expose the dashboard to every host on your LAN,
where a machine inside the trusted bridge range could reach it unauthenticated.

**Secrets at rest.** Google OAuth access and refresh tokens are encrypted with
ASP.NET Core Data Protection before being written to SQLite. Note that the Data
Protection key ring itself is not encrypted at rest on Linux containers, so the
database and the key-ring volume together are equivalent to the tokens: treat
both as secret material. Client credentials belong in .NET user secrets or
environment variables, never in tracked configuration.

**Diagnostic logging.** `GoogleHealthHttpLogging:LogRequestBodies` and
`LogResponseBodies` are off by default in production configuration. When
enabled they write Google Health API payloads to disk. Token-bearing fields are
redacted, but health data itself is not, so only enable them for local
troubleshooting and treat the resulting logs as sensitive.

## Reporting a vulnerability

Please do **not** open a public GitHub issue for suspected security
vulnerabilities.

Instead:

1. Use GitHub private vulnerability reporting for this repository if it is
   enabled.
2. If private reporting is unavailable, contact the maintainer directly through
   GitHub and include enough detail to reproduce the issue safely.

Please include:

- A description of the issue and its impact.
- Reproduction steps or a minimal proof of concept.
- Any affected routes, configuration, or data-handling behavior.

You can expect an acknowledgment as soon as practical, followed by validation,
fix planning, and coordinated disclosure once a patch is ready.
