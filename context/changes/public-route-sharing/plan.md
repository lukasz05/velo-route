# Public Route Sharing (S-05) Implementation Plan

## Overview

Let an authenticated user generate a public, unauthenticated, revocable link for one of their saved routes. Anyone with the link can view the route (name, distance, tags, map, GPX download) without logging in. The link is a live read-through to the owner's saved route — not a frozen snapshot — so it dies if the owner deletes the route or explicitly revokes the share.

## Current State Analysis

- Data model is a single `Routes` table (`Id, UserId, Name, Tags, DistanceKm, Geometry jsonb, CreatedAt`), FK-cascaded to `Users` (`src/backend/VeloRoute/Data/Route.cs`, `AppDbContext.cs:19-35`). No snapshot/versioning concept exists — sharing needs a new entity.
- `DELETE /routes/{id:guid}` (`Program.cs:185-199`) and `GET /routes/{id:guid}` (`Program.cs:169-183`) establish the 404-collapsing ownership-scoping pattern this plan's owner-scoped share endpoints reuse verbatim: `db.Routes.SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub)`, null → `404 { error: "Route not found", code: "NOT_FOUND" }`.
- `POST /routes/gpx` (`Program.cs:237-250`) is already unauthenticated — it takes coordinates directly and requires no session. The public share page reuses this endpoint as-is for GPX download; no backend change needed there.
- No unauthenticated *page* exists in the frontend today — every route under `src/app/my-routes/` gates on Clerk (`useUser`/`useAuth`, redirect-if-signed-out). The new `/r/[token]` page is the first fully public page.
- `src/frontend/src/app/api/routes/[id]/route.ts` currently exports `GET` and `DELETE`, each with its own `GUID_PATTERN` pre-validation before touching the backend — this plan follows the same self-contained-proxy-file convention rather than sharing a validation constant across files.
- `.NET 10` ships `RandomNumberGenerator.GetString(ReadOnlySpan<char> choices, int length)` (BCL, since .NET 8) — a CSPRNG-backed token generator, no external package needed for the opaque share token.

### Key Discoveries:

- **Session decision (2026-07-26):** shares are FK-linked to `Routes` with cascade delete, not an independent snapshot copy. This means deleting a route also deletes its share, and the PRD's original "link must remain stable" constraint was deliberately narrowed — see `context/foundation/prd-v2.md` Constraints (amended 2026-07-26) and `context/foundation/roadmap.md` S-05 (amended 2026-07-26). This plan implements that narrowed contract; do not re-introduce a snapshot table.
- **Session decision (2026-07-26):** sharing is idempotent (`POST /routes/{id}/share` returns the existing token if one exists) and revocable (`DELETE /routes/{id}/share` hard-deletes the share row; a later re-share mints a new token, not the same URL). A unique index on `Shares.RouteId` enforces one-share-per-route at the DB level, not just in application logic.
- `RouteDetailResponse` (`Program.cs:276-282`) has no `UserId` field already — it's safe to reuse unmodified as the public share view's response shape, just extended with a nullable `ShareToken` for the owner's own `GET /routes/{id}` call.

## Desired End State

From `/my-routes/<id>`, an authenticated owner can click "Share" to get a public URL (`/r/<token>`), copy it, and later click "Stop sharing" to revoke it. Anyone — signed in or not — who opens `/r/<token>` while it's active sees the route's name, distance, tags, and an interactive map, and can download its GPX. Opening a revoked or never-issued token, or a token whose source route was deleted, shows a generic "Route not found" page. Re-sharing after a revoke issues a new token; the old URL never resolves again.

Verify via: `POST /routes/{id}/share` returns the same token on repeated calls for the same route; `DELETE /routes/{id}/share` followed by `GET /shares/{token}` returns 404; deleting the source route (`DELETE /routes/{id}`) also makes `GET /shares/{token}` 404; the UI round-trip (share from detail page → open the link in a private/incognito window → see the route and download GPX → revoke → link now 404s) works manually against a local backend + Postgres.

### Key Discoveries: (implementation)

