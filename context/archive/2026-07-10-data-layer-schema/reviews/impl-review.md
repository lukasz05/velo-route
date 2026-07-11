<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Data Layer Schema Implementation Plan

- **Plan**: context/changes/data-layer-schema/plan.md
- **Scope**: Full plan (Phases 1–3)
- **Date**: 2026-07-11
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Notes

- **Justified drift**: `Program.cs`'s auto-migrate block adds a connection-string-presence guard (commit `2b1d93e`) beyond the plan's literal snippet. Narrow, additive, matches the existing empty-string-placeholder pattern already used for `ORS`/`Clerk`. Hardens rather than expands the plan's contract — not scope creep.
- **Local test run**: `dotnet test` locally: 59/65 pass, 3 Testcontainers-backed tests fail due to a Docker Desktop API version mismatch (client 1.44 vs Testcontainers' expected max 1.41) — an environment issue, not a code defect. CI (`ubuntu-latest`, compatible Docker) already confirmed passing at SHA `9f9ccac` (Phase 2 manual verification).
- **Unplanned byproduct**: `src/backend/dotnet-tools.json` (new) is not an explicit "Changes Required" item but is a direct, expected byproduct of Phase 1 #6's instruction to install `dotnet-ef` as a local tool. Not scope creep.
- All "What We're NOT Doing" boundaries confirmed intact: no route save/list/delete endpoints beyond pre-existing `/routes/loop`/`/routes/gpx`, no PostGIS, no CI workflow file changes, no Key Vault, no email/profile caching on `users`.

## Findings

### F1 — GeoJsonLineString.Type has no value constraint

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Data/GeoJsonLineString.cs
- **Detail**: `Type` is an unconstrained string, not validated to equal `"LineString"`. Harmless now — nothing writes/validates externally-supplied geometry yet (schema-only slice, no save endpoint).
- **Fix**: Not needed this slice. Worth a check when S-01 (save-route endpoint) starts accepting externally-supplied geometry input.
- **Decision**: SKIPPED — deferred to S-01 (save-route endpoint), where external geometry input first arrives.
