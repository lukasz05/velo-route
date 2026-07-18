# Save Route (S-02) Implementation Plan

## Overview

Add the ability for an authenticated user to save a generated loop route to their personal library: a new authenticated `POST /routes` backend endpoint that persists a `Route` row, and a frontend "Save" control in `RouteInfoPanel` with editable name/tags, wired through a new Next.js proxy route.

## Current State Analysis

- The `Routes` table already exists and is schema-tested: `Route` record (`src/backend/VeloRoute/Data/Route.cs`) with `Id`, `UserId` (FK → `Users.Id`, cascade delete), `Name`, `Tags` (`text[]`), `DistanceKm`, `Geometry` (`GeoJsonLineString`, stored as `jsonb`), `CreatedAt`. No new migration is needed.
- `AppDbContext` (`src/backend/VeloRoute/Data/AppDbContext.cs`) already maps all of the above.
- The authenticated-proxy pattern is established end-to-end by `magic-link-auth`: backend `ClaimsPrincipal` → `sub` claim extraction + `.RequireAuthorization()` (`Program.cs:116-125`), frontend `useAuth().getToken()` → `Authorization: Bearer <token>` header → Next.js proxy route that forwards the header and relays backend error bodies (`src/frontend/src/app/api/auth/sync/route.ts`).
- `RouteInfoPanel.tsx` already has the button-with-loading-state and inline-error patterns this plan reuses (`isDownloading`/`downloadError` for the existing "Download GPX" button).
- The frontend `RouteResult.geometry.coordinates` is `{ longitude, latitude }[]` — the same shape the backend's `RouteCoordinate` record (`src/backend/VeloRoute/Routing/RouteResult.cs:14`) already uses for `/routes/gpx`. The `Route` entity instead stores geometry as `GeoJsonLineString.Coordinates` (`double[][]`, `[lon, lat]` pairs) — the new endpoint converts between the two; the frontend does not need to change its coordinate shape.
- `AppDbContext.Users` currently gets rows written only by `/auth/sync`, which the frontend `Header` calls on every sign-in. By the time a user can click Save, their `User` row should already exist — but the new endpoint depends on that FK, which matters for both implementation and tests (see Critical Implementation Details).

## Desired End State

A signed-in user who has generated a route can edit its name (pre-filled with an auto-generated `"YYYY-MM-DD • Nkm"` label) and optionally add comma-separated tags, click "Save", and see the button flip to a disabled `"Saved ✓"` state. The route now exists as a row in `Routes` with the correct `UserId`, `Name`, `Tags`, `DistanceKm`, and `Geometry`. An anonymous user clicking Save sees Clerk's sign-in modal instead (no auto-resume — they click Save again after signing in). Save failures (network, 401, 500) show inline error text below the button, matching the existing GPX-download error pattern. Anonymous route generation and GPX export are unaffected.

Verify via: `POST /routes` with a valid Clerk-issued (or test) token creates exactly one `Routes` row with the submitted fields; the UI round-trip (generate → edit name → Save → "Saved ✓") works manually against a local backend + Postgres.

### Key Discoveries:

- Schema and cascade-delete already shipped and tested — this plan is additive only (no migration).
- `RouteCoordinate` (backend) and `RouteResult.geometry.coordinates` (frontend) already share the same `{longitude, latitude}` shape used by `/routes/gpx` — the save endpoint accepts the identical list shape and does the `GeoJsonLineString` conversion server-side, so the frontend reuses `route.geometry.coordinates` unchanged.
- No existing frontend proxy route both forwards an `Authorization` header *and* forwards a JSON body — `/auth/sync`'s proxy does the former with no body, `/api/routes/loop` does the latter with no auth. The new `/api/routes` proxy needs both; it follows `/auth/sync`'s inline-fetch shape rather than the `lib/routingApi.ts` helper pattern (that helper is for the two unauthenticated backend calls only).

## What We're NOT Doing

- No "My Routes" library / list view (S-03, blocked on this slice).
- No post-save editing of name/tags (would require the library page from S-03).
- No route deletion (S-04) or public sharing (S-05).
- No tag autocomplete, validation, or dedup — free-text comma-separated input only.
- No duplicate-save prevention beyond the button's own disabled-after-save state; re-generating and re-saving the same start point creates a new, separate row (no uniqueness constraint — matches the schema as shipped).
- No toast/snackbar system — reusing the existing inline button-state + inline-error-text pattern.

## Implementation Approach

