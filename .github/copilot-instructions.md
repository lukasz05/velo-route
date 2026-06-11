# VeloRoute — Copilot Instructions

## Key conventions

### Next.js (this is Next.js 16 / React 19)
`src/frontend/AGENTS.md` warns that this version has breaking changes from older training data. Before writing any Next.js code, check `node_modules/next/dist/docs/` for current API behaviour.

- Import alias `@/*` maps to `src/frontend/src/*`.
- Tailwind v4 — config is in `postcss.config.mjs` (no `tailwind.config.js`); utility classes work the same but the config surface changed.

### .NET backend
- Minimal API pattern: endpoints are registered directly in `Program.cs`, not in controller classes.
- Nullable reference types enabled (`<Nullable>enable</Nullable>`). Don't introduce `#nullable disable` suppressions.
- `RootNamespace` is `bootstrap_scaffold` (scaffolding artefact — rename when adding real namespaces).

### Monorepo
- Each project manages its own dependencies independently (`src/frontend/package.json`, `src/backend/*.csproj`). There is no root-level `package.json`.
- Environment files (`.env`, `.env.*`) are gitignored. Use `.env.example` for documenting required variables.
- `context/` is never modified by scaffold tooling or automated processes — it is the human/agent knowledge base for this project.

---

Free road-cycling loop-route planner. User enters a start point and km range; the app returns ≥1 loop route on an interactive map with GPX export. No auth, no accounts in v1. Full PRD: `context/foundation/prd.md`.

## Repository layout

```
src/
  frontend/   Next.js 16 (React 19, TypeScript, Tailwind v4, App Router)
  backend/    ASP.NET Core (.NET 10, minimal API)
context/
  foundation/
    frontend/tech-stack.md
    backend/tech-stack.md
    prd.md
  changes/    per-change work logs
.gitignore    monorepo-wide (root only)
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
dotnet test                        # once tests exist
```

Swagger UI (development only): `http://localhost:5098/swagger`

No test runner is configured in either project yet.

## Architecture

The two projects are independently runnable. In production, the Next.js frontend calls the .NET backend API over HTTP; no shared runtime or in-process communication.

- **Frontend** (`src/frontend/src/app/`): Next.js App Router. All routes are under `src/app/`. Client components are opted in with `"use client"`.
- **Backend** (`src/backend/`): .NET 10 minimal API style (`Program.cs`, no controllers folder). OpenAPI/Swagger is registered via `builder.Services.AddOpenApi()` and mapped at `/openapi/v1.json` in development.
- **Data flow**: frontend → HTTP → backend → external routing API (OpenRouteService or similar, not yet integrated). Location inputs are not persisted server-side.

## Workflow conventions

- **Branch per change**: before running `/10x-implement` for any change, create a branch named after the change-id (`git checkout -b <change-id>`). Do this automatically without being asked.

## What v1 does and does not include

**In scope**: start-point search, km range input, single loop-route proposal, interactive map display, GPX export, mobile-responsive UI.

**Explicitly deferred to v2**: user accounts/auth, saved routes, multiple proposals, point-to-point routes, imperial units, offline/PWA.