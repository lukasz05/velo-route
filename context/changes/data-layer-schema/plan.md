# Data Layer Schema Implementation Plan

## Overview

Add PostgreSQL persistence to the .NET backend via EF Core: a `users` + `routes`
schema with migrations, local dev (Docker Compose) and Azure (Postgres Flexible
Server) connectivity, and an FK-cascade so account deletion removes a user's
routes atomically. This is F-02 on the v2 roadmap — a foundation slice. It does
**not** add any save/list/delete route API endpoints; those belong to S-01/S-02/S-03/S-06.

## Current State Analysis

The backend (`src/backend/VeloRoute/`) is a .NET 10 minimal API with no controllers
— all endpoints register directly in `Program.cs`. There is no database layer of
any kind today: no EF Core, no Npgsql, no `DbContext`. The only persistence-adjacent
dependency is `NetTopologySuite` (`VeloRoute.csproj`), used purely for in-memory
route geometry math in `Routing/`, not for storage.

Configuration follows an `IOptions<T>` pattern bound to named sections in
`appsettings.json` (see `ORS` and `Clerk` sections, `Program.cs:25-26` and
`:52-80`). Real secrets are empty strings in `appsettings.json` and filled via
User Secrets locally (`appsettings.Development.json` is gitignored — `.gitignore:53`)
and Azure App Service Application Settings in prod. This is the established
pattern for F-02's connection string too.

Auth (F-01, already merged) issues Clerk-signed JWTs; the validated principal
exposes the Clerk user ID via the `sub` claim (see `Program.cs:91-92`, the
`/auth/probe` endpoint). This is the natural foreign key for `routes.user_id` —
no separate signup/profile-sync step is needed.

Tests use a custom `VeloRouteWebApplicationFactory : WebApplicationFactory<Program>`
(`VeloRoute.Tests/Routing/TestInfrastructure.cs:76-134`) that swaps services in
`ConfigureWebHost` (e.g. `FakeOpenRouteServiceClient` for `IOpenRouteServiceClient`,
test JWT signing key when `useTestAuth: true`). The same swap pattern extends
naturally to point `AppDbContext` at an ephemeral Testcontainers Postgres instance.

CI (`​.github/workflows/backend.yml`) runs `dotnet test` on `ubuntu-latest`, which
has a working Docker daemon — Testcontainers works there with no workflow changes.

## Desired End State

- `users` (id = Clerk sub, created_at) and `routes` (id, user_id FK, name, tags,
  distance_km, geometry jsonb, created_at) tables exist, created via EF Core
  migrations, on both a local Docker Postgres and an Azure Postgres Flexible
  Server instance.
- The backend connects to Postgres via `AppDbContext` (Npgsql provider); in
  `Development` the app auto-applies pending migrations on startup.
- Deleting a `users` row cascades to delete that user's `routes` rows via a
  DB-level FK constraint (`ON DELETE CASCADE`) — verified by an automated test,
  not just asserted by design.
- `dotnet build` and `dotnet test` (including new Testcontainers-backed
  integration tests) pass locally and in CI with no workflow file changes.
- The deployed backend (`https://velo-route-api.azurewebsites.net`) connects to
  the Azure Postgres instance in production; `/health` still returns 200 after
  redeploy.

### Key Discoveries:

- `src/backend/VeloRoute/Program.cs:52-81` — existing `IOptions`/`AddJwtBearer`
  registration style to mirror for `AddDbContext`.
- `src/backend/VeloRoute/appsettings.json:1-16` — `ORS`/`Clerk` sections are the
  precedent for an empty-string `ConnectionStrings:Default` placeholder.
- `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:76-134` — the
  service-swap pattern in `VeloRouteWebApplicationFactory.ConfigureWebHost` to
  extend for a test-only DB connection string.
- `.gitignore:53-54` — `appsettings.Development.json` / `*.local.json` are
  already gitignored, so the local Postgres connection string never needs a new
  ignore rule.
- No `docker-compose.yml` exists in the repo yet — this plan introduces the
  first one, at the repo root (sibling to `src/`, matching the monorepo's
  no-root-package-manager layout).

## What We're NOT Doing

