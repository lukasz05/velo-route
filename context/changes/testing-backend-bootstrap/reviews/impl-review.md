<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Backend Test Bootstrap

- **Plan**: context/changes/testing-backend-bootstrap/plan.md
- **Scope**: All phases (1–3)
- **Date**: 2026-06-05
- **Verdict**: APPROVED (after triage fixes)
- **Findings**: 0 critical | 2 warnings | 1 observation

## Verdicts

| Dimension | Verdict |
|---|---|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — ORS waytype codes 9 & 10 silently map to Unknown

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Routing/OrsMapper.cs:8-9 / OrsMapperTests.cs:45-66
- **Detail**: ORS API docs define waytype codes 9=Ferry and 10=Construction. RoadClass enum only went to 8 (Steps). Both silently fell through to Unknown — the exact bug pattern this change was designed to prevent.
- **Fix Applied**: Fix A — Added `Ferry = 9` and `Construction = 10` to RoadClass enum; added `[InlineData(9, RoadClass.Ferry)]` and `[InlineData(10, RoadClass.Construction)]` to OrsMapperTests.
- **Decision**: FIXED via Fix A

### F2 — BuildSegments has no happy-path test

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/Routing/OrsMapperTests.cs:68-92
- **Detail**: BuildSegments tested only for gap and empty cases. A boundary or midpoint bug on normal contiguous spans would have passed all tests.
- **Fix Applied**: Added `BuildSegments_ContiguousSpans_ReturnsCorrectSegments` — surfaceSpans=[[0,10,3],[10,20,10]], waytypeSpans=[[0,20,6]], asserts 2 segments with correct Surface and RoadClass.
- **Decision**: FIXED

### F3 — GPX locale assertions were substring-based

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/Routing/GpxSerializerTests.cs:25-29
- **Detail**: `Assert.Contains("48.20849", result)` would pass if the value appeared anywhere in the XML, not necessarily in a trkpt attribute.
- **Fix Applied**: Both locale tests now parse the XML with XDocument, navigate to the first trkpt element via the GPX namespace, and assert the `lat` attribute value directly.
- **Decision**: FIXED
