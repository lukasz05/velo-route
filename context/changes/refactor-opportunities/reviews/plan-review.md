<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Refactor Opportunities — Implementation Plan

- **Plan**: context/changes/refactor-opportunities/plan.md
- **Mode**: Deep
- **Date**: 2026-09-11
- **Verdict**: REVISE → SOUND (all findings fixed)
- **Findings**: 1 critical, 2 warnings, 1 observation — all FIXED

## Verdicts (pre-fix)

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | FAIL |

## Grounding

10/10 paths ✓, 4/4 symbols ✓, brief↔plan ✓. Deep-mode sub-agent verified: get-only computed properties are included in `Microsoft.AspNetCore.OpenApi` schema output (confirmed via `dotnet/aspnetcore#58192`); schema naming policy matches actual HTTP response casing (both read `Microsoft.AspNetCore.Http.Json.JsonOptions`); `openapi-typescript` v7 CLI accepts a live URL as input (confirmed via official docs); no `IsDevelopment()` block ordering conflict from extracting `MapOpenApi()`.

## Findings

### F1 — Progress section omits Manual Verification for Phase 1 & 2, but their Phase blocks carry a "None" bullet under it

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 & Phase 2 (Manual Verification) / Progress section
- **Detail**: Progress↔Phase mechanical contract requires every bullet under `#### Manual Verification:` to have a matching Progress checkbox. Phase 1/2 carried a "- None" bullet with no Progress match — `/10x-implement`'s parser could choke on this.
- **Fix**: Removed the `#### Manual Verification` heading + "- None ..." bullet from Phase 1 and Phase 2 entirely.
- **Decision**: FIXED

### F2 — Phase 3's automated check doesn't test the actual fix (non-Development reachability)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 — Success Criteria
- **Detail**: The automated curl check ran against a plain `dotnet run` (defaults to Development) — passed trivially before and after. The actual regression (reachability outside Development) was verified manually only.
- **Fix A ⭐ Applied**: Added `OpenApiExposureTests.cs` — xUnit integration test via `VeloRouteWebApplicationFactory.WithWebHostBuilder(...)` overriding environment to "Production" plus in-memory Clerk config (required because `Program.cs:105-112`'s config-validation block fires for any non-Development environment), asserting `GET /openapi/v1.json` → 200.
- **Decision**: FIXED

### F3 — Two RouteResult-shaped literals outside the plan's known blast radius

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 4 — Changes Required
- **Detail**: `src/frontend/e2e/fixtures/route.ts:18-38` and `src/frontend/src/components/RouteInfoPanel.test.tsx:11-23` both hand-construct full `RouteResult` object literals, not accounted for in the plan's "3 cast sites" framing.
- **Fix**: Added both files to Phase 4's Changes Required as known blast-radius check sites (check, not necessarily edit) — a `tsc`/`npm test` failure there is expected-and-handled, not a surprise.
- **Decision**: FIXED

### F4 — Unauthenticated prod exposure of /openapi/v1.json not discussed

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 — Changes Required, item 1
- **Detail**: `app.MapOpenApi()` carries no `.RequireAuthorization()` — un-gating it exposes the schema publicly in prod, unstated as a conscious choice.
- **Fix**: Added one sentence to Phase 3's Intent confirming this is accepted, consistent with the app's already-anonymous public API surface.
- **Decision**: FIXED
