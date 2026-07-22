# Delete Route (S-04) Implementation Plan

## Overview

Add an authenticated `DELETE /routes/{id}` backend endpoint (hard delete, scoped to the caller), and wire a delete affordance into both `/my-routes` (list row) and `/my-routes/[id]` (detail page), each behind a new reusable confirmation modal — the first confirm dialog in the app.

## Current State Analysis

- `route-library` (S-03) shipped `GET /routes` (list) and `GET /routes/{id}` (detail) — no write/delete endpoints exist beyond `POST /routes` (save).
- The detail endpoint (`Program.cs:169-183`) already establishes the exact scoping pattern this plan reuses: `db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub)`, collapsing "id doesn't exist" and "id belongs to another user" into a single `404 { error: "Route not found", code: "NOT_FOUND" }` — no `403`, avoiding id-existence leakage.
- `src/frontend/src/app/api/routes/[id]/route.ts` currently exports only `GET`. It already has a `GUID_PATTERN` regex (added in route-library's impl-review) validating the dynamic `id` segment before it's used — this plan adds a `DELETE` export to the same file, reusing that constant.
- `my-routes/page.tsx`'s list rows are each a single `<Link href="/my-routes/<id>">` wrapping the whole row (name, date, distance, tags) — a delete button placed inside that row must stop the click from also triggering the Link's navigation.
- `my-routes/[id]/page.tsx` is a Client Component reading `id` via `useParams()`, with the same auth-gate pattern as the list page (`!isLoaded` → render nothing; not signed in → `router.replace('/')` + `openSignIn()`).
- No confirmation-dialog component exists anywhere in the frontend (`grep` for `confirm(` and for any `Modal`/`Dialog` component returns nothing) — this is the first one. `ErrorMessage.test.tsx` is the only existing component-level (`.test.tsx`) test in the repo, establishing that React Testing Library component tests are a supported pattern here, not just proxy-route unit tests.
- PRD (`prd-v2.md:99`): "Authenticated user can delete a saved route from their library after confirming a prompt (hard delete, no recovery)." Socrates note explicitly rejects soft-delete as unjustified complexity for v2 — a confirmation prompt alone is the accepted safeguard.

## Desired End State

A signed-in user can delete a saved route either from its row in `/my-routes` or from the open `/my-routes/<id>` detail page. Either path opens the same confirmation modal naming the route; confirming issues the delete and the route is gone permanently (hard delete). Deleting from the list removes just that row in place once the backend confirms (no full-list reload); deleting from the detail page redirects to `/my-routes` on success. A delete that fails for a reason other than "already gone" shows inline error text and leaves the UI in its prior state, retryable.

Verify via: `DELETE /routes/{id}` removes the row from Postgres and returns 404 for both a nonexistent id and an id owned by a different caller; the UI round-trip (open My Routes with ≥1 saved route → delete from the list → row disappears; open a route → delete from detail → redirected to the list, route no longer appears) works manually against a local backend + Postgres.

### Key Discoveries:

- The 404-collapsing ownership check pattern (`Program.cs:174-176`) is now used by three endpoints (`GET /routes/{id}`, and this plan's `DELETE /routes/{id}`) — the delete endpoint's not-found branch is a straight copy of the detail endpoint's, no new logic.
- Treating a `404` response as a *successful* outcome on the client (per the failure-handling decision below) means the delete handler's success path and "already deleted elsewhere" path converge — no separate race-recovery UI needed.
- `RouteSummaryResponse`/`SavedRouteSummary` (used by the list) carries `id` and `name` already — the list row's delete button and modal need no new data fetch, only the id/name already in hand.

## What We're NOT Doing

- No soft-delete, undo, or trash/recovery window — PRD explicitly rejects this for v2.
- No bulk/multi-select delete — one route at a time, matching the PRD's single-route framing.
- No "type DELETE to confirm" or similar high-friction confirmation — a modal with explicit Cancel/Delete buttons is the agreed safeguard.
- No optimistic list-row removal — the row stays (in a disabled/loading state) until the backend confirms.
- No changes to `POST /routes` or the two `GET /routes[...]` endpoints beyond what's needed to keep them working alongside the new endpoint.

## Implementation Approach

Backend first (delete endpoint), then frontend (shared `ConfirmModal` component → proxy `DELETE` handler → wire into list page → wire into detail page), matching the layering established by `route-library`.

## Phase 1: Backend delete endpoint

### Overview

Add `DELETE /routes/{id:guid}` — caller-scoped hard delete, 404 if the route doesn't exist or isn't owned by the caller, 204 on success.

### Changes Required:

#### 1. Delete endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Permanently remove one of the caller's saved routes. Scoped so a caller can only ever delete their own routes, and so a delete attempt on someone else's route or a nonexistent id is indistinguishable from the outside (no id-existence leak), matching the detail endpoint's established behavior.

**Contract**: `DELETE /routes/{id:guid}`, `.RequireAuthorization()`. Missing/invalid `sub` claim → `401` (same extraction as the other `/routes` endpoints). Query `db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub)`; if `null`, return `404` with the same `{ error: "Route not found", code: "NOT_FOUND" }` shape used by the detail endpoint. On success, `db.Routes.Remove(route)`, `await db.SaveChangesAsync(ct)`, return `204 No Content` (`Results.NoContent()`).

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- New `DeleteRouteTests.cs` passes: no-token → 401; nonexistent id → 404; id owned by a different user → 404 (and the row is unaffected — still readable by its actual owner afterward); valid id → 204, and a subsequent `GET /routes/{id}` for the same id returns 404; a subsequent `GET /routes` for the caller no longer includes the deleted id.

#### Manual Verification:

- `curl -X DELETE http://localhost:5098/routes/<id>` with a valid test bearer token for a route owned by that user returns `204`, and the route no longer appears in Postgres (`SELECT * FROM "Routes" WHERE "Id" = '<id>'` returns no rows).
- `curl -X DELETE http://localhost:5098/routes/<id>` for a route owned by a different test user returns `404` and leaves the row untouched.

---

## Phase 2: Frontend delete UI

### Overview

Add a reusable confirmation modal, a `DELETE` proxy handler, and delete affordances on both the list and detail pages.

### Changes Required:

#### 1. Confirmation modal

**File**: `src/frontend/src/components/ConfirmModal.tsx` (new)

**Intent**: A small, generic confirm/cancel dialog — this feature's delete confirmation is its first use, but it's written reusable (title, message, confirm label, `onConfirm`/`onCancel` props) rather than delete-specific, since `account-deletion` (S-06) will need the same shape.

**Contract**: Client Component. Props: `{ title: string; message: string; confirmLabel: string; onConfirm: () => void; onCancel: () => void; isConfirming?: boolean }`. Renders as a fixed-position overlay (backdrop + centered panel) when mounted — the parent controls visibility by conditionally rendering the component, not via an internal `open` prop. Two buttons: Cancel (calls `onCancel`) and a confirm button (calls `onConfirm`, shows `isConfirming` as a disabled/loading state, styled distinctly — e.g. red/destructive — since its only current use is a destructive action).

**File**: `src/frontend/src/components/ConfirmModal.test.tsx` (new)

**Intent**: Cover the component in isolation, following `ErrorMessage.test.tsx`'s established component-test pattern.

**Contract**: Render with props, assert title/message text appears; click Cancel → `onCancel` called; click confirm → `onConfirm` called; `isConfirming: true` → confirm button disabled.

#### 2. Delete proxy route

**File**: `src/frontend/src/app/api/routes/[id]/route.ts` (add `DELETE` alongside existing `GET`)

**Intent**: Forward an authenticated delete request to the backend for one route id, relaying the Authorization header — same shape as the existing `GET` handler in this file, reusing its `GUID_PATTERN` validation.

**Contract**: `DELETE` handler, reading `id` from the dynamic route segment (same async-`params` handling as `GET`). Missing `Authorization` header → `401`. Invalid (non-GUID) `id` → `400 { code: "INVALID_ID" }` (same check as `GET`). Forwards to `${VELO_API_URL ?? 'http://localhost:5098'}/routes/${id}` with method `DELETE` and the `Authorization` header. On `204`, relay `204` with no body. On any other status (including `404`), relay the backend's status and JSON body, same parse-then-relay logic as the existing `GET`/`POST` handlers.

**File**: `src/frontend/src/app/api/routes/[id]/route.test.ts` (extend)

**Intent**: Cover the new `DELETE` handler alongside the existing `GET` tests in the same file.

**Contract**: Add cases: no-Authorization → 401; malformed id → 400; valid request forwards `DELETE` + Authorization header and relays `204`; backend `404` passthrough.

#### 3. List page delete

**File**: `src/frontend/src/app/my-routes/page.tsx`

**Intent**: Let the user delete a route directly from its list row without navigating to the detail page; the row shows a pending state during the request and is removed from the list only once the backend confirms (or reports the route is already gone).

**Contract**: Add a per-row delete button. Clicking it calls `preventDefault()`/`stopPropagation()` on the triggering event (the row is a `<Link>`) and opens `ConfirmModal` with the route's name. On confirm, `DELETE /api/routes/<id>` with the caller's bearer token; track the in-flight id so that row (and only that row) shows a disabled/pending state (`ConfirmModal`'s `isConfirming`) during the request. On `204` or `404` response, remove the route from local list state and close the modal. On any other failure, close the modal, leave the row in the list, and show inline error text scoped to that row (matching the existing inline-error convention used elsewhere on this page), re-enabling the row's controls.

#### 4. Detail page delete

**File**: `src/frontend/src/app/my-routes/[id]/page.tsx`

**Intent**: Let the user delete the currently-open route; since nothing remains to view afterward, a successful delete returns them to the list.

**Contract**: Add a "Delete route" button (styled distinctly from "Download GPX", e.g. destructive/red) in the existing left panel, below the download button. Clicking it opens `ConfirmModal` with the route's name. On confirm, `DELETE /api/routes/<id>`; on `204` or `404`, `router.replace('/my-routes')`. On any other failure, close the modal and show inline error text (same convention as the existing `downloadError` display), leaving the route displayed and the button re-enabled.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build`
- Lint passes: `npm run lint`
- Tests pass: `npm test` — `ConfirmModal` component tests; proxy route `DELETE` tests (401, 400 malformed id, 204 success + header forwarding, 404 passthrough)

#### Manual Verification:

- From `/my-routes`, deleting a route via its row's delete button shows a confirmation modal naming the route; confirming removes just that row from the list without a full page reload; the route no longer appears after a manual refresh either.
- From `/my-routes/<id>`, deleting the open route shows the same confirmation modal; confirming redirects to `/my-routes` and the route is no longer listed.
- Canceling the confirmation modal (from either surface) leaves the route untouched and visible.
- Deleting a route that was already deleted in another tab (simulate: delete via `curl`, then confirm delete in the UI) does not show an error — the row/redirect behaves as if the delete succeeded.
- A genuine delete failure (e.g. stop the backend mid-request) shows inline error text and leaves the route in place, retryable.
- Anonymous route generation, GPX export, save, and the rest of the My Routes flows (list view, detail view, GPX download) are unaffected.

---

## Testing Strategy

### Unit Tests:

- Backend: delete scoping (401 missing/invalid sub, 404 nonexistent id, 404 not-owned id, 204 + row actually removed from the database).
- Frontend: `ConfirmModal` render/cancel/confirm/loading-state behavior; proxy route's `DELETE` header-forwarding, validation, and status-relay branches (including 404-as-success semantics living in the *page* logic, not the proxy — the proxy just relays whatever the backend returns).

### Integration Tests:

- Backend `DELETE /routes/{id}` end-to-end against the Testcontainers Postgres fixture (`PostgresFixture`), seeding multiple users' routes to verify cross-user scoping and that the row is actually gone afterward — same fixture pattern as `RouteLibraryTests.cs`.

### Manual Testing Steps:

1. Sign in as a user with ≥2 saved routes; from `/my-routes`, click delete on one row, confirm in the modal; verify that row disappears and the other route remains.
2. Refresh `/my-routes`; verify the deleted route stays gone (not just removed from in-memory state).
3. Open a remaining saved route's detail page; click "Delete route", confirm; verify redirect to `/my-routes` and the route is no longer listed.
4. From either surface, open the confirmation modal and click Cancel; verify the route is untouched and still listed/viewable.
5. With a route open in one tab, delete the same route via `curl -X DELETE` in a terminal, then click delete + confirm in the open tab; verify no error is shown (treated as success).
6. Confirm anonymous route generation, GPX export, and the save flow are unaffected.

## Performance Considerations

None beyond what's already established — a single-row delete by primary key, no new query patterns.

## Migration Notes

None — no schema changes; the new endpoint operates on the existing `Routes` table.

## References

- Prerequisite endpoints + ownership pattern: `context/archive/2026-07-18-route-library/`
- Existing proxy-route + backend test patterns: `src/frontend/src/app/api/routes/[id]/route.test.ts`, `src/backend/VeloRoute.Tests/Routing/RouteLibraryTests.cs`
- PRD requirement: `context/foundation/prd-v2.md:99`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend delete endpoint

#### Automated

- [x] 1.1 Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- [x] 1.2 All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- [x] 1.3 New `DeleteRouteTests.cs` passes: 401, 404 nonexistent, 404 not-owned, 204 + row removed + follow-up GETs reflect the deletion

#### Manual

- [x] 1.4 `curl -X DELETE` with a valid test bearer token for an owned route returns 204 and removes the row from Postgres
- [x] 1.5 `curl -X DELETE` for a route owned by a different test user returns 404 and leaves the row untouched

### Phase 2: Frontend delete UI

#### Automated

- [ ] 2.1 Frontend builds: `npm run build`
- [ ] 2.2 Lint passes: `npm run lint`
- [ ] 2.3 Tests pass: `npm test` — ConfirmModal component tests, proxy DELETE tests

#### Manual

- [ ] 2.4 List-row delete: confirmation modal names the route; confirming removes just that row without a full reload; stays gone after refresh
- [ ] 2.5 Detail-page delete: confirmation modal; confirming redirects to `/my-routes`; route no longer listed
- [ ] 2.6 Canceling the modal (either surface) leaves the route untouched
- [ ] 2.7 Deleting an already-deleted route (simulated via curl race) shows no error — treated as success
- [ ] 2.8 A genuine delete failure shows inline error text and leaves the route in place, retryable
- [ ] 2.9 Anonymous generation, GPX export, save, and other My Routes flows unaffected
