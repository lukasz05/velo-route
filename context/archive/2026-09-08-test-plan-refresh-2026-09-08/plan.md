# Test Plan Refresh (2026-09-08) Implementation Plan

## Overview

`context/foundation/test-plan.md` was written 2026-06-20, when the project was v1-only. Every v2 slice shipped after it (auth 2026-07-15, save/library 07-18, delete 07-22, share 07-26, account-deletion 07-26, edit 09-08). All eight sections now describe a project that no longer exists: the risk map contains only v1 routing concerns, the hot-spot data uses a 30-day window that now covers 8 commits, §3 marks a CI gate "not started" that has been live since 2026-07-01, and §4 omits the frontend test stack entirely.

This change rewrites the document to match the shipped state, then closes rollout phase 4 by wiring the frontend test gate into CI — the one piece of phase 4 that is genuinely still missing.

## Current State Analysis

Verified against the repository on 2026-09-08:

| Section | Recorded | Actual |
|---|---|---|
| §1 hot-spots | `src/backend/Routing` 33/30d, `src/frontend/src` 27/30d, `Program.cs` 9/30d | 90d: `src/frontend/src/app` 31, `VeloRoute.Tests/Routing` 29, `VeloRoute/Routing` 27, `src/frontend/src/components` 19, `Program.cs` 13 |
| §2 risk map | 6 risks, all v1 routing | 101 `[Fact]`/`[Theory]` methods across 18 backend files; the majority defend v2 behaviour that appears nowhere in the map |
| §3 phase 4 | `not started` | Backend half live: `.github/workflows/backend.yml:26` runs `dotnet test` and `deploy` carries `needs: test`. Frontend half absent |
| §4 stack | "No test runner is configured in either project yet" | Backend: xUnit 2.9.3, Mvc.Testing 10.0.7, Testcontainers.PostgreSql 4.13.0. Frontend: Vitest 4.1.9, jsdom 29.1.1, RTL 16.3.2 — 47 cases / 8 files |
| §4 tools | "GitHub MCP — available; checked 2026-06-05" | No MCP servers exposed in session; GitHub reachable via `gh` CLI only |
| §6 cookbook | 3 entries, all .NET | Frontend test base has existed since 2026-07 with no documented pattern |
| §8 ledger | 2026-06-05 / 06-15 / 06-20 | — |

The 30-day window is no longer usable for likelihood weighting: `git log --since="30 days ago"` returns 8 commits against 48 for 90 days.

### Key Discoveries

- **The SWA workflow has no Node toolchain.** `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml` goes straight from `actions/checkout@v3` to `Azure/static-web-apps-deploy@v1`, which builds internally via Oryx. There is no `setup-node`, no `npm ci`, no build step to hang a test on. Phase 4's frontend half therefore needs a *new job*, not an inserted step.
- **`backend.yml` already models the pattern to copy** — a `test` job and a `deploy` job carrying `needs: test` and an `if: github.ref == 'refs/heads/main' && github.event_name == 'push'` guard. The SWA workflow's deploy job has a different guard (it must also run on PRs, for preview environments) and a second `close_pull_request_job` that must not gain the dependency.
- **`POST /routes/gpx` (`src/backend/VeloRoute/Program.cs:407`) has no test.** Grep across `VeloRoute.Tests` returns nothing. The frontend calls it from three places (`RouteInfoPanel.tsx:72`, `my-routes/[id]/page.tsx:79`, `r/[token]/page.tsx:49`).
- **`vitest.config.ts` sets no `include`/`exclude`.** Vitest 4's defaults sweep `**/*.spec.*`, so adding any Playwright spec file without an exclude first would have Vitest collect and fail on it.
- **Playwright is not a dependency.** `src/frontend/package.json` has no `@playwright/test`. §3 phase 5 proposes e2e, so §4 must record the layer as a candidate without pretending it exists.
- **No Node version is pinned anywhere** — no `.nvmrc`, no `engines` field. Oryx picks its own inside the deploy action.
- **Frontend route-handler test pattern** (`src/frontend/src/app/api/routes/route.test.ts`): import the exported `GET`/`POST` directly, construct a `Request`, `vi.stubGlobal('fetch', mock)`, assert both the forwarded call (URL, `Authorization` header, body) and the relayed response; `vi.unstubAllGlobals()` in `afterEach`.
- **Frontend component test pattern** (`src/frontend/src/components/RouteInfoPanel.test.tsx`): `vi.mock('@clerk/nextjs', …)` for auth-dependent components, a local `makeRoute()` factory taking `Partial<T>` overrides, and assertions by ARIA role (`getByRole('status')`) rather than class or DOM position.