- No route save/list/delete/share API endpoints (S-01, S-02, S-03, S-04, S-05).
- No auto-name-generation or tag-editing business logic — `routes.name` and
  `routes.tags` columns exist so those slices don't need a schema migration,
  but nothing populates them yet.
- No PostGIS / spatial query support — roadmap already decided JSONB geometry
  storage is sufficient for v2 (no spatial queries in scope).
- No automated production migration pipeline (e.g. CI-driven `dotnet ef database
  update` on deploy) — migrations against Azure are applied manually via CLI in
  Phase 3. Automating this is a reasonable future improvement, out of scope here.
- No Azure Key Vault — connection strings go through User Secrets (dev) and App
  Service Application Settings (prod), matching F-01's precedent.
- No email/profile caching on the `users` row — Clerk remains the sole source
  of identity data beyond the row's own primary key.

## Implementation Approach

Three phases, each independently shippable: (1) schema + local dev connectivity,
(2) a real-Postgres test story via Testcontainers, (3) Azure provisioning + prod
wiring. This mirrors F-02's own roadmap "Unlocks" — S-01 needs the schema to
exist, not the Azure connection to be live, so schema-and-local-dev lands first
and is independently testable before cloud infra is touched.

## Critical Implementation Details

**Auto-migrate lifecycle**: `DbContext` is registered scoped, but `app.Services`
(available right after `builder.Build()`) is the *root* service provider — calling
`app.Services.GetRequiredService<AppDbContext>()` directly throws. The
`Database.Migrate()` call must go through an explicit scope:
```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
```

**Testcontainers fixture must migrate independently of `Program.cs`'s dev-gate**:
`WebApplicationFactory<Program>`'s hosting environment is not guaranteed to be
`Development`, so the Phase 1 auto-migrate block cannot be relied on to set up
the test database. The Phase 2 fixture calls `Database.Migrate()` itself,
explicitly, against the Testcontainers connection string, independent of
`IsDevelopment()`.

**JSONB geometry mapping**: map `Route.Geometry` (a `GeoJsonLineString` record)
with an EF Core value converter rather than `ComplexProperty(...).ToJson()` —
the latter's structural JSON mapping doesn't handle a jagged `double[][]`
coordinates array cleanly. A `HasConversion` pair (serialize/deserialize via
`System.Text.Json`) onto a `.HasColumnType("jsonb")` column is simpler and
sufficient since nothing needs to query *into* the coordinates from SQL:
```csharp
builder.Property(r => r.Geometry)
    .HasConversion(
        g => JsonSerializer.Serialize(g, (JsonSerializerOptions?)null),
        s => JsonSerializer.Deserialize<GeoJsonLineString>(s, (JsonSerializerOptions?)null)!)
    .HasColumnType("jsonb");
```

**App Service connection string naming**: Application Settings flatten nested
config keys with a double underscore — `ConnectionStrings__Default`, not
`ConnectionStrings:Default` — to bind to the same `ConnectionStrings:Default`
key ASP.NET Core config reads locally.

## Phase 1: Schema & Local Dev Connectivity

### Overview

Stand up Postgres locally via Docker Compose, add the EF Core + Npgsql
provider, define `User`/`Route` entities and `AppDbContext`, generate the
initial migration, and wire connection-string configuration + dev-time
auto-migration into `Program.cs`.

### Changes Required:

#### 1. Local Postgres

**File**: `docker-compose.yml` (new, repo root)

**Intent**: Give every contributor an identical local Postgres instance without
manual install.

**Contract**: One `postgres` service, image `postgres:18-alpine`, exposes
`5432:5432`, env `POSTGRES_DB=veloroute`, `POSTGRES_USER=veloroute`,
`POSTGRES_PASSWORD=veloroute`, a named volume for data persistence across
`docker compose down`/`up`.

#### 2. EF Core + Npgsql packages

**File**: `src/backend/VeloRoute/VeloRoute.csproj`

**Intent**: Add the Postgres EF Core provider and the design-time tooling
package migrations generation needs.

**Contract**: Add `PackageReference` for `Npgsql.EntityFrameworkCore.PostgreSQL`
(`10.0.2`) and `Microsoft.EntityFrameworkCore.Design` (latest `10.0.x` compatible
with the EF Core version Npgsql's provider pulls in transitively, currently
`>=10.0.4`).

