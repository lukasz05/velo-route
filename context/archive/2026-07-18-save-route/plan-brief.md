# Save Route (S-02) — Plan Brief

> Full plan: `context/changes/save-route/plan.md`

## What & Why

Authenticated users can currently generate a loop route but it vanishes when the session ends. This slice adds the ability to save a generated route to a personal library: one-click save, auto-named by date + distance, with optional user-editable name and tags. It's the second step of v2's north star (auth → save → library → GPX from library).

## Starting Point

The `Routes` table already exists and is fully schema-tested (`UserRouteSchemaTests.cs`) — shipped by `data-layer-schema` (F-02): `UserId` FK cascade-delete, `Name`, `Tags` (`text[]`), `DistanceKm`, `Geometry` (`jsonb`), `CreatedAt`. Magic-link auth (S-01) shipped the authenticated-proxy pattern (`useAuth().getToken()` → Next.js proxy forwarding `Authorization` → backend `ClaimsPrincipal` sub extraction) that this slice reuses directly. No new migration, no new auth design — this is the first *write* path into a table that's been ready since F-02.

## Desired End State

A signed-in user generates a route, sees a name field pre-filled `"2026-07-18 • 42 km"` and an optional tags field, edits either if they want, clicks Save, and the button flips to a disabled `"Saved ✓"`. The route now exists as a row in Postgres. Anonymous users clicking Save get Clerk's sign-in modal instead. Anonymous route generation and GPX export are untouched.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
|---|---|---|
| Anonymous save click | Trigger Clerk sign-in modal, no auto-resume | Reuses existing `openSignIn()` pattern; avoids threading route state across the auth boundary |
| Name/tag editing surface | Inline fields before Save (not after) | Library page (S-03, where "after" editing would live) is still blocked on this slice |
| Save confirmation | Button flips to disabled "Saved ✓" | Matches the existing Download-GPX loading-state pattern already in `RouteInfoPanel` |
| Tag input | Comma-separated text field | Trivial, no new component; tags are explicitly optional in the PRD |
| Save error display | Inline red text below button | Matches `RouteInfoPanel`'s existing `downloadError` pattern exactly |
| Test coverage | Backend endpoint tests + frontend proxy test | Matches this repo's existing test depth; no component-test precedent yet |
| Cut-first if tight | Tags input | Explicitly optional per PRD; name editing has more emphasis in the PRD outcome text |

## Scope

**In scope:** authenticated `POST /routes` endpoint, name/tags inline editing, Save button with saved/error states, anonymous → sign-in-modal redirect, backend + proxy tests.

**Out of scope:** "My Routes" library/list view (S-03), post-save editing, route deletion (S-04), public sharing (S-05), tag autocomplete/validation, duplicate-save prevention, toast notifications.

## Architecture / Approach

Backend-first: one new `POST /routes` endpoint in `Program.cs`, converting the frontend's `{longitude, latitude}` coordinate list to the `GeoJsonLineString` shape `Route.Geometry` already expects, scoped by the caller's JWT `sub`. Frontend second: a new `/api/routes` proxy route (forwards both the `Authorization` header and the JSON body — no existing proxy does both today) plus UI additions to the existing `RouteInfoPanel` component.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Backend save endpoint | `POST /routes`, validated, persisted, tested | Tests need a pre-seeded `User` row for the FK — easy to miss, called out explicitly in the plan |
| 2. Frontend save UI | Name/tags fields, Save button, proxy route, tests | First frontend route-handler test in the repo — no existing pattern to copy exactly |

**Prerequisites:** S-01 (magic-link-auth) and F-02 (data-layer-schema) — both done.
**Estimated effort:** ~1-2 sessions across 2 phases.

## Open Risks & Assumptions

- Assumes a `Users` row always exists by the time Save is clickable (created by `/auth/sync` on sign-in) — if that assumption ever breaks, the FK insert fails with no specific error handling beyond the generic 500 path.
- No component-test precedent in this repo yet (only `ErrorMessage.test.tsx` exists) — Phase 2's proxy-route test establishes the pattern for testing Next.js route handlers, but the plan explicitly does not add component tests for the new inline form.

## Success Criteria (Summary)

- Signed-in user can save a generated route with one click; it persists correctly in Postgres.
- Anonymous save attempt opens sign-in, sends no request.
- Anonymous route generation and GPX export show no regression.
