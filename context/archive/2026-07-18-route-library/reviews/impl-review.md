<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Route Library (S-03)

- **Plan**: context/changes/route-library/plan.md
- **Scope**: Phase 1 + Phase 2 (full plan)
- **Date**: 2026-07-18
- **Verdict**: APPROVED (2 minor warnings, both triaged and fixed)
- **Findings**: 0 critical, 2 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Automated verification

- Backend build: `dotnet build VeloRoute/VeloRoute.csproj` — pass
- Backend tests: `DOCKER_API_VERSION=1.41 dotnet test VeloRoute.Tests/VeloRoute.Tests.csproj` — 77 passed, 3 skipped (live ORS smoke tests)
- Frontend build: `npm run build` — pass
- Frontend lint: `npm run lint` — pass
- Frontend tests: `npm test` — 12 passed (11 pre-existing + 1 added for F1's 400 path)

## Manual verification

Confirmed by user during this session: 2.4 (signed-out redirect), 2.5 (empty state), 2.6 (list newest-first), 2.7 (detail page), 2.8 (GPX download), 2.9 (header link), 2.10 (anonymous flows unaffected). Two UX issues found during manual testing and fixed separately (pre-review): save-form-disabled-after-regenerate bug (commit 8487e2e), and My Routes list styling.

## Findings

### F1 — Unvalidated id interpolated into backend proxy URL

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/app/api/routes/[id]/route.ts:11 (pre-fix)
- **Detail**: `id` came straight from the dynamic route param and was spliced into `` `${apiUrl}/routes/${id}` `` with no validation or encoding. An encoded slash (%2F) could rewrite the forwarded path. Blast radius was small (backend's `{id:guid}` constraint + auth on sensitive routes), but no sibling proxy forwarded unchecked path segments this way.
- **Fix A ⭐ Recommended** (applied): Validate `id` against a GUID regex before building the URL; return 400 `{code: "INVALID_ID"}` if it doesn't match.
  - Strength: Matches the backend's own `{id:guid}` route constraint — fails fast locally.
  - Tradeoff: A few extra lines, one regex constant.
  - Confidence: HIGH — GUID shape is already the contract.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix A). Added `GUID_PATTERN` validation + 400 response in `route.ts`; updated `route.test.ts` fixtures to use real GUID literals and added a new test for the 400/malformed-id path (12 tests now, was 11).

### F2 — Unplanned RouteInfoPanel.tsx fix riding on this branch

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/frontend/src/components/RouteInfoPanel.tsx:33-38
- **Detail**: Commit 8487e2e (fixes S-02 save-route bug: `isSaved`/name/tags state persisted across route regeneration, leaving the save form disabled until refresh) isn't in the route-library plan's file list or contracts — it's an unrelated S-02 fix that landed on this branch instead of its own change folder, per the repo's branch-per-change convention. The fix itself was verified correct (effect fires only on genuine `route` identity change, no loop, no stale closure) — process note, not a code defect.
- **Fix** (applied): Documented the out-of-scope commit in `route-library/change.md` Notes.
- **Decision**: FIXED

## Notes

RouteInfoPanel's regeneration-reset `useEffect` (commit 8487e2e) was independently verified safe by the safety/pattern review agent: fires only on `route` prop identity change (not on unrelated re-renders), no infinite loop, no stale closure.
