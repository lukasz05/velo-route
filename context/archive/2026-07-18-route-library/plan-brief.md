# Route Library — Plan Brief

> Full plan: `context/changes/route-library/plan.md`

## What & Why

S-03 on the v2 roadmap — the "north star" slice: the smallest end-to-end proof that the core v2 loop works (sign up → save a route → open library → download GPX). Adds a "My Routes" library so saved routes (shipped by `save-route`, S-02) are actually viewable and retrievable, not just write-only.

## Starting Point

`save-route` shipped `POST /routes` and the `Routes` table, but no way to read routes back — no list, no detail view, no library page anywhere in the frontend. The `Route` entity stores name/tags/distance/geometry/createdAt but *not* the surface-quality fields (`segments`/`pavedRatio`/`smoothnessScore`) that only exist transiently at generation time.

## Desired End State

A signed-in user clicks "My Routes" in the header, sees their saved routes as a flat list (newest first, with name/date/distance/tags), clicks one, sees it on an interactive map, and downloads its GPX — identical file to what they'd have gotten right after saving. Zero-route and route-not-found states are handled gracefully. Signed-out access redirects home + opens the sign-in modal. Anonymous generation/GPX/save flows are untouched.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Signed-out page access | Redirect to `/` + open sign-in modal | Single consistent auth entry point, no half-rendered gated page | Plan |
| Empty library state | Friendly message + link back to planner | Guides new users toward the save flow instead of looking broken | Plan |
| Detail view missing surface-quality data | New lightweight panel, not a conditional `RouteInfoPanel` | Honest UI, avoids a dual-purpose component with branchy props | Plan |
| GPX download on detail page | Client-side reuse of existing `/api/routes/gpx` | Zero new backend code; same proxy already tested by `save-route` | Plan |
| Detail 404 semantics | 404 for both "doesn't exist" and "not yours" | Avoids leaking route-id existence to non-owners; no UX difference either way | Plan |
| List row fields | Name, date, distance, tags | All four are already captured at save time — showing them avoids a click-through just to tell routes apart | Plan |
| Header nav link | Text link, hidden when signed out | Matches the hide-not-disable pattern `save-route` just established for the Save button | Plan |
| List loading state | Plain text, not a skeleton | No skeleton pattern exists anywhere in this codebase yet; stay consistent | Plan |

## Scope

**In scope:**
- `GET /routes` (summary list, own routes only, newest-first)
- `GET /routes/{id}` (full detail incl. geometry, 404 if missing/not-owned)
- `/my-routes` list page, `/my-routes/[id]` detail page
- Header "My Routes" link (signed-in only)

**Out of scope:**
- Delete (S-04), public sharing (S-05) — separate slices
- Pagination/search/filter — flat list only, per PRD
- Post-save editing of name/tags from the library
- A dedicated GPX-by-id backend endpoint

## Architecture / Approach

Backend-first: two new read endpoints on the existing `Routes` table, both scoped by the Clerk `sub` claim exactly like `POST /routes`. Frontend: two new Client Component pages (avoiding Next 15's async-`params` Server Component change by using `useParams()`), reusing `RouteMap` and the `/api/routes/gpx` proxy unchanged, plus two small new Next.js proxy routes forwarding the Authorization header.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend library endpoints | `GET /routes` + `GET /routes/{id}`, both auth-scoped and tested | Getting the 404-collapses-both-cases scoping right, not leaking cross-user data |
| 2. Frontend library pages | `/my-routes`, `/my-routes/[id]`, header link, 2 new proxy routes | New page-level auth-gating pattern — first page in the app that requires sign-in to render |

**Prerequisites:** S-02 (`save-route`) done — confirmed, archived 2026-07-18
**Estimated effort:** ~1–2 sessions across 2 phases

## Open Risks & Assumptions

- The PRD's "2-second render" NFR for the library page is addressed by the list endpoint's summary-only (no-geometry) projection, but isn't load-tested in this plan — acceptable at v2's expected data volume (PRD: `data_volume: small`)
- Assumes Clerk's page-load session read (no cross-tab reactivity needed here) is reliable, unlike the live in-tab `useUser()` update gap documented as a known limitation in `save-route`

## Success Criteria (Summary)

- Signed-in user can view their saved-route list, open one, see it on the map, and download a working GPX file
- Signed-out access to either page redirects to sign-in, no gated content ever flashes
- Anonymous generation, GPX export, and save are provably unaffected
