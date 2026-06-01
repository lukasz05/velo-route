<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Loop Route Generation

- **Plan**: context/changes/loop-route-generation/plan.md
- **Scope**: All phases (1–4)
- **Date**: 2025-07-14
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — ORS API key sent as query parameter

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/app/api/geocode/route.ts:15
- **Detail**: Key was interpolated into the URL as `&api_key=`, visible in server/proxy logs. ORS supports Authorization header.
- **Fix**: Remove api_key from URL; add `headers: { Authorization: apiKey }` to fetch call.
- **Decision**: FIXED

### F2 — OverlapDetector ratio can exceed 1.0

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/Routing/OverlapDetector.cs
- **Detail**: STRtree candidate query can return the same segment multiple times. `overlappingLength` could exceed `totalLength`, producing ratio > 1.0, corrupting best-pick scoring.
- **Fix B (applied)**: Track matched segment indices via `HashSet<int>`; each segment counted at most once.
  - Strength: Eliminates double-counting at the source; ratio stays mathematically correct.
  - Tradeoff: Slightly more code; index uniqueness verified.
  - Confidence: MEDIUM
  - Blind spot: Tests not yet in place.
- **Decision**: FIXED via Fix B

### F3 — No lat/lon validation on POST /routes/loop

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/Program.cs (POST /routes/loop handler)
- **Detail**: lat/lon values passed directly to ORS without bounds check. Invalid coords surface as 500/timeout.
- **Fix**: Added bounds check — reject lat outside [-90, 90] or lon outside [-180, 180] with 400 INVALID_INPUT.
- **Decision**: FIXED

### F4 — Fallback drops distance-range filter

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/backend/Routing/LoopRouteGenerator.cs
- **Detail**: Plan says fallback relaxes overlap threshold only. Actual code dropped distance filter too — a candidate 3× too long could be returned.
- **Fix A (applied)**: Keep distance-range filter in fallback; only relax overlap threshold. Order fallback by lowest overlap ratio, then closest distance.
  - Strength: Closest to plan intent; user gets a loop in the requested km range.
  - Tradeoff: May fail if all candidates violate distance range.
  - Confidence: HIGH
  - Blind spot: No data on fallback fire rate.
- **Decision**: FIXED via Fix A

### F5 — RouteApp layout uses md:h-screen instead of h-screen

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/frontend/src/components/RouteApp.tsx
- **Detail**: Plan specified `h-screen` on root container. Actual: `md:h-screen` (desktop-only). Intentional fix during Phase 4 to avoid hiding content below fold on mobile.
- **Decision**: ACCEPTED (intentional deviation)

### F6 — No AbortController on route generation fetch

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/components/RouteApp.tsx
- **Detail**: SearchBar uses AbortController for stale-request prevention; RouteApp did not — slow response could overwrite newer state.
- **Fix**: Added `useRef<AbortController>`, abort previous at start of each generation, swallow `AbortError` in catch.
- **Decision**: FIXED
