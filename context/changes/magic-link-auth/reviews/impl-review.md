<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Magic Link Auth (S-01)

- **Plan**: context/changes/magic-link-auth/plan.md
- **Scope**: Phase 1-2 of 2 (full plan)
- **Date**: 2026-07-15
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Unguarded null `sub` claim in /auth/sync

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:118-120
- **Detail**: `sub` is `string?`, passed straight into the parameterized INSERT with no null check. "Users"."Id" is NOT NULL and there's no global exception handler in Program.cs. If a validated token ever lacked `sub`, the insert throws an unhandled DbUpdateException → bare 500, no JSON body. OnTokenValidated only checks `azp`, not `sub` presence. SQL itself confirmed properly parameterized (FormattableString → EF auto-parameterizes) — this is purely about the null path, not injection.
- **Fix**: Add `if (sub is null) return Results.Unauthorized();` before the DB call.
- **Decision**: FIXED

### F2 — Missing error handling in Header.tsx sync effect

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/components/Header.tsx:13-19
- **Detail**: The async IIFE (`getToken()` → `fetch('/api/auth/sync', ...)`) has no try/catch and never checks `res.ok`. Every other fetch in this codebase (RouteApp.tsx:35-57) wraps calls in try/catch and surfaces failures. Not user-blocking (sync is fire-and-forget), but a network failure or `getToken()` throw produces a silent unhandled promise rejection with no diagnostic signal.
- **Fix**: Wrap the effect body in try/catch (best-effort — failure is non-fatal, just log or swallow).
- **Decision**: FIXED

### F3 — Unplanned /api/auth/sync proxy route

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/frontend/src/app/api/auth/sync/route.ts (new)
- **Detail**: Not in the plan's Phase 2 file list. Added because Header.tsx is a client component and VELO_API_URL is server-only — calling the backend directly would've required exposing it via NEXT_PUBLIC_ or hardcoding a URL client-side, breaking the existing invariant every other backend call in this codebase follows (routes/gpx, routes/loop both proxy the same way). Necessary consequence of the plan's own Header.tsx contract, not scope creep — arguably a plan gap rather than drift.
- **Fix**: Note the addition in plan.md as an addendum so the file list matches what actually shipped.
- **Decision**: FIXED

### F4 — /api/auth/sync drops backend response body

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/app/api/auth/sync/route.ts:18
- **Detail**: `return new Response(null, { status: res.status })` discards the backend's JSON error body. Sibling proxies (gpx/loop) parse and re-emit `{ error, code }` on non-2xx. Low practical impact — Header.tsx never reads the response — but hides error detail from any future consumer.
- **Fix**: Mirror gpx/loop's error-body relay for non-2xx responses.
- **Decision**: FIXED

### F5 — No test for sub-less token / concurrent first-sync race

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: src/backend/VeloRoute.Tests/Routing/AuthSyncTests.cs
- **Detail**: Naming/fixture/assertion style matches AuthMiddlewareTests.cs and UserRouteSchemaTests.cs — no pattern issue. But no test covers a validly-signed token missing `sub` (ties to F1), and idempotency is only tested sequentially, not under concurrent first-sync calls. Neither is required by the plan's contract.
- **Fix**: If F1's null guard is added, add a matching test case.
- **Decision**: FIXED