Backend first (store → API), then frontend (client → proxy → API), matching the codebase's established layering. The backend phase lands a self-contained, testable endpoint; the frontend phase wires the existing UI component to it using patterns already proven by `magic-link-auth`.

## Critical Implementation Details

**FK dependency on `Users` in tests.** The `Routes.UserId` FK requires a matching `Users` row to exist first (cascade-delete relationship, same as `UserRouteSchemaTests.cs` demonstrates). Because `POST /routes` itself does not create the `User` row (only `/auth/sync` does), backend tests for the new endpoint must seed a `User` row via `AppDbContext` before exercising the save call — otherwise the insert fails on the FK constraint. This mirrors `UserRouteSchemaTests.cs`'s setup, not `AuthSyncTests.cs`'s (which tests a table with no FK dependency).

## Phase 1: Backend save endpoint

### Overview

Add an authenticated `POST /routes` endpoint that validates input, converts coordinates to `GeoJsonLineString`, inserts a `Route` row scoped to the caller's `sub`, and returns the new row's id.

### Changes Required:

#### 1. Save endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Accept a route's name, optional tags, distance, and coordinate list from an authenticated caller; persist it as a `Route` row owned by that caller.

**Contract**: `POST /routes`, `.RequireAuthorization()`, mapped alongside the existing `/auth/sync` and `/routes/*` endpoints (after `app.UseAuthentication()`/`app.UseAuthorization()`). Request body: `{ name: string, tags: string[]?, distanceKm: double, coordinates: [{ longitude: double, latitude: double }] }`. Validation: `name` non-empty/non-whitespace, `coordinates` has at least 2 points — both violations return `400` with `{ error, code: "INVALID_INPUT" }` (matching the existing validation-error shape used by `/routes/loop` and `/routes/gpx`). Missing/invalid `sub` claim returns `401` (same extraction as `/auth/sync`: `ClaimTypes.NameIdentifier` falling back to `"sub"`). On success, build the `Route` record (`Id = Guid.NewGuid()`, `UserId = sub`, coordinates mapped to `GeoJsonLineString("LineString", coordinates.Select(c => new[] { c.Longitude, c.Latitude }).ToArray())`), add via `db.Routes.Add(...)`, `SaveChangesAsync`, return `201 Created` with `{ id: route.Id }`.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- New save-endpoint tests pass: no-token → 401, missing/empty name → 400, empty coordinates → 400, valid request → 201 and persists exactly one `Routes` row with the submitted `UserId`/`Name`/`Tags`/`DistanceKm`/`Geometry`

#### Manual Verification:

- `curl -X POST http://localhost:5098/routes` with a valid test bearer token and a small JSON body returns `201` and a `Routes` row appears in Postgres with the expected values

---

## Phase 2: Frontend save UI

### Overview

Add a name/tags editing surface and a Save button to `RouteInfoPanel`, wired through a new authenticated Next.js proxy route.

### Changes Required:

#### 1. Save proxy route

**File**: `src/frontend/src/app/api/routes/route.ts` (new)

**Intent**: Forward an authenticated save request to the backend, relaying the Authorization header and the JSON body, and relaying backend error bodies on failure — same shape as `src/frontend/src/app/api/auth/sync/route.ts`, extended to also forward a JSON body (that route sends none).

**Contract**: `POST` handler. Missing `Authorization` header → `401 { error, code: "UNAUTHORIZED" }`. Parses the request body, forwards to `${VELO_API_URL ?? 'http://localhost:5098'}/routes` with the same `Authorization` header and `Content-Type: application/json`. On backend failure, relay `{ error, code }` and status from the backend response (same parse-then-relay logic as `api/auth/sync/route.ts`). On backend success, relay the `201` and its `{ id }` body.

#### 2. Save UI in RouteInfoPanel

**File**: `src/frontend/src/components/RouteInfoPanel.tsx`

**Intent**: Let the user review/edit an auto-generated name and optional tags, then save the currently displayed route. Reuses the file's existing `useState`-per-button pattern (`isDownloading`/`downloadError`) for a parallel `isSaving`/`saveError`/`isSaved` set of states.

