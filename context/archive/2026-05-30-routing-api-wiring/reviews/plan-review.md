<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Routing API Wiring Implementation Plan

- **Plan**: `context/changes/routing-api-wiring/plan.md`
- **Mode**: Deep
- **Date**: 2026-05-30
- **Verdict**: REVISE → SOUND (all findings fixed during triage)
- **Findings**: 2 critical  1 warning  0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | FAIL → FIXED (F1) |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING → FIXED (F3) |
| Plan Completeness | FAIL → FIXED (F2) |

## Grounding

5/5 paths ✓, 3/3 symbols ✓, brief↔plan ✓. No contract-surfaces.md (skipped).

## Deep Verification Summary

All three riskiest claims verified:
- ORS `/v2/directions/cycling-road/geojson` endpoint path ✅ correct
- `Microsoft.Extensions.Http.Resilience` package + `AddResilienceHandler` API ✅ correct for .NET 10
- ASP.NET Core minimal API camelCase JSON default ✅ confirmed

One correction found: `"waytypes"` in ORS request `extra_info` should be `"waytype"` (no trailing s).

## Findings

### F1 — ORS request body uses wrong extra_info value

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Critical Implementation Details + Phase 1 Change 6
- **Detail**: Plan wrote `"extra_info": ["surface","waytypes"]` but correct request value is `"waytype"` (no trailing s). Would cause all RoadClass values to be Unknown, failing Phase 2 manual criterion 2.3.
- **Fix**: Changed `"waytypes"` → `"waytype"` in Critical Implementation Details bullet 3 and Phase 1 Change 6 Contract.
- **Decision**: FIXED

### F2 — Phase 1 Progress missing one Manual Verification entry

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 Success Criteria → Manual Verification + ## Progress
- **Detail**: Second Phase 1 Manual Verification bullet ("IOpenRouteServiceClient visible in DI — optional") had no matching Progress entry. Startup success (1.2) already covers DI registration.
- **Fix**: Removed the optional criterion from the phase body.
- **Decision**: FIXED

### F3 — No step creates .env.local before Phase 3 manual verification

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 Changes Required + Manual Verification
- **Detail**: Plan created .env.example but no step instructed creation of .env.local. Implementer would hit silent fetch failure (URL becomes "undefined/routes/preview").
- **Fix**: Added step 1b in Phase 3 Changes Required to create `.env.local` from `.env.example`; added Progress entry 3.5 and Phase 3 Manual Verification bullet.
- **Decision**: FIXED
