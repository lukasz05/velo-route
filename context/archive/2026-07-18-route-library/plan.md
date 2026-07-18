# Route Library (S-03) Implementation Plan

## Overview

Add the "My Routes" library: an authenticated `GET /routes` backend endpoint returning a summary list of the caller's saved routes, an authenticated `GET /routes/{id}` endpoint returning one route's full detail (including geometry), and two new Next.js pages — `/my-routes` (flat list) and `/my-routes/[id]` (map + GPX download) — plus a "My Routes" link in the header.

## Current State Analysis

- `save-route` (S-02) shipped `POST /routes` and the `Routes` table (`Data/Route.cs`: `Id, UserId, Name, Tags, DistanceKm, Geometry, CreatedAt`) — no read endpoints exist yet.
- The `Route` entity has **no** `segments`/`pavedRatio`/`smoothnessScore` — those are computed only at generation time by `LoopRouteGenerator` and never persisted. The saved-route detail view therefore cannot show "Surface quality" the way the live-generation `RouteInfoPanel` does.
- `RouteMap.tsx` (`src/frontend/src/components/RouteMap.tsx`) takes generic `{startPoint, routeCoordinates}` props and is directly reusable for the detail page unchanged.
- `/api/routes/gpx` (`src/frontend/src/app/api/routes/gpx/route.ts`) is a stateless proxy that accepts `{coordinates}` and returns GPX text — directly reusable for the detail page's download button, identical to `RouteInfoPanel.handleDownload`.
- The authenticated-proxy pattern (`Authorization: Bearer <token>` forwarded through a Next.js route handler to the backend) is established by `/api/auth/sync` and `/api/routes` (`POST`); this plan extends `/api/routes/route.ts` with a `GET` handler and adds a new `/api/routes/[id]/route.ts`.
- No frontend page currently gates on auth state — `Header.tsx` only toggles nav controls. This plan introduces the first auth-gated page.
- Route-scoping precedent: `sub` claim extraction (`ClaimTypes.NameIdentifier` falling back to `"sub"`) is identical across `/auth/sync` and `POST /routes` (`Program.cs`) — the new endpoints reuse it unchanged.

## Desired End State

A signed-in user with saved routes navigates to `/my-routes` (via a new header link) and sees a flat list sorted by save date (newest first), each row showing name, date, distance, and tags. Clicking a row opens `/my-routes/<id>`, showing the route on an interactive map with its name/tags/distance and a "Download GPX" button that produces the same GPX file as the original save. A user with zero saved routes sees a friendly empty state linking back to the planner. A signed-out user hitting either URL directly is redirected to `/` and shown the sign-in modal. Anonymous route generation, GPX export, and the save flow are unaffected.

Verify via: `GET /routes` returns only the caller's own routes, summary fields only, sorted newest-first; `GET /routes/{id}` returns 404 for a nonexistent id and for an id owned by a different caller; the UI round-trip (save a route → open My Routes → open it → see it on the map → download GPX) works manually against a local backend + Postgres.

### Key Discoveries:

- `RouteCoordinate` (`Routing/RouteResult.cs`) is the shared coordinate shape already used by `POST /routes`, `/routes/gpx`, and the frontend's `RouteResult.geometry.coordinates` — the detail endpoint reuses it for its response geometry, so the frontend's GPX-download code path needs no new coordinate mapping.
- Both list and detail auth-scoping follow the same `sub`-based `WHERE UserId = sub` filter — the detail endpoint's ownership check is just "does a row with this id AND this UserId exist," collapsing the not-found and not-owned cases into a single 404 (no `403` — avoids leaking whether an id exists to a non-owner).
- Next.js 15's async-`params` breaking change only affects Server Component page props; both new pages are Client Components (they need `useAuth`/`useUser`/`useClerk` for the auth gate and GPX download), so the detail page reads its `id` via the synchronous `useParams()` hook from `next/navigation` instead.