- The 404-collapsing ownership check pattern is now used by four endpoints (`GET`/`DELETE /routes/{id}`, and this plan's `POST`/`DELETE /routes/{id}/share`) — each share endpoint's ownership check is a straight copy of the existing ones.
- Idempotent share creation has a check-then-insert race (two tabs clicking "Share" for the same route at once): the app-level "does a share already exist" check is not itself the guarantee — the DB's unique index on `Shares.RouteId` is. The insert path must catch the unique-violation and re-query rather than assume the pre-check alone prevents duplicates.

## What We're NOT Doing

- No snapshot/copy of route data into the share record — the share is a live read-through to the `Routes` row (see Key Discoveries above).
- No preserving the same URL across a revoke → re-share cycle — each new share mints a fresh token.
- No view counts, analytics, or any visibility into who opened a link.
- No "my shares" list page — a share is only discoverable/manageable from its own route's detail page (one share per route, enforced at the DB level).
- No social-share buttons (Twitter/Facebook/etc.) — link-only, matching the PRD's Non-Goals (no community/discovery features).
- No expiration/TTL on shares — a share lasts until the owner revokes it or deletes the route.
- No distinguishing "never existed" vs. "revoked" vs. "source route deleted" in the public page's not-found state — all three render the same generic message.

## Implementation Approach

Backend first (Shares data model + four endpoint changes), then frontend (proxy routes → detail-page share UI → new public page), matching the layering established by `delete-route` and `route-library`.

The public page (`/r/[token]/page.tsx`) intentionally duplicates the map-render / GPX-download / loading-state logic already in `/my-routes/[id]/page.tsx` rather than extracting a shared component: the two pages differ in a load-bearing way (one is Clerk-gated, the other has zero auth boilerplate and different not-found semantics), and the shared surface is small enough that a shared component would trade a few dozen duplicated lines for an abstraction with only two call sites.

## Critical Implementation Details

**State sequencing (share creation race):** `POST /routes/{id}/share` must handle two tabs racing to create the first share for the same route. Check-then-insert is not sufficient on its own — wrap the insert in a try/catch for the unique-constraint violation on `Shares.RouteId`, and on catch, re-query and return the row the other request just inserted instead of erroring.

## Phase 1: Backend — Shares data model + endpoints

### Overview

Add a `Shares` table (FK to `Routes`, cascade delete, unique on `RouteId` and `Token`), and four endpoint changes: `POST /routes/{id}/share`, `DELETE /routes/{id}/share`, public `GET /shares/{token}`, and an extension of `GET /routes/{id}` to report the current `ShareToken`.

### Changes Required:

#### 1. Share data model

**File**: `src/backend/VeloRoute/Data/Share.cs` (new)

**Intent**: One row per active share, scoped to exactly one route.

**Contract**: `public sealed record Share(Guid Id, Guid RouteId, string Token, DateTimeOffset CreatedAt);` — mirrors `Route.cs`'s record style.

**File**: `src/backend/VeloRoute/Data/AppDbContext.cs`

**Intent**: Register the new entity and enforce one-share-per-route plus token uniqueness at the DB level — the real guard against the creation race described above, not just an app-level check.

**Contract**: Add `public DbSet<Share> Shares => Set<Share>();`. In `OnModelCreating`, configure `Share`: primary key `Id`; unique index on `RouteId`; unique index on `Token`; `CreatedAt` defaults to `now()`; `HasOne<Route>().WithMany().HasForeignKey(s => s.RouteId).OnDelete(DeleteBehavior.Cascade)` — same cascade style as `Route`'s FK to `User`.

**File**: `src/backend/VeloRoute/Migrations/` (generated)

**Intent**: Materialize the `Shares` table.

**Contract**: Run `dotnet ef migrations add AddShares` from `src/backend/VeloRoute/` after the model changes above. Verify the generated migration creates the `Shares` table with the two unique indexes and the cascade-delete FK described above.

#### 2. Share creation and revocation endpoints

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Let the route's owner mint (or re-fetch, idempotently) a public token for their route, and separately revoke it.

**Contract**: `POST /routes/{id:guid}/share`, `.RequireAuthorization()`. Same `sub` extraction and 404-collapsing ownership check as `DELETE /routes/{id:guid}`. If an existing `Share` for `RouteId == id` is found, return `200 Ok(new { token = existing.Token })`. Otherwise generate `RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 12)`, insert a new `Share`, `SaveChangesAsync`, return `201 Created` with `{ token }`. Wrap the insert in try/catch for the unique-constraint violation on `RouteId` (see Critical Implementation Details); on catch, re-query and return the winning row's token with `200`.

`DELETE /routes/{id:guid}/share`, `.RequireAuthorization()`. Same ownership check. If no `Share` exists for `RouteId == id`, return `404 { error: "Share not found", code: "NOT_FOUND" }`. Otherwise remove it, `SaveChangesAsync`, return `204 No Content`.

#### 3. Public share lookup endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Serve the shared route's data to anyone with a valid token — no authentication, no ownership check (the token itself is the access control).

**Contract**: `GET /shares/{token}`, no `.RequireAuthorization()`. Look up `db.Shares.SingleOrDefaultAsync(s => s.Token == token, ct)`; if `null`, `404 { error: "Route not found", code: "NOT_FOUND" }` (same shape as the owner-scoped 404s — deliberately not distinguishing "never existed" from "revoked" from "source deleted", per What We're NOT Doing). Otherwise load the route by `share.RouteId` and return the same `RouteDetailResponse` shape used by `GET /routes/{id:guid}` (it already carries no `UserId`, so it's safe to reuse as-is for the public view).

#### 4. Extend the owner's detail response with share status

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Let the detail page know on load whether the route is currently shared, so it can render the copy-link UI instead of a bare "Share" button without a second round-trip.

**Contract**: Add `string? ShareToken` as the final field of the `RouteDetailResponse` record. In `GET /routes/{id:guid}`, after loading the route, query `db.Shares.SingleOrDefaultAsync(s => s.RouteId == id, ct)` and pass `share?.Token` into the response. `GET /shares/{token}` (item 3 above) populates the same field too — harmless, the visitor already has the token via the URL.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- New `ShareRouteTests.cs` passes: share creation (401, 404, 201+token, idempotent 200 on re-share), revocation (401, 404 not-owned/nonexistent route, 404 no active share, 204 + token stops resolving), public lookup (404 unknown token, 200 + route data with no auth header), cascade (deleting the source route makes the token 404), and detail-response `shareToken` reflects share state (null before, set after, null again after revoke)

#### Manual Verification:

- `curl -X POST http://localhost:5098/routes/<id>/share` with a valid test bearer token for an owned route returns `201` and a token; calling it again returns `200` with the same token
- `curl http://localhost:5098/shares/<token>` (no auth header) returns the route's data
- `curl -X DELETE http://localhost:5098/routes/<id>/share` with a valid test bearer token revokes the share; a subsequent `curl http://localhost:5098/shares/<token>` returns `404`

---

## Phase 2: Frontend — share UI + public page

### Overview

Add proxy routes for the new backend endpoints, wire a Share/Copy/Stop-sharing UI into the detail page, and build the new unauthenticated `/r/[token]` public page.

### Changes Required:

#### 1. Types

**File**: `src/frontend/src/types/route.ts`

**Intent**: Carry share status through the detail fetch; the public page reuses the same shape.

**Contract**: Add `shareToken: string | null;` to `SavedRouteDetail`.

#### 2. Share/unshare proxy route

**File**: `src/frontend/src/app/api/routes/[id]/share/route.ts` (new)

**Intent**: Forward authenticated share-create and revoke requests to the backend, relaying the Authorization header — same shape as the existing handlers in `api/routes/[id]/route.ts`, in its own file per this codebase's one-file-per-sub-resource convention.

**Contract**: Own `GUID_PATTERN` constant (matching the existing one, duplicated per the sibling-file convention already established by `route.ts`). `POST` handler: missing `Authorization` → `401`; invalid id → `400 { code: "INVALID_ID" }`; forwards `POST` + `Authorization` to `${apiUrl}/routes/${id}/share`; relays `200`/`201` body and any error status/body unchanged. `DELETE` handler: same auth/id validation; forwards `DELETE` + `Authorization`; relays `204` bodiless or any error status/body unchanged.

**File**: `src/frontend/src/app/api/routes/[id]/share/route.test.ts` (new)

**Intent**: Cover both handlers in isolation, following the existing proxy-route test pattern.

**Contract**: `POST` cases: 401 no auth, 400 malformed id, 201/200 forwards + relays token body. `DELETE` cases: 401 no auth, 400 malformed id, 204 relay, 404 passthrough (no active share).

#### 3. Public share lookup proxy route

**File**: `src/frontend/src/app/api/shares/[token]/route.ts` (new)

**Intent**: Forward the public, unauthenticated lookup to the backend — no Authorization header involved at all.

**Contract**: `GET` handler, no auth check. Light token-shape pre-validation (`/^[A-Za-z0-9]{12}$/`, mirroring the `GUID_PATTERN` pre-validation pattern) → `400 { code: "INVALID_TOKEN" }` on mismatch. Forwards to `${apiUrl}/shares/${token}` with no headers; relays `200` body or `404` passthrough, same parse-then-relay logic as the existing `GET` handlers.

**File**: `src/frontend/src/app/api/shares/[token]/route.test.ts` (new)

**Intent**: Cover the handler, following the existing proxy-route test pattern.

**Contract**: Cases: 400 malformed token (fetch not called), 200 relay, 404 relay. No 401 case — this endpoint takes no Authorization header by design.

#### 4. Detail page: share, copy, revoke

**File**: `src/frontend/src/app/my-routes/[id]/page.tsx`

**Intent**: Let the owner turn sharing on/off for the currently open route and get a copyable link while it's active.

**Contract**: Read `route.shareToken` once the detail fetch resolves. If `null`, render a "Share" button (styled like "Download GPX" — not destructive, so not the red styling used for "Delete route"); on click, `POST /api/routes/{id}/share` with the caller's bearer token, and on success set local share state from the returned token. If a token is present (from the initial fetch or after creating a share), render a read-only text input containing `${window.location.origin}/r/${token}`, a "Copy" button (Clipboard API, brief "Copied!" confirmation text), and a "Stop sharing" button (plain button, no confirmation modal per the session decision — this is a low-stakes, instantly-reversible-by-resharing action, unlike route deletion); on click, `DELETE /api/routes/{id}/share`, and on success clear the local share state back to "not shared". Any failure on either action shows inline error text (same convention as `downloadError`) and leaves the prior UI state in place, retryable.

#### 5. Public share page

**File**: `src/frontend/src/app/r/[token]/page.tsx` (new)

**Intent**: Show the shared route to anyone with the link — no sign-in required, no owner-only affordances (no delete, no share/revoke).

**Contract**: Client Component reading `token` via `useParams<{ token: string }>()`. On mount, fetch `GET /api/shares/${token}` with no Authorization header. `404` → render a generic "Route not found" state (same shape as the existing not-found state in `my-routes/[id]/page.tsx`, but its "back" link points to `/` instead of `/my-routes` since the visitor may not be signed in). Other non-2xx → inline error text. On success, render: route name, distance, tags, the `RouteMap` component (same dynamic-import, `ssr: false` usage as the detail page), a "Download GPX" button reusing the existing unauthenticated `POST /api/routes/gpx` flow, and a "Plan your own route" link back to `/`.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build`
- Lint passes: `npm run lint`
- Tests pass: `npm test` — share/unshare proxy tests, public share proxy tests

#### Manual Verification:

- From `/my-routes/<id>`, clicking "Share" reveals a copyable URL; clicking "Copy" copies it and shows a brief confirmation
- Opening the copied URL in a private/incognito browser window (no session) shows the route's name, distance, tags, and map, with a working "Download GPX" button
- Refreshing `/my-routes/<id>` (without revoking) still shows the copy-link UI, not a bare "Share" button — confirms `shareToken` persists across reloads
- Clicking "Stop sharing" reverts the detail page to a bare "Share" button; the previously copied URL now shows "Route not found" when opened
- Clicking "Share" again after revoking produces a **different** URL than before
- Deleting the route (via the existing "Delete route" flow) while it has an active share also makes the old share URL 404
- Anonymous route generation, GPX export, save, delete, and the rest of the My Routes flows are unaffected

---

## Testing Strategy

### Unit Tests:

- Backend: share creation/idempotency, revocation, public lookup, and cascade-on-route-delete (all via `ShareRouteTests.cs`, `PostgresFixture`-backed, following `DeleteRouteTests.cs`'s structure).
- Frontend: proxy routes' auth/validation/relay branches for both the owner-scoped share/unshare endpoints and the public lookup endpoint.

### Integration Tests:

- Backend `POST`/`DELETE /routes/{id}/share` and `GET /shares/{token}` end-to-end against the Testcontainers Postgres fixture, seeding a route and verifying the full share → view → revoke → 404 lifecycle, plus the delete-cascades-share case — same fixture pattern as `RouteLibraryTests.cs` and `DeleteRouteTests.cs`.

### Manual Testing Steps:

1. Sign in, open a saved route's detail page, click "Share"; verify a copyable URL appears.
2. Open that URL in a private/incognito window; verify the route displays with map, name, distance, tags, and GPX download works.
3. Back in the signed-in tab, click "Stop sharing"; verify the detail page reverts to a bare "Share" button.
4. Reload the incognito tab on the same URL; verify it now shows "Route not found."
5. Click "Share" again on the detail page; verify the new URL differs from the first one.
6. Delete the route entirely; verify the (still-active, from step 5) share URL now also 404s.
7. Confirm anonymous route generation, GPX export, save, and delete-route flows are unaffected.

## Performance Considerations

None beyond what's already established — single-row lookups by unique index (`Token`, `RouteId`), no new query patterns at this data volume.

## Migration Notes

New `Shares` table via EF Core migration (`dotnet ef migrations add AddShares`); no changes to the existing `Routes` or `Users` tables beyond the new inbound FK.

## References

- Prerequisite endpoints + ownership pattern: `context/archive/2026-07-18-delete-route/`, `context/archive/2026-07-18-route-library/`
- Existing proxy-route + backend test patterns: `src/frontend/src/app/api/routes/[id]/route.test.ts`, `src/backend/VeloRoute.Tests/Routing/DeleteRouteTests.cs`
- PRD requirement (amended 2026-07-26 during this planning session): `context/foundation/prd-v2.md` — Scope of Change → Route library, and Constraints & Compatibility
- Roadmap entry (amended 2026-07-26 during this planning session): `context/foundation/roadmap.md` S-05

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend — Shares data model + endpoints

#### Automated

- [x] 1.1 Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj` — d1331e2
- [x] 1.2 All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj` — d1331e2
- [x] 1.3 New `ShareRouteTests.cs` passes: creation (401/404/201+token/idempotent 200), revocation (401/404/404 no-share/204), public lookup (404/200 no-auth), cascade-on-delete, detail response `shareToken` reflects state — d1331e2

#### Manual

- [x] 1.4 `curl -X POST .../routes/<id>/share` returns 201+token; repeat call returns 200 with the same token — d1331e2
- [x] 1.5 `curl .../shares/<token>` (no auth header) returns the route's data — d1331e2
- [x] 1.6 `curl -X DELETE .../routes/<id>/share` revokes; subsequent `curl .../shares/<token>` returns 404 — d1331e2

### Phase 2: Frontend — share UI + public page

#### Automated

- [ ] 2.1 Frontend builds: `npm run build`
- [ ] 2.2 Lint passes: `npm run lint`
- [ ] 2.3 Tests pass: `npm test` — share/unshare proxy tests, public share proxy tests

#### Manual

- [ ] 2.4 "Share" on detail page reveals a copyable URL; "Copy" copies it with a brief confirmation
- [ ] 2.5 Opening the URL in a private window (no session) shows name/distance/tags/map + working GPX download
- [ ] 2.6 Refreshing the detail page still shows the copy-link UI (not a bare "Share" button)
- [ ] 2.7 "Stop sharing" reverts to a bare "Share" button; the old URL now 404s
- [ ] 2.8 Re-sharing after revoke produces a different URL than before
- [ ] 2.9 Deleting the route also invalidates its active share URL
- [ ] 2.10 Anonymous generation, GPX export, save, delete-route, and other My Routes flows unaffected
