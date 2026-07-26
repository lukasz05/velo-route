# VeloRoute — Copilot Instructions

## Key conventions

### Next.js (this is Next.js 15 / React 19)
`src/frontend/AGENTS.md` warns that this version has breaking changes from older training data. Before writing any Next.js code, check `node_modules/next/dist/docs/` for current API behaviour.

- Import alias `@/*` maps to `src/frontend/src/*`.
- Tailwind v4 — config is in `postcss.config.mjs` (no `tailwind.config.js`); utility classes work the same but the config surface changed.

### .NET backend
- Minimal API pattern: endpoints are registered directly in `Program.cs`, not in controller classes.
- Nullable reference types enabled (`<Nullable>enable</Nullable>`). Don't introduce `#nullable disable` suppressions.
- `RootNamespace` is `VeloRoute`.

### Monorepo
- Each project manages its own dependencies independently (`src/frontend/package.json`, `src/backend/*.csproj`). There is no root-level `package.json`.
- Environment files (`.env`, `.env.*`) are gitignored. Use `.env.example` for documenting required variables.
- `context/` is never modified by scaffold tooling or automated processes — it is the human/agent knowledge base for this project.

---

Free road-cycling loop-route planner. User enters a start point and km range; the app returns a loop route on an interactive map with GPX export — no account needed for that core flow. v2 layers on optional accounts (Clerk email magic link) for a personal route library: save, view, delete, and share a route via a public unauthenticated link. Full PRD (current, v2): `context/foundation/prd-v2.md` (`prd.md` is the frozen v1 doc — do not treat it as current scope).

## Repository layout

```
src/
  frontend/   Next.js 15 (React 19, TypeScript, Tailwind v4, App Router)
  backend/    ASP.NET Core (.NET 10, minimal API) — Program.cs, Routing/, Data/, Migrations/, Auth/
context/
  foundation/
    frontend/tech-stack.md
    backend/tech-stack.md
    prd.md       # v1, frozen
    prd-v2.md    # current
    roadmap.md
  changes/    per-change work logs
.gitignore    monorepo-wide (root only)
docker-compose.yml   local Postgres (`docker compose up -d`)
```

## Dev commands

**Frontend** (`src/frontend/`)
```bash
npm run dev        # http://localhost:3000
npm run build
npm run lint       # eslint
```

**Backend** (`src/backend/`)
```bash
dotnet run                         # http://localhost:5098
dotnet run --launch-profile https  # https://localhost:7125
dotnet build
dotnet test
```

Swagger UI (development only): `http://localhost:5098/swagger`

Backend test runner: xUnit 2.9.3, bootstrapped in `src/backend/VeloRoute.Tests/`. Run with `dotnet test` from `src/backend/` (needs Postgres — Testcontainers-backed; `docker compose up -d` or a running Docker daemon). Frontend test runner: Vitest 4 + React Testing Library, co-located `*.test.ts(x)`. Run with `npm test` from `src/frontend/`.

## Architecture

The two projects are independently runnable. In production, the Next.js frontend calls the .NET backend API over HTTP; no shared runtime or in-process communication.

- **Frontend** (`src/frontend/src/app/`): Next.js App Router. All routes are under `src/app/`. Client components are opted in with `"use client"`. `@/app/api/**/route.ts` files proxy to the backend, relaying the Clerk-issued bearer token where the underlying endpoint requires auth.
- **Backend** (`src/backend/`): .NET 10 minimal API style (`Program.cs`, no controllers folder). OpenAPI/Swagger is registered via `builder.Services.AddOpenApi()` and mapped at `/openapi/v1.json` in development. `Data/` holds EF Core entities + `AppDbContext`; `Migrations/` the generated EF Core migrations; `Auth/` shared auth helpers (e.g. `ClaimsPrincipalExtensions.GetSub()`).
- **Data flow**: frontend → HTTP → backend → OpenRouteService (ORS) HTTP API for route generation (still fully anonymous, nothing persisted). For account-gated features (save/library/delete/share), the backend also validates the Clerk-issued JWT and reads/writes Postgres via EF Core.

## Workflow conventions

- **Branch per change**: before running `/10x-implement` for any change, create a branch named after the change-id (`git checkout -b <change-id>`). Do this automatically without being asked.
- **Keep docs accurate**: before every commit, check whether any fact in this file or in `context/` has become stale — version numbers, test counts, integration status, namespace names, "not yet implemented" claims. Update them in the same commit as the code change that made them stale. Never let a commit leave the docs describing a state that no longer exists.

## Current scope (v2 in progress)

**v1 (shipped)**: anonymous start-point search, km range input, single loop-route proposal, interactive map display, GPX export, mobile-responsive UI. Still fully unauthenticated — no v2 feature gates this path.

**v2 done**: Clerk email-magic-link auth (sign up/in/out); save a generated route to a personal library; view the library and open a saved route (map + GPX); delete a saved route; share a saved route via a public unauthenticated link (live read-through, revocable, dies if the route is deleted).

**v2 remaining** (see `context/foundation/roadmap.md` for current status): account self-serve deletion, OSM-driven routing quality improvements (scenic/low-traffic preference, cyclist POIs).

**Still explicitly deferred** (per `context/foundation/prd-v2.md` Non-Goals): multiple route proposals per request, point-to-point routes, imperial units, offline/PWA, library search/filter/pagination, social/community features.