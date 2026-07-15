# Data Layer Schema — Plan Brief

> Full plan: `context/changes/data-layer-schema/plan.md`

## What & Why

Add PostgreSQL persistence to the .NET backend: EF Core schema for `users` and
`routes`, local + Azure connectivity, and an FK-cascade so account deletion
removes a user's routes atomically. This is F-02 on the v2 roadmap — a
foundation slice with no user-facing behavior of its own, but it unblocks
S-01 (magic-link auth), S-02 (save-route), S-03 (route library), and S-06
(account deletion).

## Starting Point

The backend has no database layer today — no EF Core, no Npgsql, no
`DbContext`. Auth (F-01) already issues Clerk-signed JWTs whose `sub` claim
gives a stable user identifier to key `users.id` off, with no separate
identity-sync step needed.

## Desired End State

`users`/`routes` tables exist (via EF Core migrations) on a local Docker
Postgres and an Azure Postgres Flexible Server; the backend connects via
`AppDbContext`; deleting a user cascades to delete their routes, verified by
an automated test against a real Postgres instance (not an InMemory stand-in).

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Azure provisioning timing | Include in this plan (Phase 3) | Server wasn't yet provisioned; plan should be self-contained | Plan |
| Migration application | Auto-migrate on `Development` startup; manual `dotnet ef database update` in prod | Matches the minimal-API "wire it in Program.cs" style locally; avoids startup-race risk in prod | Plan |
| DB test strategy | Testcontainers (real Postgres in Docker) | InMemory provider wouldn't enforce FKs/cascade/JSONB — exactly what this schema needs verified | Plan |
| Secrets management | User Secrets (dev) + App Service settings (prod) | Matches F-01's Clerk secrets precedent, zero new infra | Plan |
| Routes table columns | `name`, `tags`, `distance_km`, `created_at`, `user_id` FK | Lets S-03's library list query summary fields without loading geometry | Plan |
| Cascade delete | DB-level `ON DELETE CASCADE` FK | Atomic, can't be bypassed by an app bug; matches S-06's roadmap assumption | Plan |
| `users` table shape | Minimal — `id` (Clerk sub) + `created_at` | No sync problem, no stale-data risk, minimal PII | Plan |
| Geometry storage | JSONB, GeoJSON `LineString` shape | Directly consumable by MapLibre GL on the frontend | Plan |
| Postgres version | 18 (current stable) | User asked for "latest stable"; research confirmed 18 is current, not 16/17 | Plan |

## Scope

**In scope:** `users`/`routes` schema + migrations, local Docker Postgres,
Azure Postgres Flexible Server provisioning, EF Core wiring, cascade-delete
FK, Testcontainers-based integration tests.

**Out of scope:** Any save/list/delete/share route API endpoints (S-01–S-06),
auto-name/tag business logic, PostGIS/spatial queries, automated prod
migration pipeline, Key Vault, email/profile caching on `users`.

## Architecture / Approach

Standard EF Core + Npgsql setup: `AppDbContext` with two entities, a value
converter for JSONB geometry, and a DB-level cascading FK. Local dev runs
against Docker Compose Postgres with auto-migrate on startup; tests spin up
their own ephemeral Postgres via Testcontainers (no CI workflow changes
needed — GH Actions runners already have Docker); production runs against
Azure Postgres Flexible Server with migrations applied manually via the EF
CLI.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Schema & local dev | `AppDbContext`, entities, migration, Docker Compose Postgres, auto-migrate in dev | JSONB value-converter mapping subtlety (documented in plan) |
| 2. DB-backed tests | Testcontainers fixture, cascade-delete + JSONB round-trip tests | WebApplicationFactory's environment isn't guaranteed `Development` — fixture must migrate explicitly |
| 3. Azure provisioning | Flexible Server, firewall, App Service settings, prod migration applied | Manual CLI steps; no automated rollback if a step is missed |

**Prerequisites:** None — no dependency on F-01 beyond reusing its
secrets-management pattern.
**Estimated effort:** ~2-3 sessions across 3 phases.

## Open Risks & Assumptions

- Production migrations are manual (`dotnet ef database update`) for now —
  every future migration needs a human to run this against Azure; automating
  it via CI/CD was considered and explicitly deferred.
- Azure Postgres Flexible Server resource group/region/SKU specifics are
  decided during Phase 3 implementation, not fixed in this plan.

## Success Criteria (Summary)

- `dotnet build` and `dotnet test` pass locally and in CI with the new DB
  layer, no CI workflow file changes needed
- Deleting a user row removes their routes automatically (verified by test
  against real Postgres)
- The deployed backend connects to Azure Postgres in production; `/health`
  still returns 200 after redeploy
