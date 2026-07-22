<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Delete Route (S-04)

- **Plan**: context/changes/delete-route/plan.md
- **Scope**: Full plan (Phase 1 + Phase 2)
- **Date**: 2026-07-22
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Notes:
- Plan-drift sub-agent: MATCH on all 8 changed files, no missing pieces. Three developer-approved cosmetic UI deviations noted (list delete button repositioned next to date instead of absolute-overlaying it; muted/outline red instead of solid red-600 fill on both ConfirmModal's confirm button and the detail-page delete button; detail-page delete button shrunk full-width→auto-width) — approved live during manual QA, not drift.
- Safety/pattern sub-agent: no CRITICAL/WARNING findings. DELETE endpoint scoping verified correct (same 404-collapse ownership pattern as existing GET /routes/{id}, explicitly tested for the cross-owner case). 404-as-success race handling and hard-delete-no-undo are per plan intent, not flagged.
- Automated success criteria: backend build clean, `dotnet test` 81 passed / 3 skipped (live ORS smoke tests, expected); frontend build clean, lint clean, `npm test` 20/20 passed.
- Manual success criteria: all Progress-section manual rows already `[x]` with commit SHAs from the implementation phases (1.4-1.5 → cfadf20; 2.4-2.9 → 24c7bcf) — confirmed by the developer live during implementation, not rubber-stamped.

## Findings

### F1 — Unreachable success branch in DELETE proxy handler

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/app/api/routes/[id]/route.ts:73-75
- **Detail**: The final `res.ok` branch (parse JSON body, relay as 2xx) was copied from the GET handler. The backend DELETE endpoint only ever returns 204 or an error status, so this branch can never execute.
- **Fix**: Drop the branch (or leave a one-line comment noting it's intentionally-unreachable defensiveness).
- **Decision**: FIXED

### F2 — Duplicated MakeRoute test helper

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute.Tests/Routing/DeleteRouteTests.cs:16-24
- **Detail**: `MakeRoute` is copied verbatim from RouteLibraryTests.cs rather than shared via TestInfrastructure.cs.
- **Fix**: Move to TestInfrastructure.cs if/when a third test class needs it; not worth churn for two callers today.
- **Decision**: FIXED (moved to RouteTestHelpers.MakeRoute in TestInfrastructure.cs, both DeleteRouteTests.cs and RouteLibraryTests.cs updated)

### F3 — ConfirmModal lacks dialog a11y semantics

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/components/ConfirmModal.tsx
- **Detail**: No `role="dialog"`/`aria-modal`, no focus trap, no Escape-to-cancel. ErrorMessage.tsx establishes a codebase precedent of attending to a11y (`role="alert"`). This component is explicitly written for reuse by account-deletion (S-06), so the gap compounds.
- **Fix**: Add `role="dialog"` + `aria-modal="true"` + an Escape-key handler calling `onCancel`. Skip a full focus-trap for now — low blast radius with only two buttons.
- **Decision**: FIXED
