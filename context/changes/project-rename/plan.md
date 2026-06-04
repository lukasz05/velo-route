# Project Rename, READMEs, and License — Implementation Plan

## Overview

Replace every scaffold-generated placeholder (`bootstrap-scaffold`, `bootstrap_scaffold`, `Create Next App`) with the real product name **VeloRoute** across the backend and frontend projects, add practical developer READMEs for both, and add a repo-level MIT `LICENSE` and `README.md`. This is a pure housekeeping change — no logic, no behaviour, no API contract changes.

## Current State Analysis

The monorepo was bootstrapped with a generic scaffold. Placeholder names remain in:
- `.NET` backend: `bootstrap_scaffold` namespace (11 `.cs` files), `bootstrap-scaffold.csproj`, `bootstrap-scaffold.http`, and a dead `WeatherForecast` record in `Program.cs`
- Frontend: `"name": "bootstrap-scaffold"` in `package.json`, `"Create Next App"` metadata in `layout.tsx`, generic `create-next-app` boilerplate in `README.md`
- Repo root: no `README.md`, no `LICENSE`

The GitHub Actions workflow (`azure-static-web-apps-purple-sky-08f4fb710.yml`) does **not** reference any scaffold names — no changes needed there.

No `.sln` file exists; the backend is a standalone `.csproj` project, so renaming the file requires no solution reference update.

## Desired End State

After this plan completes:
- `dotnet build` succeeds in `src/backend/` using the renamed project and `VeloRoute.Routing` namespace
- `npm run build` and `npm run lint` succeed in `src/frontend/`
- No `bootstrap-scaffold`, `bootstrap_scaffold`, or "Create Next App" strings remain in tracked source files
- Repo root has a working `README.md` and `LICENSE`
- Both projects have practical dev-guide READMEs

### Key Discoveries

- `src/backend/Routing/` contains exactly 11 `.cs` files, all starting with `namespace bootstrap_scaffold.Routing;` — confirmed by grep
- `Program.cs` line 2: `using bootstrap_scaffold.Routing;` and lines 106–109: dead `WeatherForecast` record to remove
- `bootstrap-scaffold.http` contains a stale `/weatherforecast/` request that should be removed
- `.env.example` already uses `ORS_API_KEY` and `VELO_API_URL` — consistent naming, no change needed
- `appsettings.json` uses `"ORS"` config section — unrelated to rename, no change needed
- `UserSecretsId` GUID in `.csproj` must be preserved as-is (tied to local user secrets store)

## What We're NOT Doing

