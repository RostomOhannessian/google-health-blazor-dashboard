# Fitbit Metrics Demo Plan

## Problem
Build a public, recruiter-ready .NET skills demo that ingests Fitbit metrics via **Fitbit Web API + OAuth 2.0**, stores normalized data in a **local SQLite database**, and presents it through a **Blazor frontend** with production-style engineering practices.

## Proposed Approach
- Create a multi-project .NET solution with clear boundaries: Blazor UI, API/application layer, infrastructure/data layer, and shared contracts.
- Use ASP.NET Core + EF Core (SQLite) with migrations and domain-focused services for ingestion and querying.
- Implement OAuth 2.0 Authorization Code flow for Fitbit with secure token handling and refresh support.
- Add an ingest pipeline to collect and persist:
  - Resting heart rate
  - HRV (when exposed by Fitbit endpoints for the account)
  - VO2 Max / cardio fitness score (when available)
  - Calories consumed
  - Macronutrients and micronutrients (if Fitbit nutrition data provides them)
- Design schema to handle partial metric availability without data loss (nullable fields + source metadata).
- Build Blazor pages for OAuth connect status, sync controls, and metric dashboards with clean UI and explanatory labels suitable for portfolio review.

## Todos
1. **Bootstrapping solution architecture**  
   Create the .NET solution and projects (Blazor app, API/app core, infrastructure), wire dependency injection, configuration, logging, and baseline conventions.
2. **Designing persistence model and EF Core setup**  
   Define entities/value objects for user connection, tokens, daily aggregates, and metric records; configure SQLite context and initial migration.
3. **Implementing Fitbit OAuth and API client**  
   Add typed HttpClient, OAuth callback/token refresh flow, options validation, and secure token storage practices appropriate for a local demo.
4. **Building ingestion and mapping services**  
   Implement service(s) to fetch Fitbit metrics, map endpoint payloads into normalized entities, and persist idempotently.
5. **Exposing backend endpoints**  
   Add API endpoints for auth start/callback, sync execution/status, and metric retrieval for frontend charts/cards.
6. **Creating Blazor recruiter-facing UI**  
   Implement pages/components for connect/disconnect, sync controls, metric cards/charts, and data availability messaging.
7. **Hardening for demo quality**  
   Add focused tests for core mapping/ingestion behavior, API error paths, and basic docs/config examples for public GitHub usage.

## Notes and Considerations
- Fitbit data coverage varies by account/device and permissions; the app should clearly distinguish **missing by design** vs **not yet synced**.
- For a public repo, secrets remain out of source; use local user-secrets/environment variables and sample config templates.
- Start with a single-user local demo flow, but keep schema/service abstractions ready for future multi-user extension.

## Milestone Update (Completed)
- Scaffolded full solution/projects (`Application`, `Infrastructure`, `Web`, `Tests`) and wired references.
- Implemented EF Core SQLite persistence with migrations and uniqueness constraints for daily snapshots.
- Implemented Fitbit OAuth and token refresh flow, typed API client calls, and sync pipeline for requested metrics.
- Added backend API routes for connect/callback/status/sync/metrics.
- Built Blazor dashboard UI for connection state, sync actions, and metric table.
- Added persistence-focused automated test and public-facing README with secure local setup instructions.
