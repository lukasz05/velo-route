<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Public Route Sharing (S-05)

- **Plan**: context/changes/public-route-sharing/plan.md
- **Scope**: Full plan (Phase 1 of 2, Phase 2 of 2)
- **Date**: 2026-07-26
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING (benign) |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Notes

- Phase 1 was already reviewed in an earlier session (see `impl-review-phase-1.md`); this review spot-checked that its one fix (F1: unique-violation catch, `SingleAsync` → `SingleOrDefaultAsync` + retry) is still present and correct in current `Program.cs` — confirmed.
- Phase 2 (frontend: types, both proxy routes + tests, detail-page share UI, public `/r/[token]` page) matches every "Changes Required" contract in the plan with no drift.
- Success criteria: backend build clean, 94/94 backend tests pass (3 skipped live-ORS smoke tests, expected); frontend build clean, lint clean, 32/32 frontend tests pass. All Phase 1 and Phase 2 manual verification items confirmed by the user, including a mid-phase bug (route line not rendering on either the detail page or the public page) found and fixed in `RouteMap.tsx`.
- Verified clean (no finding): share-token regex on the frontend proxy matches the backend's actual 12-char alphanumeric token generation exactly; `GET /shares/{token}` leaks no owner-only data (`RouteDetailResponse` has no `UserId`); `Shares` cascade-delete wired correctly at both EF model and migration level; all new fetch boundaries (proxy routes, public page, share/copy/revoke handlers) have try/catch with inline error state; `handleStopSharing` treats a 404 (already-revoked) as idempotent success, matching the plan's semantics.

## Findings

### F1 — Unplanned RouteMap.tsx fix (not in Phase 2 scope)

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/frontend/src/components/RouteMap.tsx
- **Detail**: Not listed in the plan's Phase 2 "Changes Required." Surfaced by manual verification step 2.5 (route line intermittently not rendering, on both the authenticated detail page and the new public page). Adds `isMapLoaded` state gated on a new `onLoad` handler on the `Map` component, deferring the existing `fitBounds`/`flyTo` effects until the underlying maplibre-gl map's `load` event fires, plus a `map.resize()` call before `fitBounds`. Diff is small and surgical; dependency arrays verified correct (no stale closure, no missed re-render — whichever of props/`onLoad` arrives last triggers the final effect run with current values). This is a genuine pre-existing race in a component shared by both pages, not scope creep.
- **Fix**: Optional — add a short addendum note to the plan documenting this as a fix discovered during manual verification. Not required for approval.
- **Decision**: FIXED (addendum added to plan.md Phase 2)

### F2 — resize() only added to the fitBounds path, not flyTo

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/components/RouteMap.tsx:33-42
- **Detail**: The `flyTo` effect (used for the plain `startPoint` case, e.g. the home page's search-driven pin) received the same `isMapLoaded` gate but not the `resize()` call added to the `fitBounds` path. Not a regression — matches pre-fix behavior for that path — but could show the same "camera lands wrong" symptom if that container is ever zero-sized at mount.
- **Fix**: Add `map.resize()` before `flyTo` too, for consistency. Only worth doing proactively if `flyTo` flakiness is ever actually reported.
- **Decision**: FIXED (resize() added before flyTo call)

### F3 — No regression test for the RouteMap.tsx load-gating fix

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/components/RouteMap.tsx (no co-located test file)
- **Detail**: Sibling components in the same directory (`ErrorMessage.test.tsx`, `ConfirmModal.test.tsx`) have co-located tests; `RouteMap.tsx` never has, both before and after this change. A test locking in "camera moves deferred until `onLoad` fires" would guard against this race regressing silently.
- **Fix**: Optional — mock `@vis.gl/react-maplibre`'s `Map` and assert `fitBounds`/`flyTo` aren't called before a stubbed `onLoad` fires. Low priority: gap pre-dates this change, and maplibre-backed components are awkward to test.
- **Decision**: FIXED (added `src/frontend/src/components/RouteMap.test.tsx`, 4 tests covering the load-gating for both `fitBounds` and `flyTo`)