#### 3. Entities

**File**: `src/backend/VeloRoute/Data/User.cs` (new)

**Intent**: Represent an authenticated account row, keyed by the Clerk `sub`
claim already validated by the JWT middleware — no separate identity sync step.

**Contract**: `public sealed record User(string Id, DateTimeOffset CreatedAt)` —
`Id` is the Clerk subject string (primary key, not auto-generated).

**File**: `src/backend/VeloRoute/Data/Route.cs` (new)

**Intent**: Represent a saved route row. Columns beyond geometry exist now so
S-02 (save-route) doesn't need its own migration.

**Contract**: `Id` (`Guid`, PK, DB-generated), `UserId` (`string`, FK →
`User.Id`), `Name` (`string`, not null — always populated by the future save
endpoint), `Tags` (`string[]?`, nullable Postgres native array — optional per
PRD FR-005), `DistanceKm` (`double`, not null), `Geometry`
(`GeoJsonLineString`, not null), `CreatedAt` (`DateTimeOffset`, not null,
DB default `now()`).

**File**: `src/backend/VeloRoute/Data/GeoJsonLineString.cs` (new)

**Intent**: Strongly-typed shape for the geometry JSONB payload, consumable
directly by MapLibre GL on the frontend without a wrapper transform.

**Contract**: `public sealed record GeoJsonLineString(string Type, double[][] Coordinates)`.

#### 4. DbContext

**File**: `src/backend/VeloRoute/Data/AppDbContext.cs` (new)

**Intent**: EF Core context exposing `Users` and `Routes`, with the FK cascade
and JSONB conversion configured in `OnModelCreating`.

**Contract**: `DbSet<User> Users`, `DbSet<Route> Routes`. `OnModelCreating`
configures: `Route.UserId` → `User.Id` FK with
`.OnDelete(DeleteBehavior.Cascade)`; `Route.Tags` mapped to a Postgres
`text[]` column; `Route.Geometry` mapped per the value converter in "Critical
Implementation Details" above.

#### 5. Configuration

**File**: `src/backend/VeloRoute/appsettings.json`

**Intent**: Placeholder connection string section, matching the empty-string
precedent already set by `ORS:ApiKey` and the `Clerk` section.

**Contract**: Add `"ConnectionStrings": { "Default": "" }`.

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Register `AppDbContext` against the configured connection string,
and auto-apply pending migrations on `Development` startup only.

**Contract**: `builder.Services.AddDbContext<AppDbContext>(opts =>
opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));` placed
alongside the existing service registrations; the migrate-on-startup block
(see Critical Implementation Details) placed immediately after `var app =
builder.Build();`, before the `if (app.Environment.IsDevelopment())` block
that maps `/auth/probe` (reuse the same `IsDevelopment()` guard).

#### 6. Initial migration

**File**: `src/backend/VeloRoute/Migrations/*` (generated)

**Intent**: Create the `users` and `routes` tables matching the entity shapes
above.

**Contract**: Generate via `dotnet ef migrations add InitialCreate --project
VeloRoute/VeloRoute.csproj --startup-project VeloRoute/VeloRoute.csproj` (run
from `src/backend/`; install `dotnet-ef` as a local tool first via `dotnet new
tool-manifest` + `dotnet tool install dotnet-ef` if no manifest exists yet).

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- `dotnet ef migrations add InitialCreate` completes without error and the
  generated migration builds cleanly