**Contract**: On mount (or when `route` changes), derive the default name as `` `${YYYY-MM-DD} • ${Math.round(route.distanceMeters / 1000)} km` `` (matches the format already used in `UserRouteSchemaTests.cs`'s fixture and the PRD example). Render a text input bound to that name (user-editable) and a text input for comma-separated tags, both above the existing "Download GPX" button. Add a "Save" button: if `useUser().isSignedIn` is false, call `useClerk().openSignIn()` and stop (no request sent, no auto-resume). If signed in, call `useAuth().getToken()`, `POST` to `/api/routes` with `{ name, tags: tags.split(',').map(t => t.trim()).filter(Boolean) || undefined, distanceKm: route.distanceMeters / 1000, coordinates: route.geometry.coordinates }`. On success, set `isSaved = true` and disable the button, label `"Saved ✓"`. On failure, set `saveError` and render it as inline red text below the button, matching `downloadError`'s existing rendering.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build`
- Lint passes: `npm run lint`
- Proxy route tests pass: `npm test` — no-Authorization-header → 401, valid request forwards body+header and relays a 201, backend error response is relayed with its `{ error, code }` and status

#### Manual Verification:

- Signed-out user generates a route, clicks Save → Clerk sign-in modal opens, no request is sent
- Signed-in user generates a route, name field is pre-filled as `"YYYY-MM-DD • Nkm"`, editing it and adding tags, clicking Save succeeds → button becomes disabled `"Saved ✓"`, and the row appears in Postgres with the edited name/tags
- Simulated save failure (e.g., stop the backend) shows inline error text below the Save button
- Anonymous route generation and GPX export still work signed out (no regression)

---

## Testing Strategy

### Unit Tests:

- Backend: input validation branches (empty name, too-few coordinates), successful persistence with correct field mapping, 401 on missing/invalid `sub`.
- Frontend: proxy route's header-forwarding, body-forwarding, and error-relay branches.

### Integration Tests:

- Backend `POST /routes` end-to-end against the Testcontainers Postgres fixture (`PostgresFixture`, same as `AuthSyncTests.cs`/`UserRouteSchemaTests.cs`), including the FK-seeding step noted in Critical Implementation Details.

### Manual Testing Steps:

1. Generate a route while signed out; click Save; confirm the Clerk sign-in modal opens and no network request to `/api/routes` fires.
2. Sign in, generate a route, confirm the name field pre-fills as `"YYYY-MM-DD • Nkm"`.
3. Edit the name, add tags (`"scenic, hilly"`), click Save; confirm the button becomes disabled `"Saved ✓"`.
4. Query Postgres directly (or via existing `check the db` workflow) to confirm the new `Routes` row has the edited name, `["scenic","hilly"]` tags, correct `DistanceKm`, and valid `Geometry`.
5. Stop the backend, click Save on a freshly generated route, confirm inline error text appears.
6. Confirm anonymous route generation and GPX export still work without signing in.

## Performance Considerations

None beyond what's already covered by the existing schema (route geometry stored as `jsonb`, no new indexes needed for a single-row insert path).

## Migration Notes

None — schema already exists from `data-layer-schema` (F-02).

## References

- Prerequisite schema: `context/archive/2026-07-10-data-layer-schema/`
- Auth/proxy pattern this plan reuses: `context/archive/2026-07-15-magic-link-auth/`
- Existing schema tests: `src/backend/VeloRoute.Tests/Data/UserRouteSchemaTests.cs`
- Existing authenticated-endpoint tests: `src/backend/VeloRoute.Tests/Routing/AuthSyncTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend save endpoint

#### Automated

- [ ] 1.1 Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- [ ] 1.2 All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- [ ] 1.3 New save-endpoint tests pass: no-token → 401, missing/empty name → 400, empty coordinates → 400, valid request → 201 and persists exactly one `Routes` row with the submitted fields

#### Manual

- [ ] 1.4 `curl -X POST http://localhost:5098/routes` with a valid test bearer token and a small JSON body returns `201` and a `Routes` row appears in Postgres with the expected values

### Phase 2: Frontend save UI

#### Automated

- [ ] 2.1 Frontend builds: `npm run build`
- [ ] 2.2 Lint passes: `npm run lint`
- [ ] 2.3 Proxy route tests pass: `npm test`

#### Manual

- [ ] 2.4 Signed-out user generates a route, clicks Save → Clerk sign-in modal opens, no request is sent
- [ ] 2.5 Signed-in user: name field pre-fills as `"YYYY-MM-DD • Nkm"`, editing name/tags and clicking Save succeeds → button becomes disabled `"Saved ✓"`, row appears in Postgres with edited values
- [ ] 2.6 Simulated save failure shows inline error text below the Save button
- [ ] 2.7 Anonymous route generation and GPX export still work signed out
