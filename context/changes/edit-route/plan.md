# Edit Saved Route Implementation Plan

## Overview

Add the missing update operation for saved routes: an owner-scoped `PATCH /routes/{id}` with true-partial semantics, an edit form on the route detail page, and shared name/tag limits enforced on both the save and edit paths.

This closes roadmap slice **S-08**. S-02 (`save-route`) shipped name and tag entry *before* a route is persisted but never added a way to change either afterwards, leaving the PRD's "optional user-editable name and optional tags" requirement half-delivered.

## Current State Analysis

The library API in `src/backend/VeloRoute/Program.cs` covers create (`:167`), read (`:194`, `:209`), and delete (`:226`), but has no update verb. Every owner-scoped endpoint uses one shape:

```
var sub = user.GetSub();                 // null → 401
SingleOrDefaultAsync(r => r.Id == id && r.UserId == sub)   // null → 404 NOT_FOUND
```

The 404-on-foreign-id behaviour (rather than 403) is deliberate and load-bearing: it prevents the endpoints from disclosing which route ids exist. `DeleteRouteTests.Delete_OwnedByDifferentUser_Returns404AndLeavesRowUntouched` locks that in.

Key constraints discovered:

- `src/backend/VeloRoute/Data/Route.cs` is a **positional record** with init-only properties. It cannot be mutated after materialisation by ordinary assignment — this shapes the persistence approach (see Critical Implementation Details).
- `Program.cs:382` `SaveRouteRequest` validates only `string.IsNullOrWhiteSpace(Name)` → 400 `INVALID_INPUT`. There are no length limits anywhere today.
- `src/frontend/src/components/RouteInfoPanel.tsx:53` parses tags from a comma-separated string (`split(',').map(trim).filter(Boolean)`). The edit form needs the same parsing, so it should be extracted rather than duplicated.
- `src/frontend/src/app/api/routes/[id]/route.ts` validates the id against `GUID_PATTERN` before proxying, in both `GET` and `DELETE`. A `PATCH` handler must do the same.
- Shares read through to the live route (`Program.cs:306` `GET /shares/{token}` joins `Shares` → `Routes`), so an edited name propagates to active public links with no extra work. No share-lifetime interaction to handle.
- `src/frontend/src/app/my-routes/[id]/page.tsx` already holds full route state (`SavedRouteDetail`) plus per-action `isX`/`xError` state pairs for download, delete, and share. The edit form follows that established shape.

## Desired End State

A signed-in user viewing one of their saved routes can change its name and tags from the detail page and see the change persist across a reload, in the library list, and on any active public share link. A `PATCH` for a route belonging to someone else is indistinguishable from one for a route that does not exist. Names and tags that would break the library list are rejected on both save and edit with the same error.

Verify by: renaming a route on `/my-routes/[id]`, reloading, confirming the new name on `/my-routes` and at `/r/<token>` if shared; and by running the full backend and frontend suites.

### Key Discoveries:

- Ownership + 404 pattern to mirror: `src/backend/VeloRoute/Program.cs:226-240`
- Test pattern to mirror, including the cross-user case: `src/backend/VeloRoute.Tests/Routing/DeleteRouteTests.cs:50`
- Proxy handler pattern with GUID guard: `src/frontend/src/app/api/routes/[id]/route.ts:23`
- Tag parsing to extract and reuse: `src/frontend/src/components/RouteInfoPanel.tsx:53`
- `Route` is immutable (positional record): `src/backend/VeloRoute/Data/Route.cs`

## What We're NOT Doing

- No editing of geometry, distance, or creation date — only name and tags are user-editable.
- No edit affordance in the `/my-routes` list; editing happens on the detail page only.
- No component tests for the detail-page form itself (Clerk + map mocking cost outweighs the risk for a simple form). Its behaviour is covered by manual verification.
- No database-level length constraints or migration — limits are validated at the API boundary.
- No optimistic-concurrency handling (last write wins). Single-user-per-route ownership makes concurrent edits a non-scenario.
- No changes to the share token lifecycle.