## Desired End State

`context/foundation/test-plan.md` describes the project as it is on 2026-09-08: a ten-row risk map covering both v1 routing and v2 account features, hot-spot data from a 90-day window, a phase table whose statuses match CI and the archive, a stack section listing every runner actually installed, a cookbook with a frontend entry, and a freshness ledger dated today. The Azure SWA workflow runs `npm test` and refuses to deploy when it fails.

Verify by: reading the document end-to-end against the table in "Current State Analysis" — no row should still describe the left-hand column; and by pushing a branch with a deliberately failing frontend test and confirming the SWA deploy job does not run.

## What We're NOT Doing

- **Not implementing rollout phases 5 or 6.** They get their own change folders via `/10x-new`, per the rollout convention. This change only *writes them down*.
- **Not installing Playwright** or writing any e2e test. §4 records it as an unpinned candidate; phase 5 evaluates and pins it.
- **Not adding the `vitest.config.ts` exclude.** It is a phase-5 prerequisite, documented in §3 as such, not applied here — nothing in this change adds a `*.spec.*` file, so applying it now would be an unmotivated config edit.
- **Not writing new tests for risks 9 and 10** (ownership checks, account-deletion cascade). Both are already covered by `EditRouteTests`, `ShareRouteTests`, and `AccountDeletionTests`. They need documenting in §2, not defending.
- **Not testing anonymous rate-limit abuse.** The PRD explicitly defers app-level throttling ("ORS free-tier rate limits are the de-facto ceiling"); a test would require building the safeguard first. Moved to §7 negative space.
- **Not pinning a Node version** in the new CI job beyond `lts/*` — see Critical Implementation Details.

## Implementation Approach

Two phases with a clean boundary: phase 1 touches one markdown file and nothing else; phase 2 touches CI plus the two documents that phase 2 makes stale. Splitting the other way (risk map separately from the mechanical sections) was considered and rejected — §3's phase rows cite risk IDs, so a half-updated document is internally inconsistent between commits.

**Risk ID stability.** Original IDs 1–6 are preserved exactly, because §3's shipped phase rows cite them ("Risks covered: #1, #3") and renumbering would retroactively falsify that history. New risks append as 7–10. The `#` column is therefore an identifier, not a rank; the Impact × Likelihood columns carry the ordering. The document states this explicitly so a future reader does not read row order as priority order.

Handoff risks are renumbered accordingly: handoff "#6 anonymous flow regression" becomes **#7**, handoff "#7 silent token failure" becomes **#8**, handoff "#8 ownership" becomes **#9**, handoff "#9 account deletion" becomes **#10**. Handoff's merged log/key risk splits back into the original **#4** (coordinates in logs) and **#6** (ORS key in error body).

## Critical Implementation Details

**CI job wiring.** The SWA workflow's `close_pull_request_job` runs on `pull_request` with `action == 'closed'`; it must NOT gain `needs: test`, or closing a PR would be blocked by a test run against a deleted branch. Only `build_and_deploy_job` takes the dependency.

**Node version.** The repository pins no Node version anywhere — no `.nvmrc`, no `engines`. Use `node-version: 'lts/*'` in the new job. Pinning a specific major only in CI would create a drift with nothing in the repo to sync it against, which is the exact staleness class this refresh exists to remove.

