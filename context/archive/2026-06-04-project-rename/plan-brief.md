# Project Rename, READMEs, and License — Plan Brief

> Full plan: `context/changes/project-rename/plan.md`

## What & Why

Replace every scaffold-generated placeholder (`bootstrap-scaffold`, `bootstrap_scaffold`, `Create Next App`) with the real product name **VeloRoute** across the backend and frontend. Add practical developer READMEs for both projects plus a repo-level `README.md` and MIT `LICENSE`. The project is in active development (S-01 done, S-02 next) — shipping with scaffold names and missing docs is an ongoing rough edge.

## Starting Point

Both the .NET backend and Next.js frontend were scaffolded using generic CLI tools. The backend carries `bootstrap_scaffold` as the C# root namespace across 11 source files, a dead `WeatherForecast` record, and a stale `.http` test request. The frontend shows "Create Next App" as the browser tab title and has generic `create-next-app` boilerplate as its README.

## Desired End State

No `bootstrap-scaffold`, `bootstrap_scaffold`, or "Create Next App" strings remain in tracked source. The browser tab shows "VeloRoute". `dotnet build` and `npm run build` both pass with the renamed identifiers. New developers can follow either project README to get running in under 5 minutes.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| License | MIT | Simplest permissive license; lowest friction for contributors and consumers | Plan |
| .csproj metadata depth | Full (AssemblyName, Version, Description, Authors) | Produces a properly self-describing assembly; minimal extra effort given we're already editing the file | Plan |
| README depth | Practical dev guides (run commands, env vars, Swagger URL) | Enough to unblock a new contributor; richer docs (architecture, API reference) deferred until the API surface stabilises | Plan |

## Scope

**In scope:**
- `src/backend/bootstrap-scaffold.csproj` → `velo-route.csproj` with updated metadata
- All 11 `src/backend/Routing/*.cs` namespace declarations
- `src/backend/Program.cs` — using directive + remove dead `WeatherForecast` record
- `src/backend/bootstrap-scaffold.http` → `velo-route.http` (remove stale `/weatherforecast/` request)
- New `src/backend/README.md`
- `src/frontend/package.json` — name, description, license, author, repository, homepage
- `src/frontend/src/app/layout.tsx` — metadata.title and metadata.description
- `src/frontend/README.md` — replace create-next-app boilerplate
- New repo root `README.md`
- New repo root `LICENSE` (MIT)

**Out of scope:**
- GitHub Actions workflow (no scaffold names present)
- `appsettings.json` config keys (already clean)
- `.env.example` (already uses `VELO_API_URL`)
- `UserSecretsId` GUID in `.csproj` (opaque identifier — renaming breaks local secrets)
- Contributing guide, API endpoint docs, architecture diagrams

## Architecture / Approach

Three independent phases, each verifiable in isolation. No logic changes anywhere — every change is a string replacement, a file rename, or a new documentation file. Build tools (`dotnet build`, `npm run build`) are the primary automated verification gate.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Backend Rename | Renamed project, clean `VeloRoute.Routing` namespace, dead code removed, practical backend README | File rename (not edit) — `bootstrap-scaffold.csproj` must be deleted, not just updated |
| 2. Frontend Metadata | Correct package name, "VeloRoute" browser title, practical frontend README | Low — purely additive metadata changes |
| 3. Repo Root Files | `README.md` + MIT `LICENSE` | None — net-new files only |

**Prerequisites:** None — can run at any time, parallel with any feature slice  
**Estimated effort:** ~1 session across 3 phases

## Open Risks & Assumptions

- `UserSecretsId` GUID preserved as-is — if a developer has already stored secrets under the old project, they will need to re-enter them after the rename (standard `dotnet user-secrets` behaviour; acceptable)
- Copyright holder set to "VeloRoute Contributors" — adjust if a specific legal entity is preferred

## Success Criteria (Summary)

- `dotnet build` + `npm run build` + `npm run lint` all pass with zero references to scaffold names
- Browser tab shows "VeloRoute"; `GET /health` + Swagger UI confirm backend is healthy with new identity
- Root `README.md` and `LICENSE` render correctly on GitHub