## Implementation Approach

Three phases, each independently verifiable. Phase 1 lands the widest-blast-radius piece (validation applied to the *existing* save path) alone, so any regression surfaces before new endpoint code exists. Phase 2 adds the endpoint and its JSON plumbing. Phase 3 wires the UI.

The request contract uses **true-partial** semantics: an absent field leaves the stored value untouched; an explicit `null` for `tags` clears them. Because `System.Text.Json` cannot distinguish "absent" from "explicitly null" on a plain nullable property, an `Optional<T>` wrapper with a converter carries that distinction.

## Critical Implementation Details

**Persisting to an immutable record.** `Data/Route.cs` is a positional record with init-only properties, so a materialised entity cannot be mutated and re-saved through the change tracker. Use `ExecuteUpdateAsync` on the filtered query, chaining a `SetProperty` call only for each field the request actually provided. Fetch first for the ownership check and 404, then issue the update — `ExecuteUpdate` bypasses the change tracker, which is fine here since nothing else in the request touches the entity.

**Absent vs. null in the converter.** A JSON property that is absent never reaches the converter, so the struct stays at `default` (`HasValue == false`) — that is the "leave alone" case, and it works without any converter involvement. The converter's `Read` only ever runs for a *present* property, including an explicit `null` token, which must produce `HasValue == true` with a null inner value. Getting this backwards silently turns every "clear tags" request into a no-op.

## Phase 1: Shared validation + caps

### Overview

Extract name and tag validation into one place, add length limits, and apply it to the existing `POST /routes`. No new endpoint yet.

### Changes Required:

#### 1. Validation rules

**File**: `src/backend/VeloRoute/RouteMetadataValidation.cs` (new)

**Intent**: Single source of truth for what a saved route's name and tags may contain, so `POST` and the forthcoming `PATCH` cannot drift apart.

**Contract**: `static class RouteMetadataValidation` exposing `MaxNameLength = 120`, `MaxTagCount = 10`, `MaxTagLength = 30`, and a `Validate(string? name, string[]? tags)` returning an error string or `null` when valid. Rules: name required, non-whitespace, `<= MaxNameLength` after trimming; tags at most `MaxTagCount` entries, each non-empty and `<= MaxTagLength` after trimming. Callers map a non-null result to `400 INVALID_INPUT`, matching the existing error body shape `{ error, code }`.

#### 2. Apply to the save path

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Replace the inline `IsNullOrWhiteSpace` check in `POST /routes` with the shared validator, keeping the coordinate check as-is.

**Contract**: The existing 400 response shape and the `"Name is required"` message for the blank case are preserved so current tests and the frontend error path keep working; new messages appear only for the newly-rejected length cases.

#### 3. Cover the new rules

**File**: `src/backend/VeloRoute.Tests/Routing/SaveRouteTests.cs`

**Intent**: Assert that over-long names, too many tags, and over-long tags are rejected on save, and that values at the limit are accepted.

**Contract**: New `[Fact]`s alongside the existing save tests, using the established `VeloRouteWebApplicationFactory` + `TestJwtFactory` setup.

### Success Criteria:

#### Automated Verification:

- Backend builds cleanly: `dotnet build` from `src/backend`
- New validation tests pass: `dotnet test --filter FullyQualifiedName~SaveRouteTests`
- Full backend suite passes: `dotnet test` from `src/backend`

#### Manual Verification:

- Saving a route from the UI with a normal name and tags still works unchanged

---

## Phase 2: `Optional<T>` + PATCH endpoint

### Overview

Add the wrapper that distinguishes absent from null, the request record, and the endpoint itself with full test coverage.

### Changes Required:

#### 1. Optional wrapper and converter

**File**: `src/backend/VeloRoute/Optional.cs` (new)

**Intent**: Carry the absent / null / value distinction from JSON into the endpoint handler.

**Contract**: `readonly struct Optional<T>` with `bool HasValue` and `T? Value`, plus a `JsonConverterFactory` handling any `Optional<>`. `Read` returns `HasValue = true` for every present token including `null`; `default(Optional<T>)` (`HasValue = false`) represents absent. `Write` emits the inner value. Register the factory in `Program.cs` via `builder.Services.ConfigureHttpJsonOptions`.