**Path filters already scope the workflow** to `src/frontend/**`, so the test job needs no additional filtering; it inherits the trigger.

## Phase 1: Refresh test-plan.md

### Overview

Rewrite all eight sections of `context/foundation/test-plan.md` so each describes the 2026-09-08 state. Single file, no code.

### Changes Required:

#### 1. Header and §1 Strategy

**File**: `context/foundation/test-plan.md`

**Intent**: Update the `Last updated` line, and replace the hot-spot scope sentence with the 90-day figures. The three strategy principles are unchanged — they held up.

**Contract**: Hot-spot line records: `src/frontend/src/app` 31, `src/backend/VeloRoute.Tests/Routing` 29, `src/backend/VeloRoute/Routing` 27, `src/frontend/src/components` 19, `src/backend/VeloRoute/Program.cs` 13 — all commits/90d. State the window choice and its reason (30d yields 8 commits; 90d yields 48). Name the scanned scope: `src/backend/VeloRoute`, `src/backend/VeloRoute.Tests`, `src/frontend/src`.

#### 2. §2 Risk Map

**File**: `context/foundation/test-plan.md`

**Intent**: Extend the six existing risks to ten, adding the four v2 risks, and add the ID-stability note so the non-monotonic Impact column is not misread as a sorting bug.

**Contract**: Rows 1–6 keep their current text, IDs, Impact, Likelihood, and Source cells verbatim, with one edit: risk 2's Source gains "re-confirmed by the 2026-09-08 interview (Q3)". Four rows append:

| # | Risk | Impact | Likelihood | Source |
|---|---|---|---|---|
| 7 | v2 auth/library work regresses the anonymous generate → map → GPX flow; a stranger hits a broken core product | High | High | interview Q1 + Q4; hot-spot dir `src/frontend/src/app` (31 commits/90d); PRD-v2 Constraints ("anonymous route generation must continue to work without an account in v2") |
| 8 | A third-party token (ORS, Clerk) is absent or misconfigured and the failure is silent rather than loud; the product looks broken with no diagnosable signal | High | Medium | interview Q2 (lived incident); PRD-v2 dependency on ORS + Clerk |
| 9 | A route or share endpoint verifies "logged in" but not "owns this"; one user reads or mutates another's route | High | Medium | abuse/security lens (auth + user input both present); PRD-v2 flat user model, Access Control section |
| 10 | Account deletion leaves user data behind in Postgres or Clerk | High | Medium | PRD-v2 NFR ("all associated data — email address, saved routes — is permanently deleted") |

The Risk Response Guidance table gains matching rows 7–10 with the same six columns. Row 9 and row 10 must record in "What would prove protection" that coverage already exists (`EditRouteTests` cross-user rejection, `ShareRouteTests`, `AccountDeletionTests`) so no reader opens a rollout phase for them.

Two challenger findings are already applied and must not be re-litigated: anonymous rate-limit abuse is dropped to §7; share-token *guessability* is reframed as share-token *lifecycle* (observable form: a revoked or deleted token returns 404) and folded into risk 9.

#### 3. §3 Phased Rollout

**File**: `context/foundation/test-plan.md`

**Intent**: Correct phase 4's scope and status, append phases 5 and 6, and record that risks 9 and 10 arrived covered.

**Contract**: Phase rows 1–3 unchanged (`shipped`, existing change folders). Phase 4 rescoped: goal becomes the frontend `npm test` gate only, with a note that the backend `dotnet test` gate has been live since 2026-07-01; status `in progress` when phase 1 lands, flipped to `shipped` by phase 2 of this plan; change folder `context/changes/test-plan-refresh-2026-09-08`. Two new rows:

- **5. Core anonymous flow end-to-end** — prove generate → map → GPX download survives in a real browser, and add the missing `POST /routes/gpx` endpoint test (three untested validation branches: empty coordinates, non-finite values, out-of-range values; plus the `application/gpx+xml` content type). Risks covered: #7. Types: e2e + integration. Status: `not started`.
- **6. Config-failure loudness** — an absent or misconfigured ORS/Clerk token produces a loud, diagnosable failure rather than a silent one. Risks covered: #8. Type: integration. Status: `not started`.