- No changes to any business logic, API endpoints, routing algorithm, or data contracts
- No changes to the GitHub Actions workflow (it references no scaffold names)
- No changes to `appsettings.json` / `appsettings.Development.json` (config keys are already clean)
- No changes to `.env.example` (already uses `VELO_API_URL`, not scaffold names)
- No namespace update to `UserSecretsId` GUID (it's an opaque identifier — renaming breaks local secrets)
- No contributing guide or API endpoint docs in READMEs (out of scope per user decision; practical dev guides only)
- No v2 features, auth, or persistence

## Implementation Approach

Three independent phases, each verifiable before moving on:
1. **Backend** — rename the project, update all namespaces, clean up dead code, update `.http`, add `README.md`
2. **Frontend** — update `package.json` metadata fields, update `layout.tsx` metadata, replace `README.md`
3. **Repo root** — create `README.md` and `LICENSE`

## Critical Implementation Details

**File rename, not edit**: Renaming `bootstrap-scaffold.csproj` → `velo-route.csproj` requires creating the new file and deleting the old one (the `edit` tool cannot rename files). `dotnet build` discovers `.csproj` files by directory scan, so the rename alone is sufficient — no project reference or solution entry to update.

**`WeatherForecast` record**: Lives at the bottom of `Program.cs` (lines 106–109). Removing it is safe — it is never referenced anywhere in the codebase (confirmed by grep).

---

## Phase 1: Backend Rename

### Overview

Rename the `.csproj` file, update all project metadata (`RootNamespace`, `AssemblyName`, `Version`, `Description`, `Authors`), replace every `bootstrap_scaffold.Routing` namespace declaration across all 11 Routing source files and `Program.cs`, clean up the dead `WeatherForecast` record, update `bootstrap-scaffold.http`, and add a practical developer `README.md`.

### Changes Required

#### 1. `src/backend/velo-route.csproj` (new file — replaces `bootstrap-scaffold.csproj`)

**File**: `src/backend/velo-route.csproj`

**Intent**: Rename the project file and add full metadata so the assembly identifies itself as VeloRoute. Preserve the existing `UserSecretsId` GUID exactly — changing it breaks local developer secrets.

**Contract**: New file with the same `<PackageReference>` block as the original. Updated `<PropertyGroup>` additions:
- `<RootNamespace>VeloRoute</RootNamespace>`
- `<AssemblyName>VeloRoute</AssemblyName>`
- `<Version>0.1.0</Version>`
- `<Description>VeloRoute backend API — loop route generation for road cyclists</Description>`
- `<Authors>VeloRoute Contributors</Authors>`

After creating the new file, delete `bootstrap-scaffold.csproj`.

#### 2. `src/backend/Program.cs`

**File**: `src/backend/Program.cs`

**Intent**: Update the using directive to the renamed namespace, and remove the dead `WeatherForecast` record that was never wired to any endpoint.

**Contract**: Line 2 changes from `using bootstrap_scaffold.Routing;` to `using VeloRoute.Routing;`. Lines 106–109 (the `WeatherForecast` record) are deleted. No other changes.

#### 3. `src/backend/Routing/*.cs` (11 files)

**Files**: All 11 files in `src/backend/Routing/`

**Intent**: Update the namespace declaration in every file from the scaffold name to the product name.

**Contract**: Each file's first `namespace` statement changes from `namespace bootstrap_scaffold.Routing;` to `namespace VeloRoute.Routing;`. No other changes to any file.

#### 4. `src/backend/VeloRoute.http` (new file — replaces `bootstrap-scaffold.http`)

**File**: `src/backend/VeloRoute.http`

**Intent**: Update the HTTP test file to use the new variable name and remove the stale `/weatherforecast/` request that no longer exists in the API.

**Contract**:
- Variable renamed: `@velo_route_HostAddress = http://localhost:5098`
- Stale `GET /weatherforecast/` block removed
- A `GET /health` smoke-test request replaces it (endpoint already exists in `Program.cs`)
- After creating the new file, delete `bootstrap-scaffold.http`

#### 5. `src/backend/README.md` (new file)

**File**: `src/backend/README.md`

**Intent**: Give developers a practical guide for running and configuring the backend — replacing the absence of any README.

**Contract**: Covers: how to run (`dotnet run`), the Swagger UI URL (`http://localhost:5098/swagger`), required user secrets (`ORS__ApiKey`), the `ALLOWED_ORIGINS` env var, and a brief project structure note listing `Routing/` and `Program.cs`.

### Success Criteria

#### Automated Verification

- `dotnet build` passes in `src/backend/` with the renamed project
- No `bootstrap_scaffold` or `bootstrap-scaffold` strings remain in `src/backend/` tracked files

#### Manual Verification

- `dotnet run` starts successfully and `GET http://localhost:5098/health` returns `{"status":"ok"}`
- Swagger UI loads at `http://localhost:5098/swagger` and shows "VeloRoute API v1"
- `bootstrap-scaffold.csproj` and `bootstrap-scaffold.http` no longer exist in `src/backend/`

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation that the backend starts and the health endpoint responds before proceeding.

---

## Phase 2: Frontend Metadata

### Overview

Update `package.json` with proper VeloRoute metadata fields, update `layout.tsx` app metadata to show the real product name and description, and replace the generic create-next-app `README.md` with a practical VeloRoute frontend dev guide.

### Changes Required

#### 1. `src/frontend/package.json`

**File**: `src/frontend/package.json`

**Intent**: Replace the scaffold package name and add the metadata fields expected of a named open-source package.

**Contract**: Fields to update/add:
- `"name"` → `"velo-route"`
- `"description"` → `"Free road-cycling loop-route planner — enter a start point and km range, get a loop route on an interactive map with GPX export"`
- `"license"` → `"MIT"`
- `"author"` → `"VeloRoute Contributors"`
- `"repository"` → `{"type": "git", "url": "https://github.com/lukasz05/velo-route"}`
- `"homepage"` → `"https://github.com/lukasz05/velo-route#readme"`

#### 2. `src/frontend/src/app/layout.tsx`

**File**: `src/frontend/src/app/layout.tsx`

**Intent**: Replace scaffold placeholder metadata with the real product name and description so browsers, search engines, and social previews show VeloRoute.

**Contract**: `metadata.title` → `"VeloRoute"`. `metadata.description` → `"Free road-cycling loop-route planner. Enter a start point and distance range, get a loop route on an interactive map — no account required."`. No other changes to this file.

#### 3. `src/frontend/README.md`

**File**: `src/frontend/README.md`

**Intent**: Replace the generic create-next-app boilerplate with a practical guide for developers working on the VeloRoute frontend.

**Contract**: Covers: what this project is (one sentence), how to run (`npm run dev` → `http://localhost:3000`), required env vars (sourced from `.env.example`: `ORS_API_KEY`, `VELO_API_URL`), the `local-ca.pem` note from `.env.example`, and the available scripts (`dev`, `build`, `lint`). No Vercel deploy section (the project deploys to Azure SWA, not Vercel).

### Success Criteria

#### Automated Verification

- `npm run build` passes in `src/frontend/`
- `npm run lint` passes in `src/frontend/`
- No `bootstrap-scaffold` or `Create Next App` strings remain in `src/frontend/` tracked files

#### Manual Verification

- Browser tab shows "VeloRoute" as the page title when running `npm run dev`
- `package.json` `"name"` field is `"velo-route"` (verified by inspection)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation that the page title is correct in the browser before proceeding.

---

## Phase 3: Repo Root Files

### Overview

Add a `README.md` at the repo root giving an overview of the monorepo, and an MIT `LICENSE` file.

### Changes Required

#### 1. `README.md` (repo root, new file)

**File**: `README.md`

**Intent**: Give anyone landing on the repo a clear picture of what VeloRoute is, how the monorepo is laid out, and how to run both projects locally.

**Contract**: Sections:
- **VeloRoute** — one-paragraph product description (road-cycling loop-route planner, free, no account, GPX export)
- **Monorepo layout** — table or list showing `src/frontend/` and `src/backend/` with one-liner each
- **Dev commands** — frontend (`npm run dev` in `src/frontend/`) and backend (`dotnet run` in `src/backend/`) with their default URLs
- **Required env vars** — `ORS_API_KEY` (backend user secret for OpenRouteService) and `VELO_API_URL` (frontend; see `.env.example`)
- **Further reading** — link to `context/foundation/prd.md`

#### 2. `LICENSE` (repo root, new file)

**File**: `LICENSE`

**Intent**: Establish MIT licensing for the repository.

**Contract**: Standard MIT License text, copyright year `2026`, copyright holder `VeloRoute Contributors`.

### Success Criteria

#### Automated Verification

- Both `README.md` and `LICENSE` exist at the repo root
- `LICENSE` contains "MIT License" and "VeloRoute Contributors"

#### Manual Verification

- Root `README.md` renders correctly on GitHub (check headings, code blocks, links)

**Implementation Note**: After completing this phase, all automated and manual criteria for all three phases should be verified before the change is considered done.

---

## Testing Strategy

### Automated Tests

- `dotnet build` (Phase 1) — verifies namespace rename compiles cleanly
- `npm run build` + `npm run lint` (Phase 2) — verifies no TypeScript/ESLint regressions

### Manual Testing Steps

1. `dotnet run` in `src/backend/` — confirm startup, hit `/health`, confirm Swagger shows "VeloRoute API v1"
2. `npm run dev` in `src/frontend/` — confirm browser tab shows "VeloRoute"
3. `grep -r "bootstrap.scaffold\|bootstrap-scaffold\|Create Next App" src/` — confirm zero matches
4. Verify `README.md` and `LICENSE` render correctly on GitHub

## References

- Roadmap entry: `context/foundation/roadmap.md` (H-01, "Project rename, READMEs, and license")
- Backend source: `src/backend/Program.cs`, `src/backend/Routing/`
- Frontend source: `src/frontend/package.json`, `src/frontend/src/app/layout.tsx`

---

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Backend Rename

#### Automated

- [x] 1.1 `dotnet build` passes in `src/backend/` with renamed project — 56c7c19
- [x] 1.2 No `bootstrap_scaffold` or `bootstrap-scaffold` strings remain in `src/backend/` tracked files — 56c7c19

#### Manual

- [x] 1.3 `dotnet run` starts and `GET /health` returns `{"status":"ok"}` — 56c7c19
- [x] 1.4 Swagger UI loads at `http://localhost:5098/swagger` and shows "VeloRoute API v1" — 56c7c19
- [x] 1.5 `bootstrap-scaffold.csproj` and `bootstrap-scaffold.http` no longer exist in `src/backend/` — 56c7c19

### Phase 2: Frontend Metadata

#### Automated

- [x] 2.1 `npm run build` passes in `src/frontend/` — 27b5cf3
- [x] 2.2 `npm run lint` passes in `src/frontend/` — 27b5cf3
- [x] 2.3 No `bootstrap-scaffold` or `Create Next App` strings remain in `src/frontend/` tracked files — 27b5cf3

#### Manual

- [x] 2.4 Browser tab shows "VeloRoute" when running `npm run dev` — 27b5cf3
- [x] 2.5 `package.json` `"name"` field is `"velo-route"` — 27b5cf3

### Phase 3: Repo Root Files

#### Automated

- [x] 3.1 `README.md` and `LICENSE` exist at repo root
- [x] 3.2 `LICENSE` contains "MIT License" and "VeloRoute Contributors"

#### Manual

- [x] 3.3 Root `README.md` renders correctly on GitHub