- App starts against the Docker Compose Postgres in `Development` and
  `GET /health` returns `{"status":"ok"}` (proves `AddDbContext` registration
  and the migrate-on-startup block don't throw at boot)

#### Manual Verification:

- `docker compose up -d` starts Postgres; `dotnet run` in `Development`
  auto-applies the migration
- Inspect the DB (`psql` or a GUI client): `users` and `routes` tables exist
  with the expected columns and types (`geometry` is `jsonb`, `tags` is
  `text[]`, the FK on `routes.user_id` shows `ON DELETE CASCADE`)

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Phase 2: DB-Backed Test Infrastructure

### Overview

Add a Testcontainers-based Postgres fixture so integration tests exercise the
real Npgsql provider and real constraint behavior (cascade delete, JSONB
round-trip) instead of an InMemory provider that would silently skip both.

### Changes Required:

#### 1. Testcontainers package

**File**: `src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`

**Intent**: Add the Postgres Testcontainers module.

**Contract**: Add `PackageReference` for `Testcontainers.PostgreSql` (`4.13.0`).

#### 2. Postgres test fixture

**File**: `src/backend/VeloRoute.Tests/Data/PostgresFixture.cs` (new)

**Intent**: Start one ephemeral Postgres container per test collection (not
per test class — a shared container is cheaper and there's no cross-test
state to isolate beyond what each test already resets), apply migrations
against it explicitly (see Critical Implementation Details — do not rely on
`Program.cs`'s dev-gated auto-migrate).

**Contract**: `PostgresFixture : IAsyncLifetime` wrapping a
`PostgreSqlBuilder().WithImage("postgres:18-alpine").Build()` container;
`InitializeAsync` starts the container then runs `Database.Migrate()` against
it via a throwaway `AppDbContext`; paired with a `[CollectionDefinition]` so
tests opt in via `ICollectionFixture<PostgresFixture>`.

#### 3. Extend the existing test factory

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: Let `VeloRouteWebApplicationFactory` point `AppDbContext` at the
Testcontainers connection string, following the same service-swap pattern
already used for `IOpenRouteServiceClient`.

**Contract**: Add an optional `string? dbConnectionString` constructor
parameter; when set, `ConfigureWebHost` removes the existing
`DbContextOptions<AppDbContext>` registration and re-adds it via
`UseNpgsql(dbConnectionString)`, mirroring the existing
`services.Remove(descriptor); services.AddSingleton(...)` pattern used for the
fake ORS client.

#### 4. Schema tests

**File**: `src/backend/VeloRoute.Tests/Data/UserRouteSchemaTests.cs` (new)

**Intent**: Verify the two behaviors the schema exists to guarantee: cascade
delete and JSONB round-trip fidelity — the exact things an InMemory provider
would not have caught.

**Contract**: Test cases: (a) inserting a `User` + a `Route` referencing it
persists both rows; (b) deleting the `User` row also removes the `Route` row
(cascade, no orphan); (c) a `GeoJsonLineString` written to `Geometry` and
re-read from a fresh `AppDbContext` instance deep-equals the original.

### Success Criteria:

#### Automated Verification:

- All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- New `UserRouteSchemaTests` cases pass: insert round-trip, cascade delete,
  geometry JSONB round-trip

#### Manual Verification:

- Push to a branch and confirm the existing `backend.yml` CI workflow passes
  unmodified — Testcontainers uses the Docker daemon already present on
  `ubuntu-latest` runners, no workflow file change needed

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Azure Provisioning & Production Wiring

### Overview

Provision the Azure Postgres Flexible Server, connect the deployed backend to
it, and apply the schema — completing F-02's roadmap outcome ("Postgres
deployed and reachable from the .NET backend").

### Changes Required:

#### 1. Azure Postgres Flexible Server

**Intent**: Provision the production database, matching the roadmap's
resolved decision (Azure Database for PostgreSQL Flexible Server).

**Contract**: Manual, via Azure CLI — `az postgres flexible-server create`
against the same resource group as the existing `velo-route-api` App Service
(see `backend-deploy` in `context/archive/`), PostgreSQL major version `18`,
smallest Burstable tier sufficient for the PRD's `data_volume: small` /
`qps: low` targets.

#### 2. Firewall rule

**Intent**: Allow the App Service to reach the Flexible Server.

**Contract**: Manual — `az postgres flexible-server firewall-rule create`
with the "Allow public access from Azure services" rule (`0.0.0.0`–`0.0.0.0`
convention), since the App Service is not currently VNet-integrated.

#### 3. App Service connection string

**Intent**: Wire the production connection string without touching source
control, matching F-01's Clerk secrets precedent.

**Contract**: Manual — `az webapp config appsettings set --name
velo-route-api --settings ConnectionStrings__Default="<value>"` (double
underscore — see Critical Implementation Details).

#### 4. Apply the migration to Azure

**Intent**: Create `users`/`routes` on the production database. Production
does not auto-migrate on startup (dev-only per the plan's design decision),
so this is a one-time manual step now and after each future migration.

**Contract**: Manual — `dotnet ef database update --project
VeloRoute/VeloRoute.csproj --startup-project VeloRoute/VeloRoute.csproj
--connection "<azure-connection-string>"`, run from a machine with network
access to the Flexible Server (may require temporarily widening the firewall
rule to the operator's IP, or running from within Azure Cloud Shell).

### Success Criteria:

#### Automated Verification:

- None — this phase is infrastructure provisioning, verified manually below.

#### Manual Verification:

- Azure Postgres Flexible Server is created and shows `Ready` state in the
  Azure Portal
- Firewall rule allows the App Service's outbound traffic
- `az webapp config appsettings list` shows `ConnectionStrings__Default` set
- `dotnet ef database update` against Azure completes without error; `users`
  and `routes` tables are visible via the Azure Portal query editor or `psql`
- After the next deploy (push to `main`), `https://velo-route-api.azurewebsites.net/health`
  still returns `{"status":"ok"}` — confirms the app boots successfully with
  the new `AddDbContext` registration pointed at the real Azure connection
  string, with no startup crash

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Entity/DbContext model configuration is exercised indirectly through the
  Phase 2 integration tests — there's no meaningful unit-level (non-DB) logic
  in this schema-only change to isolate.

### Integration Tests:

- Insert User + Route, verify persistence (Phase 2)
- Cascade delete: delete User, verify Route is gone (Phase 2)
- JSONB geometry round-trip fidelity (Phase 2)
- App boot with real DbContext registration against Docker Postgres (Phase 1,
  via `/health`)

### Manual Testing Steps:

1. `docker compose up -d`, `dotnet run` from `src/backend/VeloRoute`, confirm
   tables appear via `psql`
2. Delete a manually-inserted user row via `psql`, confirm the cascade removes
   its routes
3. After Phase 3, confirm the deployed app's `/health` still returns 200 and
   the Azure tables exist

## Performance Considerations

None expected at this scope — `target_scale` in the PRD frontmatter is `users:
medium`, `qps: low`, `data_volume: small`. No indexing beyond the PK/FK is
needed until S-03 (route library) defines its actual list-query shape.

## Migration Notes

This is a greenfield schema — no existing data to migrate. Future schema
changes (e.g. adding columns for S-05's public-sharing snapshot) will be
additive migrations on top of `InitialCreate`.

## References

- Roadmap: `context/foundation/roadmap.md` (F-02: Data layer schema)
- PRD: `context/foundation/prd-v2.md` (FR-004–FR-009, NFR "account deletion")
- Prior auth work: `context/archive/2026-07-04-auth-provider-scaffold/` (F-01,
  the `IOptions`/secrets-management precedent this plan follows)
- Existing test factory pattern: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Schema & Local Dev Connectivity

#### Automated

- [x] 1.1 Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- [x] 1.2 `dotnet ef migrations add InitialCreate` completes and builds cleanly
- [x] 1.3 App starts against Docker Compose Postgres in Development; `GET /health` returns 200

#### Manual

- [ ] 1.4 `docker compose up -d` + `dotnet run` auto-applies the migration
- [ ] 1.5 `users`/`routes` tables inspected via psql/GUI client match expected columns, types, and FK cascade

### Phase 2: DB-Backed Test Infrastructure

#### Automated

- [ ] 2.1 All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- [ ] 2.2 UserRouteSchemaTests: insert round-trip passes
- [ ] 2.3 UserRouteSchemaTests: cascade delete passes
- [ ] 2.4 UserRouteSchemaTests: geometry JSONB round-trip passes

#### Manual

- [ ] 2.5 CI (`backend.yml`) passes unmodified on a pushed branch

### Phase 3: Azure Provisioning & Production Wiring

#### Manual

- [ ] 3.1 Azure Postgres Flexible Server created, `Ready` state
- [ ] 3.2 Firewall rule allows App Service connectivity
- [ ] 3.3 `ConnectionStrings__Default` set in App Service Application Settings
- [ ] 3.4 `dotnet ef database update` against Azure succeeds; tables visible
- [ ] 3.5 Post-deploy `/health` still returns 200 against the live Azure connection