Below the table, add: the order rationale (4 locks the floor cheaply; 5 carries the highest risk and is the priority if only one lands before the 2026-09-14 deadline; 6 is narrow), a note that risks 9 and 10 need no phase, and the two phase-5 prerequisites — the `vitest.config.ts` exclude, and `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` being required even on anonymous pages because `ClerkProvider` wraps the whole app in `src/frontend/src/app/layout.tsx`.

#### 4. §4 Stack

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the "no test runner is configured" preamble and the four placeholder rows with what is installed, and correct the grounding-tools block.

**Contract**: Rows — unit+integration (.NET): xUnit 2.9.3 with `xunit.runner.visualstudio` 3.1.4, `Microsoft.AspNetCore.Mvc.Testing` 10.0.7, `Testcontainers.PostgreSql` 4.13.0 (note: `dotnet test` needs a running Docker daemon). HTTP mocking (.NET): custom `FakeOpenRouteServiceClient` at the `IOpenRouteServiceClient` boundary — the WireMock.Net option floated in the original §4 was not taken. Frontend unit + component: Vitest 4.1.9, jsdom 29.1.1, `@testing-library/react` 16.3.2, `@testing-library/jest-dom` 6.9.1, `@testing-library/user-event` 14.6.1 — 47 cases across 8 files, global setup at `src/frontend/src/test-setup.ts`. E2E: Playwright as candidate, **not installed**, version to be evaluated and pinned by phase 5.

Grounding tools block, all `checked: 2026-09-08`: no MCP servers exposed in the session (this replaces the incorrect "GitHub MCP — available" claim); GitHub reachable via the `gh` CLI through the shell only; no Playwright MCP.

#### 5. §5 Quality Gates

**File**: `context/foundation/test-plan.md`

**Intent**: Add the frontend test gate row and correct the "required after §3 Phase N" annotations.

**Contract**: New row — `unit + component (Vitest)` | local + CI | required after §3 Phase 4 | regressions in route-handler proxying and component rendering. Existing `.NET` rows keep their gate references. The lint/typecheck and lint/build rows are unchanged (both verified still wired).

#### 6. §6 Cookbook

**File**: `context/foundation/test-plan.md`

**Intent**: Add a frontend entry drawn from the shipped test files, and reserve an e2e slot for phase 5. Existing §6.1–6.3 are still accurate and stay verbatim.

**Contract**: New **§6.4 Adding a frontend test (Vitest + RTL)**, split into two documented patterns with a short snippet each, drawn from real files:

- *Route-handler proxy test* — source pattern `src/frontend/src/app/api/routes/route.test.ts`. Import the exported `GET`/`POST` directly from `./route`; build a `Request`; `vi.stubGlobal('fetch', fetchMock)`; assert both directions — the forwarded backend URL, `Authorization` header and body, and the relayed status and JSON. `vi.unstubAllGlobals()` in `afterEach`. Anti-pattern to name: asserting only the relayed status, which lets a handler that drops the `Authorization` header pass.
- *Component test* — source pattern `src/frontend/src/components/RouteInfoPanel.test.tsx`. `vi.mock('@clerk/nextjs', …)` for anything under `ClerkProvider`; a local `makeRoute(overrides: Partial<RouteResult>)` factory so each case states only the field under test; query by ARIA role. Anti-pattern to name: querying by CSS class or DOM position, which breaks on every Tailwind edit while the behaviour holds.

Renumber the existing per-rollout-phase notes section to **§6.6**, and insert **§6.5 Adding an e2e test** as `TBD — see §3 Phase 5`.

#### 7. §7 Negative Space and §8 Freshness Ledger

**File**: `context/foundation/test-plan.md`

**Intent**: Add two exclusions, narrow one, and re-date the ledger.

