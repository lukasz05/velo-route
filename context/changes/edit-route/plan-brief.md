# Edit Saved Route — Plan Brief

> Full plan: `context/changes/edit-route/plan.md`

## What & Why

Saved routes can be created, listed, opened, and deleted — but never changed. S-02 (`save-route`) shipped name and tag entry only *before* a route is persisted, leaving the PRD's "optional user-editable name and optional tags" requirement half-delivered. This adds the missing update path so a user can fix a name or retag a route after saving it.

## Starting Point

`Program.cs` has create/read/delete for routes, all sharing one owner-scoped shape: `GetSub()` → filter by `Id && UserId` → 404 on miss. There is no update verb anywhere in the backend or the frontend proxy. `Data/Route.cs` is an immutable positional record, and validation today is a single blank-name check with no length limits.

## Desired End State

A signed-in user viewing one of their routes can rename it and change its tags from the detail page. The change persists across reloads, shows in the library list, and propagates to any active public share link (shares read the route live). Editing someone else's route is indistinguishable from editing one that does not exist.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Edit surface | Route detail page only | More room for the form, and the page already holds full route state including share status. |
| Request semantics | True partial (absent = leave alone) | Correct PATCH behaviour; a future client can rename without knowing the tags. |
| Absent vs. null | `Optional<T>` struct + `JsonConverterFactory` | `System.Text.Json` cannot otherwise distinguish absent from explicit null, which is what makes "clear tags" expressible. |
| Validation scope | Length caps on both POST and PATCH | Consistent rules; PATCH stricter than POST would make some saved routes unrenameable to their own current name. |
| Persistence | `ExecuteUpdateAsync` with per-field `SetProperty` | `Route` is an immutable positional record, so change-tracker mutation is not available. |
| Test depth | Backend integration + frontend proxy | Matches the depth `DeleteRouteTests` and the existing proxy tests set; the form itself stays manual. |

## Scope

**In scope:** `PATCH /routes/{id}` with owner scoping; shared name/tag validation with length caps applied to save and edit; `Optional<T>` JSON plumbing; PATCH proxy handler; edit form on the detail page; extracted tag parsing.

**Out of scope:** editing geometry/distance/date; edit affordance in the library list; component tests for the form; DB-level constraints or migration; optimistic concurrency; share lifecycle changes.

## Architecture / Approach

Detail page form → `PATCH /api/routes/[id]` (Next.js proxy, GUID-guarded, relays bearer token) → `PATCH /routes/{id}` (.NET, `RequireAuthorization`) → ownership fetch for 404 → `ExecuteUpdateAsync` setting only the provided fields. Shares need no changes: `GET /shares/{token}` joins to `Routes` at read time, so edits propagate for free.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared validation + caps | One validator, new length limits, applied to existing `POST /routes` | Touches the shipped save path — a regression here breaks route saving |
| 2. `Optional<T>` + PATCH endpoint | The wrapper, converter, request record, endpoint, full xUnit matrix | Getting absent-vs-null backwards silently turns "clear tags" into a no-op |
| 3. Frontend edit surface | Tag-parsing helper, PATCH proxy + tests, detail-page edit form | Form state must reset correctly on cancel and after a failed save |

**Prerequisites:** S-02 and S-03 shipped (both done). Docker running for the Testcontainers-backed backend suite (`DOCKER_API_VERSION=1.41`).
**Estimated effort:** ~1 session across 3 phases; Phase 2 is the bulk.

## Open Risks & Assumptions

- Adding caps to `POST` widens this change into shipped territory; Phase 1 lands it alone so a regression is unambiguous.
- A pre-existing route whose name exceeds 120 characters could not be re-saved via PATCH without shortening. Assumed absent — names are auto-generated as `"YYYY-MM-DD • NN km"`.
- Last write wins on concurrent edits. Single-owner routes make this a non-scenario.

## Success Criteria (Summary)

- A user can rename and retag a saved route from the detail page and the change survives a reload
- The edited name appears in the library list and on an active public share link
- A PATCH for another user's route returns 404 and leaves the row untouched