#### 2. Request record

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Define the PATCH body next to the other request records at the bottom of the file.

**Contract**: `record UpdateRouteRequest(Optional<string> Name, Optional<string[]?> Tags)`.

#### 3. The endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Owner-scoped partial update of name and tags, mirroring the delete endpoint's auth and 404 behaviour.

**Contract**: `PATCH /routes/{id:guid}`, `.RequireAuthorization()`. 401 when `GetSub()` is null; 404 `NOT_FOUND` when the route is missing *or* owned by someone else; 400 `INVALID_INPUT` when a provided field fails `RouteMetadataValidation` (validate the merged result — a provided name against the rules, a provided tag array against the rules). `Name` present-and-null or blank is a 400, not a clear — a route must always have a name. `Tags` present with `null` or an empty array clears them. Neither field provided is a no-op success. Returns `204 NoContent`. Persist with `ExecuteUpdateAsync`, chaining `SetProperty` only for provided fields.

#### 4. Endpoint tests

**File**: `src/backend/VeloRoute.Tests/Routing/EditRouteTests.cs` (new)

**Intent**: Lock down auth, ownership, partial semantics, and validation.

**Contract**: `[Collection(PostgresCollection.Name)]` following `DeleteRouteTests`. Cases: no token → 401; unknown id → 404; route owned by another user → 404 *and the row is unchanged*; name only → name updated, tags untouched; tags only → tags updated, name untouched; `tags: null` → tags cleared; empty body → 204 with no change; blank name → 400; over-long name → 400; too many tags → 400.

### Success Criteria:

#### Automated Verification:

- Backend builds cleanly: `dotnet build` from `src/backend`
- New endpoint tests pass: `dotnet test --filter FullyQualifiedName~EditRouteTests`
- Full backend suite passes: `dotnet test` from `src/backend`

#### Manual Verification:

- `curl -X PATCH` with only `{"name":"..."}` leaves tags intact; with `{"tags":null}` clears them
- A PATCH against another user's route id returns 404, not 403

---

## Phase 3: Frontend edit surface

### Overview

Proxy the new verb and add the edit form to the route detail page.

### Changes Required:

#### 1. Shared tag parsing

**File**: `src/frontend/src/lib/tags.ts` (new)

**Intent**: One implementation of the comma-separated tag convention, used by both the save panel and the edit form.

**Contract**: `parseTags(input: string): string[]` (split, trim, drop empties) and `formatTags(tags: string[] | null): string` for pre-filling the edit input. `RouteInfoPanel.tsx` switches to `parseTags`, replacing its inline expression.

#### 2. Proxy handler

**File**: `src/frontend/src/app/api/routes/[id]/route.ts`

**Intent**: Relay PATCH to the backend with the bearer token, mirroring the existing handlers.

**Contract**: `export async function PATCH(...)` with the same `requireAuthHeader` + `GUID_PATTERN` guards as `DELETE`, forwarding the JSON body and `Content-Type`, and returning a bare `204` response without attempting to parse a body.

#### 3. Types

**File**: `src/frontend/src/types/route.ts`

**Intent**: Type the PATCH payload.

**Contract**: `interface UpdateRoutePayload { name?: string; tags?: string[] | null }` — optional keys mirror the backend's absent-means-leave-alone semantics.

#### 4. Edit form

**File**: `src/frontend/src/app/my-routes/[id]/page.tsx`

**Intent**: Let the user rename and retag from the detail sidebar, following the page's existing per-action state pattern.

**Contract**: An "Edit" button toggling an inline form (name input, comma-separated tags input, Save/Cancel), with `isSaving` / `editError` state alongside the existing pairs. On success, update local `route` state so the heading and tag line re-render without a refetch. Cancel restores the fields from `route`. A 400 surfaces the backend's `error` message; other failures use the page's generic phrasing.

#### 5. Proxy tests