**Contract**: `/dev` preview page and live ORS responses unchanged. MapLibre entry narrowed to canvas/snapshot assertions specifically, so it no longer reads as excluding browser tests that assert DOM — phase 5 depends on that distinction. Two additions: **Clerk's own UI** (do not test the vendor's components) and **anonymous rate-limit abuse** (PRD defers app-level throttling; testing it would require building the safeguard first — speculative). §8 dates all move to 2026-09-08, and the refresh-trigger list gains "a rollout phase's recorded status disagrees with CI".

### Success Criteria:

#### Automated Verification:

- Every hot-spot figure in §1 reproduces: `git log --since="90 days ago" --name-only --pretty=format: -- src/`
- Every version in §4 matches its manifest: `src/frontend/package.json`, `src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- Frontend case count in §4 matches: `grep -rE "^\s*(it|test)\(" src/frontend/src | wc -l` returns 47
- No `[ ]`-style TODO or `TBD` remains in §1–§5 (TBD is permitted only in §6.5)
- Markdown tables parse — no ragged column counts

#### Manual Verification:

- Every row of the "Current State Analysis" table above now describes the document's right-hand column, not its left
- §2 risk IDs 1–6 are byte-identical to the previous version except risk 2's Source addition, so §3's shipped phase rows stay truthful
- §3 phase 5 and 6 read as scoped goals a `/10x-new` change could be opened against without further interviewing
- §6.4 snippets are copied from the real test files, not invented

**Implementation Note**: Pause after this phase for manual confirmation before starting phase 2.

---

## Phase 2: Frontend CI gate

### Overview

Add a test job to the Azure Static Web Apps workflow so `npm test` gates deployment, then flip §3 phase 4 to `shipped` and sync the one document the change makes stale.

### Changes Required:

#### 1. SWA workflow

**File**: `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`

**Intent**: The 47 Vitest cases currently gate nothing — the workflow deploys without ever running them. Add a job that runs them and make deployment depend on it.

**Contract**: New job `test`, mirroring `backend.yml`'s test→deploy split: `runs-on: ubuntu-latest`; `actions/checkout@v4`; `actions/setup-node@v4` with `node-version: 'lts/*'` and `cache: 'npm'` plus `cache-dependency-path: src/frontend/package-lock.json`; `npm ci` and `npm test` both with `working-directory: src/frontend`.

`build_and_deploy_job` gains `needs: test`. Its existing `if:` guard is unchanged. `close_pull_request_job` must NOT gain the dependency — see Critical Implementation Details.

#### 2. Phase-4 status

**File**: `context/foundation/test-plan.md`

**Intent**: Close the row this phase completes.

**Contract**: §3 phase 4 status → `shipped`. §5's `unit + component (Vitest)` row annotation is already correct from phase 1 and needs no edit.

#### 3. Repository docs

**File**: `AGENTS.md`

**Intent**: Line 52 states "Frontend builds and deploys to Azure Static Web Apps on push to `main`" — true but now incomplete, since tests gate the deploy. The repo convention requires this to land in the same commit as the change that makes it stale.

**Contract**: Extend the CI sentence to state that frontend `npm test` must pass before the SWA deploy runs, matching how the same paragraph already describes the backend gate.

### Success Criteria:

#### Automated Verification:

- Workflow YAML parses and the job graph is valid: `gh workflow view "Azure Static Web Apps CI/CD"` (or a local `yq`/`actionlint` pass)
- `npm test` passes locally from `src/frontend/`: 47 cases green
- `npm ci` succeeds from a clean `node_modules` — proves the lockfile is complete for CI

#### Manual Verification:

- On a PR touching `src/frontend/**`, the `test` job appears and `build_and_deploy_job` waits for it
- With a deliberately failing frontend test on a branch, `build_and_deploy_job` is skipped and no preview environment deploys
- Closing that PR still runs `close_pull_request_job` successfully — confirming it did not inherit `needs: test`
- Revert the deliberate failure; deploy proceeds normally

---

## Testing Strategy

This change ships no application code, so there is no new test surface. Verification is documentary for phase 1 and behavioural for phase 2.

### Manual Testing Steps:

1. Read the refreshed `test-plan.md` top-to-bottom against the "Current State Analysis" table; every left-hand cell should be gone.
2. Re-run each §1 and §4 grounding command and diff against the recorded figures.
3. Open a throwaway PR touching `src/frontend/`, with one assertion inverted in `RouteInfoPanel.test.tsx`. Confirm `test` fails and deploy is skipped.
4. Restore the assertion, push, confirm both jobs go green and the preview environment appears.
5. Close the PR; confirm `close_pull_request_job` runs.

## Migration Notes

Not applicable — no data, no schema, no runtime behaviour changes.

The one operational consequence: after phase 2, a frontend PR with a failing test no longer produces a preview environment. That is the intent, but it changes the feedback shape for anyone used to previewing broken branches.

## References

- Discovery handoff (prior session, 2026-09-08): risk map, phase proposals, and negative space were accepted by the user there; this plan re-verified every factual claim in it against the repository before writing
- Document under refresh: `context/foundation/test-plan.md`
- Backend CI pattern to mirror: `.github/workflows/backend.yml`
- Untested endpoint noted for phase 5: `src/backend/VeloRoute/Program.cs:407`
- Frontend cookbook sources: `src/frontend/src/app/api/routes/route.test.ts`, `src/frontend/src/components/RouteInfoPanel.test.tsx`
- PRD (current scope): `context/foundation/prd-v2.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Refresh test-plan.md

#### Automated

- [x] 1.1 Every hot-spot figure in §1 reproduces from the 90-day git log command — b6658fb
- [x] 1.2 Every version in §4 matches its manifest — b6658fb
- [x] 1.3 Frontend case count in §4 matches the grep (47) — b6658fb
- [x] 1.4 No TODO or TBD remains in §1–§5 (TBD permitted only in §6.5) — b6658fb
- [x] 1.5 Markdown tables parse — no ragged column counts — b6658fb

#### Manual

- [x] 1.6 Every "Current State Analysis" row now describes the right-hand column — b6658fb
- [x] 1.7 Risk IDs 1–6 unchanged except risk 2's Source addition — b6658fb
- [x] 1.8 Phases 5 and 6 read as scoped, openable goals — b6658fb
- [x] 1.9 §6.4 snippets copied from real test files, not invented — b6658fb

### Phase 2: Frontend CI gate

#### Automated

- [x] 2.1 Workflow YAML parses and the job graph is valid — e310114
- [x] 2.2 `npm test` passes locally from `src/frontend/` — 47 cases green — e310114
- [x] 2.3 `npm ci` succeeds from a clean `node_modules` — e310114

#### Manual

- [x] 2.4 `test` job appears on a frontend PR and deploy waits for it — e310114
- [x] 2.5 A failing frontend test skips `build_and_deploy_job` — no preview deploys — e310114
- [x] 2.6 `close_pull_request_job` still runs on PR close — e310114
- [x] 2.7 Reverting the failure lets deploy proceed normally — e310114

#### Phase 2 CI verification evidence

Run against PR #21 (closed after verification; the two throwaway commits were dropped
from the branch afterwards, so the net diff is zero):

| Item | Run | Head | Result |
|---|---|---|---|
| 2.4 | 34282700196 | e310114 | `Frontend Tests` success 21:50:14 → `Build and Deploy Job` started 21:50:16, success |
| 2.5 | 34283695813 | e80f4e6 (inverted assertion) | `Frontend Tests` **failure** → `Build and Deploy Job` **skipped**, no preview |
| 2.7 | 34284003681 | e9b4553 (revert) | both jobs success; deploy completed 22:07:52 |
| 2.6 | 34284311902 | close event | `Close Pull Request Job` **success** while `Frontend Tests` and deploy were skipped |

2.6 is the decisive one: the close job ran green *while its would-be dependency was
skipped*. Had it inherited `needs: test`, the skipped dependency would have skipped it too.