## What We're NOT Doing

- No delete (S-04) or public sharing (S-05) — separate roadmap slices.
- No pagination, search, or filter — flat list only, matches PRD's explicit v2 scope.
- No post-save editing of name/tags from the library (would require an update endpoint not in scope).
- No "Surface quality" section on the detail view — that data was never persisted; the detail panel is a new, smaller component rather than a conditionally-degraded `RouteInfoPanel`.
- No dedicated `GET /routes/{id}/gpx` backend endpoint — the detail page reuses the existing `/api/routes/gpx` proxy client-side with the geometry it already fetched.

## Implementation Approach

Backend first (list → detail endpoint), then frontend (list page → detail page → header link), matching the layering established by `save-route`. Both new pages are Client Components using the existing Clerk hooks; the list page's auth gate and the detail page's auth gate share the same redirect-then-open-modal logic.

## Phase 1: Backend library endpoints

### Overview

Add `GET /routes` (caller's routes, summary fields, newest-first) and `GET /routes/{id}` (one route's full detail, 404 if missing or not owned by caller).

### Changes Required:

#### 1. List endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Return the caller's saved routes as a lightweight summary list — no geometry — so the library page can render within the PRD's 2-second budget without loading every row's full coordinate array.

**Contract**: `GET /routes`, `.RequireAuthorization()`, mapped alongside the existing `/routes` `POST`. Missing/invalid `sub` claim → `401` (same extraction as `POST /routes`). On success, query `db.Routes.Where(r => r.UserId == sub).OrderByDescending(r => r.CreatedAt)`, project to `{ id, name, tags, distanceKm, createdAt }` (no `geometry`), return `200` with the array.

#### 2. Detail endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Return one saved route's full detail, including geometry, for the map view and GPX download — scoped so a caller can only ever retrieve their own routes.

