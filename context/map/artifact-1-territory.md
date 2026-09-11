# Territory Map — Git History

Scope: full repo history (first commit 2026-05-20, HEAD 2026-09-10 — repo is ~4 months old, shorter than the nominal 12-month window). Noise filtered: `package-lock.json`, `.snap` files, `node_modules/`, `bin/`, `obj/`.

## Activity — where the project was actually touched

**Top files** (by commit count):

| Commits | File |
|---|---|
| 29 | `context/foundation/roadmap.md` |
| 14 | `src/backend/VeloRoute/Program.cs` |
| 13 | `.github/workflows/backend.yml` |
| 10 | `.github/copilot-instructions.md` |
| 9 | `src/frontend/package.json` |
| 9 | `src/backend/Program.cs` *(pre-rename path — see Caveats)* |
| 8 | `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs` |
| 7 | `src/frontend/src/types/route.ts` |
| 7 | `src/frontend/src/components/RouteInfoPanel.tsx` |
| 7 | `src/frontend/README.md` / `README.md` |
| 6 | `context/foundation/test-plan.md` |
| 6 | `context/changes/{loop-route-generation,ci-deploy-hardening,backend-deploy}/plan.md` |

**Top folders** (module level, after collapsing generic `src/frontend/src` and `src/backend/VeloRoute` roots). Note: this column counts *file-touch lines* from `git log --name-only` (one line per file changed per commit), not commits — a directory with many small files inflates on a single scaffold or delete commit even without repeated hands-on work, as `.github/skills/*` below shows:

| File-touches | Folder |
|---|---|
| 44 | `src/frontend/src/app` (Next.js App Router routes) |
| 30 | `src/backend/VeloRoute.Tests/Routing` |
| 28 | `src/frontend/src/components` |
| 27 | `src/backend/VeloRoute/Routing` |
| 19 | `.github/skills/10x-bootstrapper` *(gone — see Caveats)* |
| 15 | `.github/skills/10x-tech-stack-selector` *(gone — see Caveats)* |
| 14 | `context/changes/loop-route-generation` |
| 11 | `context/changes/routing-api-wiring` |
| 11 | `context/changes/ci-deploy-hardening` |
| 9 | `context/changes/project-rename` |
| 9 | `context/changes/backend-deploy` |

`Routing/` (backend) and `src/frontend/src/app` + `components` are the real hands-on hotspots — consistent with `context/foundation/test-plan.md`'s own risk map calling out `LoopRouteGenerator`.

## Activity by quarter

Repo history only fills the last two of four nominal 12-month quarters:

- **2025-09-11 → 2025-12-11**: no commits (repo didn't exist yet).
- **2025-12-11 → 2026-03-11**: no commits.
- **2026-03-11 → 2026-06-11** (project start → early growth): `src/backend/Routing` (33), `src/frontend/src` (27), `context/changes/loop-route-generation` (14), `context/changes/routing-api-wiring` (11), `.github/skills/10x-bootstrapper` (11), `src/backend/Program.cs` (9). Core routing algorithm and initial scaffolding — pre-rename backend layout (`src/backend/Program.cs`, `src/backend/Routing`, no `VeloRoute/` subfolder yet).
- **2026-06-11 → 2026-09-11** (most recent): `src/backend/VeloRoute` (75), `src/frontend/src` (59), `src/backend/VeloRoute.Tests` (38), `context/foundation/roadmap.md` (25), `.github/workflows/backend.yml` (13), `context/changes/ci-deploy-hardening` (11). Shift from building the core algorithm to v2 features (auth, library, sharing) plus test coverage and deploy hardening — backend moved under `VeloRoute/` (project-rename), and testing/CI investment jumped sharply.

Net trend: effort moved from "build the routing engine" (Q3) to "harden, test, and ship v2 features" (Q4), with `roadmap.md` churn rising in step — a maturing, ship-and-iterate rhythm rather than a single monolithic build phase.

## Co-changes — what moves together

Depth-3 (`src/backend/VeloRoute`, `src/backend/VeloRoute.Tests`, `src/frontend/src`) is too coarse here — those paths *are* the project roots, so collapsing to that depth just says "backend co-changes with its own tests and with frontend," which is true of nearly every commit in a full-stack app and says nothing module-specific. Redone one level deeper (depth 4, e.g. `src/frontend/src/app`, `src/backend/VeloRoute/Routing`):

**Top pairs** (commits touching both):

| Commits | Pair |
|---|---|
| 12 | `src/backend/VeloRoute.Tests/Routing` ↔ `src/backend/VeloRoute/Program.cs` |
| 10 | `context/foundation/roadmap.md` ↔ `src/backend/VeloRoute.Tests/Routing` |
| 10 | `src/frontend/src/app` ↔ `src/frontend/src/components` |
| 9 | `context/foundation/roadmap.md` ↔ `src/frontend/src/components` |
| 9 | `src/backend/VeloRoute.Tests/Routing` ↔ `src/frontend/src/components` |
| 8 | `context/foundation/roadmap.md` ↔ `src/backend/VeloRoute/Program.cs` |
| 8 | `context/foundation/roadmap.md` ↔ `src/frontend/src/app` |
| 8 | `src/backend/VeloRoute.Tests/Routing` ↔ `src/frontend/src/app` |
| 8 | `src/backend/VeloRoute/Program.cs` ↔ `src/frontend/src/app` |
| 7 | `src/backend/VeloRoute/Program.cs` ↔ `src/frontend/src/components` |
| 5 | `src/backend/VeloRoute.Tests/Routing` ↔ `src/backend/VeloRoute/Routing` |
| 5 | `src/frontend/src/app` ↔ `src/frontend/src/types` |
| 5 | `src/frontend/src/components` ↔ `src/frontend/src/types` |
| 5 | `README.md` ↔ `src/frontend/README.md` |

**Top triples**: `roadmap.md` + `VeloRoute.Tests/Routing` + `VeloRoute/Program.cs` (8); `VeloRoute.Tests/Routing` + `VeloRoute/Program.cs` + `frontend/app` (8); `frontend/app` + `frontend/components` + `roadmap.md` (7).

Reading this: `Program.cs` (minimal-API style — endpoints registered directly, no controllers) is the de-facto backend seam, so it co-changes with both `Routing/` and its own test suite on almost every routing change — a new/changed endpoint in `Program.cs` routinely means a `Routing/` change and a `VeloRoute.Tests/Routing` change land in the same commit (12 commits, the single strongest pairing). On the frontend, `app` ↔ `components` ↔ `types` form a tight triangle — a route change typically drags a component and its shared type along. The two stacks meet through `roadmap.md` and, more concretely, through `src/frontend/src/app/api` (Next.js route handlers proxying to the backend) co-changing with `src/backend/VeloRoute/Program.cs` — the expected full-stack coupling point for any change that touches both an API endpoint and its proxy.

### File-level finding: a shared test harness coupled to every endpoint change

Pushing co-change to full file paths (no directory truncation) mostly just thins the signal — with 99 commits and ~250 unique files, most pairs repeat once or twice, and only a handful clear 4+ occurrences. One of those survivors is architecturally relevant: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs` ↔ `src/backend/VeloRoute/Program.cs`, **6 commits**.

`TestInfrastructure.cs` is shared test-harness/fixture code, not a single endpoint's test file — yet it changes in step with `Program.cs` on more than half of `Program.cs`'s own commits in this window. That means adding or touching almost any backend endpoint routinely forces an edit to the shared harness, rather than the harness staying stable while individual endpoint tests vary independently. That's a coupling smell worth carrying into M4L4 (refactoring plan): a shared test-infrastructure file that can't stay untouched across unrelated endpoint changes is itself a piece of debt, likely worth stabilizing (e.g. via a more general fixture/builder API) as part of any `Routing`/`Program.cs` refactor.

### Common denominator

`context/foundation/roadmap.md` is the one file that co-changes with essentially every other hotspot (backend code, backend tests, frontend code alike) rather than clustering with a single area — it's the project's cross-cutting ledger, updated as a matter of workflow discipline on nearly every feature commit (this matches the repo's own documented convention in `.github/copilot-instructions.md`: keep `roadmap.md` current as part of each change's final commit).

A secondary, smaller cluster: `README.md` ↔ `AGENTS.md` ↔ `.github/copilot-instructions.md` ↔ per-component `README.md`s — these move together during doc-sync commits (also a documented convention: check all of these for staleness before every commit).

## Caveats — stale co-change data

Two entries in the raw co-change data no longer point at real files, due to the `project-rename` change (commits 9, mid-project):

- `src/backend/Program.cs` — **no longer exists**; moved to `src/backend/VeloRoute/Program.cs`.
- `src/backend/Routing` — **no longer exists**; moved to `src/backend/VeloRoute/Routing`.

Their historical co-change counts (e.g. `Program.cs` ↔ `Routing`, 7 commits) reflect the old layout and should not be read as current coupling — the equivalent current-day coupling is folded into the `VeloRoute` ↔ `VeloRoute.Tests` numbers above.

A third, different case: `.github/skills/10x-bootstrapper` and `.github/skills/10x-tech-stack-selector` rank high on file-touches (19 and 15) but represent only **3 commits total**, not sustained activity — an initial scaffold commit adding ~10+ small reference files, one edit, then a full-tree deletion (`4e67d80`, "consolidate skills to .claude/skills — drop .github/skills duplicate", 2026-06-15). Both directories are gone; the live equivalents are `.claude/skills/10x-bootstrapper` and `.claude/skills/10x-tech-stack-selector`. Lesson for reading this table: a directory with many small files can dominate the file-touch ranking off a single add or delete commit — cross-check against actual commit count before calling something a hotspot.