**File**: `src/frontend/src/app/api/routes/[id]/route.test.ts`

**Intent**: Cover the new handler at the same depth as its GET/DELETE siblings.

**Contract**: Cases: missing auth header → 401 without calling the backend; malformed id → 400 `INVALID_ID`; valid request → backend called once with `PATCH`, the bearer header, and the JSON body, returning 204; backend error status is relayed.

### Success Criteria:

#### Automated Verification:

- Frontend type-checks: `npx tsc --noEmit` from `src/frontend`
- Frontend lints cleanly: `npm run lint` from `src/frontend`
- Frontend tests pass: `npm test` from `src/frontend`

#### Manual Verification:

- Rename a route on the detail page; the new name shows immediately, survives a reload, and appears in `/my-routes`
- Clear all tags, then add new ones; both persist
- With the route shared, open `/r/<token>` and confirm the edited name appears there
- Submitting a blank name shows the validation error and does not persist

---

## Testing Strategy

### Unit Tests:

- `RouteMetadataValidation` boundaries: exactly-at-limit values accepted, one-over rejected, whitespace-only name rejected
- `Optional<T>` converter: absent → `HasValue == false`; explicit null → `HasValue == true, Value == null`; value → both set

### Integration Tests:

- Full `PATCH` matrix in `EditRouteTests` (auth, ownership, partial semantics, validation) against the Testcontainers Postgres fixture
- Frontend proxy handler tests with a mocked `proxyFetch`

### Manual Testing Steps:

1. Sign in, open a saved route, rename it, reload — new name persists
2. Add two tags, save, return to `/my-routes` — tags render on the card
3. Clear tags via the edit form — tag line disappears
4. Share the route, open the public link in a private window, edit the name, refresh the public link — name updates (live read-through)
5. Try a blank name and a 200-character name — both rejected with a visible message
6. Delete the route, then attempt an edit from a stale tab — 404 handled without a crash

## Performance Considerations

`ExecuteUpdateAsync` issues a single `UPDATE` with no entity materialisation beyond the ownership fetch. Route counts per user are small (flat list, no pagination, per PRD Non-Goals). No indexing changes needed.

## Migration Notes

No schema change and no migration. The new length limits are validated at the API boundary only, so any pre-existing row longer than the caps keeps loading and rendering normally. Such a row could not be re-saved with its current name via `PATCH` without shortening it — acceptable, since no route in the current data set approaches 120 characters (names are auto-generated as `"YYYY-MM-DD • NN km"`).

## References

- Roadmap slice: `context/foundation/roadmap.md` § S-08
- Ownership + 404 pattern: `src/backend/VeloRoute/Program.cs:226`
- Test pattern: `src/backend/VeloRoute.Tests/Routing/DeleteRouteTests.cs:50`
- Prior art for the save path: `context/archive/2026-07-18-save-route/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared validation + caps

#### Automated

- [x] 1.1 Backend builds cleanly — 88ae903
- [x] 1.2 New validation tests pass — 88ae903
- [x] 1.3 Full backend suite passes — 88ae903

#### Manual

- [x] 1.4 Saving a route from the UI still works unchanged — 88ae903

### Phase 2: Optional<T> + PATCH endpoint

#### Automated

- [x] 2.1 Backend builds cleanly — 320078c
- [x] 2.2 New endpoint tests pass — 320078c
- [x] 2.3 Full backend suite passes — 320078c

#### Manual

- [x] 2.4 curl PATCH partial semantics verified (name-only, tags-null) — 320078c
- [x] 2.5 PATCH against another user's route returns 404, not 403 — 320078c

### Phase 3: Frontend edit surface

#### Automated

- [x] 3.1 Frontend type-checks
- [x] 3.2 Frontend lints cleanly
- [x] 3.3 Frontend tests pass

#### Manual

- [x] 3.4 Rename persists across reload and shows in the library list
- [x] 3.5 Clearing and re-adding tags persists
- [x] 3.6 Edited name appears on an active public share link
- [x] 3.7 Blank name shows the validation error and does not persist
