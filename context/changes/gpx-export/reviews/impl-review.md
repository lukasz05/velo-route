<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: GPX Export

- **Plan**: context/changes/gpx-export/plan.md
- **Scope**: All Phases (1–3)
- **Date**: 2026-06-04
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 3 warnings · 1 observation

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

### F1 — Blob URL / anchor not cleaned up on error

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/components/RouteInfoPanel.tsx:25–34
- **Detail**: URL.revokeObjectURL and document.body.removeChild run only on the happy path. If anything throws after createObjectURL (DOM error, click failure), the object URL leaks and the anchor stays in the DOM.
- **Fix**: Wrap the Blob/anchor work in a nested try/finally so cleanup always runs.
  - Strength: Eliminates the leak class entirely; ~5 line change; no behaviour change on happy path.
  - Tradeoff: Minor — slightly more nesting.
  - Confidence: HIGH
  - Blind spot: None significant.
- **Decision**: FIXED — nested try/finally ensures cleanup on error path

### F2 — No per-coordinate range validation in POST /routes/gpx

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/Program.cs:99–105
- **Detail**: Endpoint only checks non-empty. Out-of-range or NaN/Infinity doubles produce malformed GPX that will fail to import into Strava/Garmin/Komoot.
- **Fix**: Add guard rejecting coordinates where lat is outside [-90,90], lon outside [-180,180], or value is not finite.
- **Decision**: FIXED — added double.IsFinite + range guard in Program.cs

### F3 — Non-JSON backend errors lose message in GPX proxy

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/app/api/routes/gpx/route.ts:21–29
- **Detail**: On a non-OK plain-text backend response (e.g. ASP.NET 500), the proxy discards the message body and returns a generic string.
- **Fix**: Fall back to res.text() for the message when JSON.parse fails.
- **Decision**: SKIPPED — current generic message is correct security posture; raw error bodies could leak server internals

### F4 — Silent download failures give no user feedback

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/components/RouteInfoPanel.tsx:15–37
- **Detail**: If the GPX fetch fails, loading state resets but user sees nothing. RouteApp already has an ErrorMessage component and error pattern available.
- **Fix**: Catch download errors and surface a local error state in the component.
- **Decision**: FIXED — added downloadError state + inline red error message below button