**Contract**: `GET /routes/{id:guid}`, `.RequireAuthorization()`. Missing/invalid `sub` claim → `401`. Query `db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub)`; if `null` (either the id doesn't exist or it belongs to another user), return `404` with `{ error: "Route not found", code: "NOT_FOUND" }` — same shape as the existing validation-error responses. On success, return `200` with `{ id, name, tags, distanceKm, geometry: { coordinates: [{longitude, latitude}, ...] }, createdAt }`, converting `Geometry.Coordinates` (`double[][]`, `[lon, lat]`) back to the `RouteCoordinate` list shape via `new RouteCoordinate(c[0], c[1])` per pair — the inverse of `POST /routes`'s conversion.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- New library-endpoint tests pass: list — no-token → 401, caller with zero routes → 200 empty array, caller with routes only sees their own (not another user's), results sorted newest-first, summary fields only (no geometry key in the JSON); detail — no-token → 401, nonexistent id → 404, id owned by a different user → 404, valid id → 200 with correct geometry round-trip (coordinates match what was saved)

#### Manual Verification:

- `curl http://localhost:5098/routes` with a valid test bearer token returns the seeded user's routes newest-first
- `curl http://localhost:5098/routes/<id>` for a route owned by a different test user returns `404`

---

## Phase 2: Frontend library pages

### Overview

Add `/my-routes` (list) and `/my-routes/[id]` (detail) pages, two new authenticated Next.js proxy routes, and a "My Routes" header link.

### Changes Required:

#### 1. List + detail proxy routes

**File**: `src/frontend/src/app/api/routes/route.ts` (add `GET` alongside existing `POST`)

**Intent**: Forward an authenticated list request to the backend, relaying the Authorization header — same shape as the existing `POST` handler in this file, no body.

**Contract**: `GET` handler. Missing `Authorization` header → `401 { error, code: "UNAUTHORIZED" }`. Forwards to `${VELO_API_URL ?? 'http://localhost:5098'}/routes` with the `Authorization` header, relays the backend's JSON array and status on success, relays `{ error, code }` and status on failure (same parse-then-relay logic as the existing `POST` handler).

**File**: `src/frontend/src/app/api/routes/[id]/route.ts` (new)

**Intent**: Forward an authenticated detail request to the backend for one route id, relaying the Authorization header.

**Contract**: `GET` handler, reading `id` from the dynamic route segment. Missing `Authorization` header → `401`. Forwards to `${VELO_API_URL ?? 'http://localhost:5098'}/routes/${id}` with the `Authorization` header, relays the backend's JSON body and status (including `404` for not-found-or-not-owned) on both success and failure paths.

#### 2. Library list page

**File**: `src/frontend/src/app/my-routes/page.tsx` (new)

**Intent**: Show the signed-in user's saved routes as a flat, newest-first list; gate the page behind sign-in; handle the empty-library case.

**Contract**: Client Component. On mount, if `useUser().isLoaded` and `!isSignedIn`, redirect to `/` (`useRouter().replace('/')`) and call `useClerk().openSignIn()`, rendering nothing further. While `!isLoaded`, render nothing (avoid a flash of gated content). Once signed in, fetch `GET /api/routes` with `Authorization: Bearer <token>` from `useAuth().getToken()`; while in flight, show a simple "Loading your routes…" text state (no skeleton, matching the codebase's existing plain-text loading-state convention). On success with zero rows, show a centered empty-state message ("No saved routes yet") with a link back to `/`. On success with rows, render each as a row/card showing name, formatted date, distance, and tags, each linking to `/my-routes/<id>`. On fetch failure, show inline error text matching the existing `ErrorMessage`/inline-error convention used elsewhere.

#### 3. Route detail page

**File**: `src/frontend/src/app/my-routes/[id]/page.tsx` (new)

**Intent**: Show one saved route on the map with its metadata and a GPX download button.

**Contract**: Client Component, `id` read via `useParams()`. Same auth-gate pattern as the list page (redirect + open sign-in modal if signed out). Once signed in, fetch `GET /api/routes/<id>`; on `404`, show a "Route not found" message with a link back to `/my-routes`; on success, render `RouteMap` (imported the same dynamic-`ssr:false` way `RouteApp` does) with `routeCoordinates` from the fetched `geometry.coordinates`, plus a new lightweight info panel (name, tags, distance — no surface-quality section) and a "Download GPX" button that `POST`s the fetched `geometry.coordinates` to `/api/routes/gpx`, mirroring `RouteInfoPanel.handleDownload`'s blob-download logic exactly.

#### 4. Header nav link

**File**: `src/frontend/src/components/Header.tsx`

**Intent**: Let a signed-in user navigate to their library from anywhere in the app.

**Contract**: Add a `next/link` `<Link href="/my-routes">My Routes</Link>` inside the existing `isSignedIn` branch, alongside the email display and logout button — hidden entirely when signed out, matching the pattern `save-route` established for the Save UI (hide rather than disable).

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build`
- Lint passes: `npm run lint`
- Proxy route tests pass: `npm test` — list proxy: no-Authorization → 401, valid request forwards header and relays the backend array; detail proxy: no-Authorization → 401, valid request forwards header and relays the backend body/status including a 404 pass-through

#### Manual Verification:

- Signed-out user navigating directly to `/my-routes` or `/my-routes/<any-id>` is redirected to `/` and sees the sign-in modal
- Signed-in user with zero saved routes sees the empty-state message and a working link back to the planner
- Signed-in user with saved routes (from `save-route`'s manual testing) sees them listed newest-first with correct name/date/distance/tags
- Clicking a row opens the detail page, showing the correct route on the map with matching name/tags/distance
- "Download GPX" on the detail page produces a GPX file matching the one downloadable from the original generation flow
- "My Routes" link appears in the header only when signed in
- Anonymous route generation and GPX export, and the save flow, still work unaffected

---

## Testing Strategy

### Unit Tests:

- Backend: list scoping (own routes only, sorted, summary fields), detail 404 branches (missing id, not-owned id), detail geometry round-trip, 401 on missing/invalid `sub` for both endpoints.
- Frontend: both proxy routes' header-forwarding and error/status-relay branches.

### Integration Tests:

- Backend `GET /routes` and `GET /routes/{id}` end-to-end against the Testcontainers Postgres fixture (`PostgresFixture`), seeding multiple users' routes to verify cross-user scoping — same fixture pattern as `SaveRouteTests.cs`.

### Manual Testing Steps:

1. As a signed-out user, navigate to `/my-routes` directly; confirm redirect to `/` and the sign-in modal opening.
2. Sign in as a user with no saved routes; confirm the empty-state message and its link back to the planner.
3. Generate and save 2-3 routes (varying names/tags/distances) using the existing save flow.
4. Navigate to `/my-routes`; confirm all saved routes appear, newest first, with correct name/date/distance/tags.
5. Click a route; confirm the detail page shows it correctly on the map with matching metadata.
6. Click "Download GPX" on the detail page; confirm the file opens correctly and matches the route shown.
7. Navigate to `/my-routes/<a-random-guid>`; confirm a "Route not found" message, not a crash.
8. Confirm the "My Routes" header link is absent when signed out.
9. Confirm anonymous route generation and GPX export still work signed out.

## Performance Considerations

The list endpoint's summary-only projection (excluding `geometry`) is the primary lever for the PRD's 2-second render NFR — full route geometry (potentially hundreds of coordinate pairs per row) is never loaded for the list view, only on-demand for a single opened route.

## Migration Notes

None — no schema changes; both new endpoints read the existing `Routes` table.

## References

- Prerequisite schema + save endpoint: `context/archive/2026-07-18-save-route/`
- Auth/proxy pattern this plan reuses: `context/archive/2026-07-15-magic-link-auth/`
- Existing save-endpoint tests (pattern for new library-endpoint tests): `src/backend/VeloRoute.Tests/Routing/SaveRouteTests.cs`
- Existing proxy-route tests (pattern for new proxy tests): `src/frontend/src/app/api/routes/route.test.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend library endpoints

#### Automated

- [x] 1.1 Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj` — 2ca1cdd
- [x] 1.2 All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj` — 2ca1cdd
- [x] 1.3 New library-endpoint tests pass: list scoping/sort/summary-only, detail 404s and geometry round-trip — 2ca1cdd

#### Manual

- [x] 1.4 `curl http://localhost:5098/routes` with a valid test bearer token returns the seeded user's routes newest-first — 2ca1cdd
- [x] 1.5 `curl http://localhost:5098/routes/<id>` for a route owned by a different test user returns `404` — 2ca1cdd

### Phase 2: Frontend library pages

#### Automated

- [x] 2.1 Frontend builds: `npm run build` — 3e18a6b
- [x] 2.2 Lint passes: `npm run lint` — 3e18a6b
- [x] 2.3 Proxy route tests pass: `npm test` — 3e18a6b

#### Manual

- [x] 2.4 Signed-out user navigating directly to `/my-routes` or `/my-routes/<any-id>` is redirected to `/` and sees the sign-in modal — 3e18a6b
- [x] 2.5 Signed-in user with zero saved routes sees the empty-state message and a working link back to the planner — 3e18a6b
- [x] 2.6 Signed-in user with saved routes sees them listed newest-first with correct name/date/distance/tags — 3e18a6b
- [x] 2.7 Clicking a row opens the detail page, showing the correct route on the map with matching name/tags/distance — 3e18a6b
- [x] 2.8 "Download GPX" on the detail page produces a GPX file matching the one from the original generation flow — 3e18a6b
- [x] 2.9 "My Routes" link appears in the header only when signed in — 3e18a6b
- [x] 2.10 Anonymous route generation, GPX export, and the save flow still work unaffected — 3e18a6b
