<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Loop Algorithm Quality Tuning

- **Plan**: context/changes/loop-algorithm-tuning/plan.md
- **Scope**: All Phases (1–4)
- **Date**: 2026-06-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical  4 warnings  4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Findings

### F1 — Degree-space segment length biases PavedRatio

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Routing/PavedRatioCalculator.cs:28–31
- **Detail**: Plan claimed "unit cancellation means the approximation is exact." Only holds if paved/unpaved segments share the same directional distribution. At 52°N, 1° longitude ≈ 0.62° latitude in metric length. East-west paved + north-south unpaved can miscount by up to ~35%.
- **Fix A ⭐ Applied**: `dx = (B.Lon-A.Lon) * Math.Cos(avgLat_radians)` before sqrt. Same pattern OverlapDetector uses.
- **Decision**: FIXED via Fix A

### F2 — Uncorrected longitude degrees skew SmoothnessScore

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Routing/SmoothnessCalculator.cs:23–24
- **Detail**: Bearing() used raw degree differences. At 52°N, a true 45° NE track computed as ~31.8° instead of 45°. Angular deltas between adjacent diagonal segments were distorted, causing false sharp-turn counts.
- **Fix A ⭐ Applied**: Latitude-corrected Bearing() helper: `dx = (b.Lon - a.Lon) * Math.Cos(latAvg_radians)`.
- **Decision**: FIXED via Fix A

### F3 — OrsLiveSmokeTests uses 0.40 overlap threshold, not plan's 0.10

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/backend/VeloRoute.Tests/Routing/OrsLiveSmokeTests.cs:18
- **Detail**: Plan specified `overlapRatio ≤ 0.10`. Actual `MaxOverlapRatio = 0.40` (fallback threshold). Mazury test uses Olsztyn (53.78°N, 20.49°E) not plan's Mrągowo (53.87°N, 21.57°E).
- **Fix B Applied**: Plan updated to document 0.40 as intentional (calibration showed live ORS routes use fallback path) and Olsztyn as the actual test location.
- **Decision**: FIXED via Fix B (plan updated)

### F4 — OrsLiveSmokeTests silently fails when API key absent

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/backend/VeloRoute.Tests/Routing/OrsLiveSmokeTests.cs
- **Detail**: If someone removes `[Fact(Skip)]` and runs without ORS:ApiKey, test fails with confusing HTTP 401/503 assertion error.
- **Fix Applied**: First status assertion now emits clear "ORS:ApiKey not configured" message on 401.
- **Decision**: FIXED

### F5 — Phase 3 manual checkboxes unchecked despite calibration done

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/loop-algorithm-tuning/plan.md (Progress §Phase 3)
- **Detail**: 3.3 and 3.4 were unchecked but calibration.md existed and 4.6 was checked.
- **Fix Applied**: Marked 3.3 and 3.4 as [x] in the Progress section.
- **Decision**: FIXED

### F6 — pavedRatio === 0 guard conflates missing data with genuine 0% paved

- **Severity**: 👁 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/components/RouteInfoPanel.tsx:52
- **Detail**: `route.pavedRatio === 0 ? 'Unknown'` displays "Unknown" for a genuinely 0% paved route (all gravel), not just for missing segment data.
- **Fix Applied**: Changed to `route.segments.length === 0 ? 'Unknown'`.
- **Decision**: FIXED

### F7 — BboxAspectRatio helper duplicated across two test files

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute.Tests/Routing/RouteQualityTests.cs + OrsLiveSmokeTests.cs
- **Detail**: Verbatim duplicate of `BboxAspectRatio` in both files; project centralises shared test infra in TestInfrastructure.cs.
- **Fix Applied**: Moved to `RouteTestHelpers.BboxAspectRatio` in TestInfrastructure.cs; both test files updated to use shared version.
- **Decision**: FIXED

### F8 — LoopRouteGenerator fallback error path may be unreachable

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:~99
- **Detail**: `firstError ?? Failure(...)` — concern was that `RoutingResult<T>` might be a struct. Verified: it is a sealed class. Null-coalescing is valid; branch is reachable.
- **Decision**: DISMISSED — false alarm, type is a class
