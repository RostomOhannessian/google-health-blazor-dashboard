# Security policy

## Supported use

This project is intended to run as a personal localhost-only dashboard. Do not
deploy it as a public internet-facing service without additional hardening,
secret management, and infrastructure controls.

By default the app rejects any HTTP request whose source address is not
loopback. Running under Docker (see `docs/docker-setup.md`) requires an
opt-in `LocalRequestPolicy:TrustedNetworks` CIDR allowlist, because a
published container port is reached through the Docker bridge gateway rather
than loopback. This allowlist is empty unless explicitly configured, and it
is meant to trust only your own local Docker bridge network - never widen it
to a public interface or an untrusted network.

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
