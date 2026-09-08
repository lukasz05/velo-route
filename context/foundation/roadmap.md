---
project: "VeloRoute"
version: 2
status: active
created: 2026-07-04
updated: 2026-09-08
prd_version: 2
main_goal: quality
top_blocker: none
---

# Roadmap: VeloRoute v2

> Derived from `context/foundation/prd-v2.md` (v2) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

VeloRoute v1 lets anonymous cyclists generate a loop route and download it as GPX — no account required. Two gaps remain: routes vanish when the session ends (no persistence), and the routing algorithm doesn't leverage OSM scenic or low-traffic road tags or route near cyclist POIs (cafes, water, rest stops). v2 closes both: a personal route library tied to a passwordless email-code account, and an improved algorithm that draws on OSM data. Anonymous route generation is preserved without login.

## North star

**S-03: route-library** — the smallest complete proof that the core v2 loop works.

> "North star" here means the smallest end-to-end slice whose successful delivery proves the core product hypothesis — placed as early as its Prerequisites allow because everything else only matters if this works. The v2 hypothesis is that authenticated users will save routes and access them from a personal library. Nothing is validated until the full cycle is closed: sign up via emailed verification code → save a generated route → navigate to My Routes → open the saved route → download GPX.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| F-01 | `auth-provider-scaffold` | (foundation) Clerk wired with email OTP; `@clerk/nextjs` in Next.js App Router; JWT validation via JWKS in .NET backend; auth middleware configured so anonymous route endpoints stay unprotected | — | FR-001, FR-002, FR-003, FR-012, FR-013, Access Control | done |
| F-02 | `data-layer-schema` | (foundation) Azure Database for PostgreSQL Flexible Server deployed; users + routes schema + migrations; DB client wired to backend | — | FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, NFR (account deletion) | done |
| S-07 | `routing-quality-osm` | generate routes that prefer OSM scenic/low-traffic roads and pass near cyclist POIs (cafes, water, rest stops) — best-effort; distance constraint always wins | — | FR-010, FR-011, FR-012, FR-013 | parked |
| S-01 | `magic-link-auth` | sign up by entering an email (receive a magic link), log in via the link with a clear expiry error message and one-click re-send option, and log out | F-01, F-02 | FR-001, FR-002, FR-003, US-01 | done |
| S-02 | `save-route` | save a generated route to their personal library (one-click; auto-name date + distance; optional user-editable name and tags) | S-01 | FR-004, FR-005, US-01 | done |
| S-06 | `account-deletion` | permanently delete their account and all associated data (email + saved routes) self-serve from account settings | S-01, F-02 | FR-003, NFR (account deletion) | done |
| S-03 | `route-library` | view My Routes as a flat list sorted by date, open a saved route on an interactive map, and download its GPX | S-02 | FR-007, FR-008, US-01 | done |
| S-04 | `delete-route` | delete a saved route after confirming a prompt (hard delete, no recovery) | S-02 | FR-006 | done |
| S-05 | `public-route-sharing` | share a saved route via a public link viewable without login; link is a live read-through to the owner's saved route (not a snapshot) and dies if the route is deleted or unshared | S-02 | FR-009 | done |
| S-08 | `edit-route` | rename a saved route and change its tags after saving, from the library | S-02, S-03 | FR-005 | done |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme | Chain | Note |
|---|---|---|---|
| A | Auth + Library core | `F-01` → `S-01` → `S-02` → `S-03` | Main v2 pipeline; S-03 is the north star. Blocked until auth provider decided. |
| B | Data + Account lifecycle | `F-02` → `S-06` | F-02 joins Stream A at S-01 (prerequisite alongside F-01); S-06 can run parallel to S-02 once S-01 is done. |
| C | Route management | `S-04` / `S-05` / `S-08` | All depend on S-02; parallel with S-03 (S-08 also wants S-03's library view as the place to edit from). No foundation prerequisite of their own. |
| D | Routing quality | `S-07` | Standalone; no auth/data dependency. Parked 2026-08-05 — see S-07 status. |

## Baseline

What's already in place in the codebase as of 2026-07-04 (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** present — Next.js 15 + React 19 + TypeScript; MapLibre GL; RouteForm, RouteMap, RouteInfoPanel functional; Azure SWA + GitHub Actions CI/CD (`src/frontend/`)
- **Backend / API:** present — .NET 10 minimal API; `POST /routes/loop` + `POST /routes/gpx`; ORS HTTP client with retry/circuit-breaker; GPX serialiser; 43 backend unit + integration tests (`src/backend/`)
- **Data:** absent — stateless by design; no DB driver, ORM, schema, or migrations
- **Auth:** absent — no user auth; `Program.cs` only references ORS API key header; no session/token code or auth middleware
- **Deploy / infra:** present — Azure SWA (frontend) + Azure App Service (backend); GitHub Actions CI/CD on both; `dotnet test` gate on PRs
- **Observability:** partial — .NET default logging in `appsettings.json`; no error tracking, distributed tracing, or metrics

## Foundations

### F-01: Auth provider scaffold

- **Outcome:** (foundation) Clerk application configured for external (customer) identities with email OTP as the sign-in method; `@clerk/nextjs` integrated in Next.js App Router; .NET backend validates Clerk-issued JWTs via the JWKS endpoint; route-level auth middleware configured so that `POST /routes/loop` and `POST /routes/gpx` remain accessible to unauthenticated users and new library endpoints require a valid session.
- **Change ID:** `auth-provider-scaffold`
- **PRD refs:** FR-001, FR-002, FR-003, FR-012, FR-013, Access Control section ("unauthenticated users retain full access to route generation and GPX export")
- **Unlocks:** S-01 (email OTP auth UI requires token issuance and session verification to be in place)
- **Prerequisites:** —
- **Parallel with:** F-02 (data layer schema has no dependency on auth provider choice)
- **Blockers:** —
- **Unknowns:** ~~Which magic link provider?~~ — **Resolved 2026-07-04:** Microsoft Entra External ID + email OTP. **Superseded 2026-07-07:** switched to Clerk + email OTP — Entra CIAM tenant creation blocked by Azure subscription region policy (`ciamDirectories` resource type only deploys to broad meta-regions that don't intersect the "Azure for Students" subscription's system-enforced region allowlist, which isn't customer-removable). Clerk has no Azure dependency; F-02 stays on Azure Postgres unaffected.
- **Risk:** `@clerk/nextjs` + Next.js App Router (React Server Components) behavior should be checked against current SDK docs, not training data — `src/frontend/AGENTS.md` flags this Next.js/React version as having training-data-breaking changes. No official Clerk .NET package exists; backend JWKS/OIDC discovery against Clerk's endpoint needs verifying during implementation.
- **Status:** done

### F-02: Data layer schema

- **Outcome:** (foundation) Postgres DB deployed and reachable from the .NET backend; schema with `users` and `routes` tables plus migrations; DB client wired and connection-tested; account hard-delete cascade configured (deleting a user row removes all associated route rows).
- **Change ID:** `data-layer-schema`
- **PRD refs:** FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, NFR ("when a user deletes their account, all associated data is permanently deleted")
- **Unlocks:** S-01 (user row created on first sign-in), S-06 (account deletion cascade requires schema in place)
- **Prerequisites:** —
- **Parallel with:** F-01
- **Blockers:** —
- **Unknowns:** ~~Which DB host?~~ — **Resolved 2026-07-04:** Azure Database for PostgreSQL Flexible Server. Stays Azure-only; JSONB for route geometry; EF Core migrations.
- **Risk:** Route geometry payloads can be large for long routes. Decide the geometry column type (JSONB array vs PostGIS geometry vs encoded polyline) before writing migrations — changing it later requires a data migration. JSONB is the recommended default unless PostGIS spatial queries are needed (they are not in v2 scope).
- **Status:** done

## Slices

### S-07: Routing quality — OSM scenic/low-traffic + cyclist POIs

- **Outcome:** route generation prefers roads tagged as scenic or low-traffic in OSM on a best-effort basis (graceful fallback where tags are absent); route passes near cyclist POIs (cafes, water points, rest stops from OSM) where possible without violating the user's min–max km distance constraint (distance constraint wins; POIs are best-effort).
- **Change ID:** `routing-quality-osm`
- **PRD refs:** FR-010, FR-011, FR-012, FR-013
- **Prerequisites:** —
- **Parallel with:** F-01, F-02, S-01, S-02, S-03, S-04, S-05, S-06
- **Blockers:** public Overpass API reliability. Live-verified 2026-08-05: `overpass-api.de` and 3 of 4 checked mirrors (`overpass.kumi.systems`, `overpass.private.coffee`, `maps.mail.ru`) either 504'd or stalled on connect under real usage; only `overpass.openstreetmap.fr` responded. The PRD's OSM-only/Overpass-API data-source constraint (FR-010/FR-011) makes this a hard dependency for both mechanisms this slice needs (POI-directed bearing nudging, scenic/low-traffic way-tag scoring), not a best-effort corner of it.
- **Unknowns:**
  - ~~Which OSM data source for POI and scenic-tag queries?~~ — **Resolved 2026-07-26 → reopened 2026-08-05:** Overpass API was implemented (Phases 1-4 of `routing-quality-osm`, later reverted) but proved unreliable enough in practice to block shipping. Options to revisit: (a) multi-mirror fallback list tried in sequence — cheap, keeps OSM as the source; (b) self-hosted Overpass instance — reliable but adds infra scope, previously deferred; (c) drop OSM/Overpass entirely and derive the "scenic/low-traffic" signal from ORS's own `extra_info` (waytype/surface, already fetched for `pavedRatio`/`smoothnessScore`) — no external dependency, but loses OSM-specific tags (cycleway, cycle-network membership) and the POI-proximity mechanism has no ORS-only equivalent. — Owner: user. Block: yes (blocks re-opening this slice).
  - What is the latency impact of OSM Overpass queries on route generation time? Not reached — abandoned before Phase 5's latency measurement step.
- **Risk:** OSM scenic tag density varies widely by region — improvement may be imperceptible in areas with sparse tagging. Algorithm must fall back gracefully so route quality never regresses below v1. Acceptable-quality definition should be agreed before starting to avoid open-ended tuning (same risk that required explicit acceptance thresholds in the v1 S-03 loop-algorithm-tuning slice).
- **Status:** parked

### S-01: Magic link auth

- **Outcome:** user can sign up by entering their email address and receiving a magic link; log in to an existing account by clicking the link, with a clear expiry error message and one-click re-send option; and log out. (As shipped 2026-07-15; the link was replaced by an emailed code on 2026-09-08 — see Unknowns below.)
- **Change ID:** `magic-link-auth`
- **PRD refs:** FR-001, FR-002, FR-003, US-01
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - ~~Email code (OTP) vs magic link?~~ — **Resolved 2026-07-15:** magic link (Clerk `email_link` strategy), prebuilt components in modal mode. Matches the change-id and the PRD's Access Control section; the roadmap's earlier "6-digit one-time code" wording was an unresolved carry-over from F-01 planning and has been corrected here. **Superseded 2026-09-08:** switched to email code (Clerk `email_code` strategy) after repeated `client_mismatch` failures — a development instance tracks the client via the `__clerk_db_jwt` dev-browser token, which link prefetch by a mail gateway, third-party cookie blocking, and `localhost`/`127.0.0.1` origin drift each defeat. Dashboard-only change; `openSignIn()` renders whatever the instance is configured for, so no code moved. The `magic-link-auth` change-id and this slice's shipped record are left as historical fact.
  - Code expiry window — Clerk default expiry is provider-configured; confirm exact value in Clerk dashboard during implementation. Block: no.
- **Risk:** Email delivery reliability is a dependency outside the app's control; deliverability must be verified with Clerk's free-tier email sending limits before shipping.
- **Status:** done

### S-02: Save route

- **Outcome:** authenticated user can save a generated route to their personal library with one click; the route is auto-named with date + distance (e.g. "2026-07-04 • 42 km"); the user can optionally edit the name and optionally add tags before saving. (Editing a route's name or tags *after* it is saved was in the original outcome text but shipped separately as S-08 on 2026-09-08 via `PATCH /routes/{id}`.)
- **Change ID:** `save-route`
- **PRD refs:** FR-004, FR-005, US-01
- **Prerequisites:** S-01
- **Parallel with:** S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The save action stores the full route geometry (coordinate list) in Postgres. Route geometry payloads can be large for long routes; the schema's data type for the geometry column (JSON array, PostGIS geometry, or encoded polyline) should be decided in F-02 to avoid a costly migration later.
- **Status:** done

### S-06: Account deletion

- **Outcome:** authenticated user can permanently delete their account and all associated data (email address + all saved routes) self-serve from account settings, with no support contact required; the deletion is immediate and irreversible.
- **Change ID:** `account-deletion`
- **PRD refs:** FR-003, NFR ("when a user deletes their account, all associated data is permanently deleted; account deletion is self-serve from account settings")
- **Prerequisites:** S-01, F-02
- **Parallel with:** S-02
- **Blockers:** —
- **Unknowns:**
  - ~~Does the chosen auth provider support programmatic user deletion?~~ — **Resolved 2026-07-04, provider updated 2026-07-07:** Clerk supports user deletion via its Backend API (`DELETE /users/{id}`). Backend calls Clerk's Backend API on account delete, then cascades the Postgres row via FK constraint.
- **Risk:** Hard delete with no soft-delete buffer means a mis-click permanently destroys a user's route library. A confirmation prompt (e.g. "type DELETE to confirm") is the minimum safeguard; the PRD requires a prompt but does not specify its form.
- **Status:** done

### S-03: Route library

- **Outcome:** authenticated user can view their route library as a flat list sorted by date (no search or filter); open any saved route to see it on an interactive map; and download its GPX file.
- **Change ID:** `route-library`
- **PRD refs:** FR-007, FR-008, US-01
- **Prerequisites:** S-02
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:**
  - Does the library page need pagination from day one, or is a single flat list acceptable until volume warrants it? PRD says "flat list sorted by date; search and filter deferred to v3." — Owner: user. Block: no (flat list is acceptable; pagination can be added without a breaking change).
- **Risk:** The "My Routes" library page must render within 2 seconds (PRD secondary success criterion). With route geometry stored in Postgres, the list query must not load full geometry for every row — return summary fields (name, date, distance) and lazy-load geometry only when a route is opened.
- **Status:** done

### S-04: Delete route

- **Outcome:** authenticated user can delete a saved route from their library after confirming a prompt; the deletion is immediate and irreversible (hard delete, no recovery).
- **Change ID:** `delete-route`
- **PRD refs:** FR-006
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Hard delete with no undo. Confirmation prompt is the only safeguard. PRD explicitly rejects soft-delete ("soft-delete adds complexity not justified in v2"); do not re-introduce it here.
- **Status:** done

### S-05: Public route sharing

- **Outcome:** authenticated user can generate a public shareable link for a saved route, and later revoke ("stop sharing") it; anyone with an active link can view the route (live geometry read from the owner's saved route, not a re-generation) on an interactive map, without logging in. The link is tied to the source route's lifetime — deleting the route also removes the share.
- **Change ID:** `public-route-sharing`
- **PRD refs:** FR-009
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-04
- **Blockers:** —
- **Unknowns:**
  - ~~What is the URL shape for public links?~~ — **Resolved 2026-07-26 (planning):** short opaque random token (~12-char base62), not a GUID — enumeration-resistant, shorter URLs.
  - ~~Should public links be revocable?~~ — **Resolved 2026-07-26 (planning), reversed later same session:** yes, revocable. `DELETE /routes/{id}/share` hard-deletes the `Shares` row; re-sharing afterward mints a brand-new token, not the same URL. Combined with the FK-cascade-delete behavior below, a share now ends either when the owner revokes it or when the source route is deleted.
- **Risk:** ~~Public snapshot links... store the geometry at save time...~~ — **Superseded 2026-07-26:** the team chose FK-to-Route (no geometry copy) over a snapshot table, accepting that a share stops working if the source route is deleted — see PRD-v2 Constraints (amended 2026-07-26) and Scope of Change → Route library. Revisit only if user feedback shows recipients are surprised by a link dying.
- **Status:** done

### S-08: Edit saved route

- **Outcome:** authenticated user can rename a saved route and change its tags from the library, after it has been saved; the change persists and is reflected in the library list, the route detail view, and any active public share.
- **Change ID:** `edit-route`
- **PRD refs:** FR-005 ("optional user-editable name and optional tags")
- **Prerequisites:** S-02, S-03
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:**
  - ~~Whether editing is inline in the library list or on the route detail view.~~ — **Resolved 2026-09-08 (planning):** route detail view only, alongside the existing delete/share affordances; the library list gets no edit control.
- **Risk:** Low. The write path mirrors the existing owner-scoped `DELETE /routes/{id}` — same auth check, same 404-on-foreign-id semantics, no cascade or share-lifetime interaction (a share reads the route live, so an edit propagates for free). The one thing to get right is that a foreign `PATCH` returns 404 rather than 403, matching delete, so the endpoint doesn't leak which ids exist.
- **Status:** done

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| F-01 | `auth-provider-scaffold` | Auth provider scaffold — Clerk + email OTP + .NET JWT middleware | shipped | Archived → `context/archive/2026-07-04-auth-provider-scaffold/` |
| F-02 | `data-layer-schema` | Data layer — Azure Postgres schema + EF Core migrations (users + routes) | shipped | Archived → `context/archive/2026-07-10-data-layer-schema/` |
| S-07 | `routing-quality-osm` | Routing quality — OSM scenic/low-traffic preference + cyclist POI proximity | no | Parked 2026-08-05 — public Overpass API unreliable; needs a data-source decision (multi-mirror, self-host, or ORS-only) before re-planning, see S-07 Unknowns |
| S-01 | `magic-link-auth` | Magic link auth — signup, login, logout (FR-001–FR-003) | shipped | Archived → `context/archive/2026-07-15-magic-link-auth/` |
| S-02 | `save-route` | Save route to personal library — one-click, auto-name, optional tags (FR-004–FR-005) | shipped | Archived → `context/archive/2026-07-18-save-route/`. Post-save name/tag editing was in the outcome text but shipped later, in S-08 |
| S-03 | `route-library` | Route library — flat list, open on map, GPX download (FR-007–FR-008) | shipped | Archived → `context/archive/2026-07-18-route-library/` |
| S-06 | `account-deletion` | Account deletion — self-serve hard delete of account + all routes (NFR) | shipped | Archived → `context/archive/2026-07-26-account-deletion/` |
| S-04 | `delete-route` | Delete route — confirmation prompt + hard delete (FR-006) | shipped | Archived → `context/archive/2026-07-18-delete-route/` |
| S-05 | `public-route-sharing` | Public route sharing — shareable link, live read-through, no login required (FR-009) | shipped | Archived → `context/archive/2026-07-26-public-route-sharing/` |
| S-08 | `edit-route` | Edit saved route — rename + retag after saving (FR-005) | shipped | Archived → `context/archive/2026-09-08-edit-route/`. Closed the post-save editing gap S-02 left open |

## Open Roadmap Questions

1. ~~**Which magic link provider?**~~ — **Resolved 2026-07-04:** Microsoft Entra External ID + email OTP. **Superseded 2026-07-07:** Clerk + email OTP. Entra CIAM tenant creation blocked by the available Azure subscription's system-enforced region policy (no override available on the "Azure for Students" offer). Clerk removes the Azure dependency entirely; OIDC/JWKS validation in .NET unchanged in shape.

2. ~~**Which DB + host?**~~ — **Resolved 2026-07-04:** Azure Database for PostgreSQL Flexible Server. EF Core migrations; JSONB for route geometry.

3. **Route generation latency under OSM POI querying.** Superseded 2026-08-05 — S-07 was reverted before this was measured; the blocking issue turned out to be Overpass API *reliability*, not latency. Re-open only if/when S-07 resumes.

4. **OSM data-source strategy for S-07.** The public Overpass API (`overpass-api.de` + checked mirrors) proved unreliable 2026-08-05 (see S-07 Blockers). Before S-07 can be re-planned, pick one: multi-mirror fallback list, self-hosted Overpass, or drop OSM/Overpass in favor of ORS's own `extra_info` road-class data (loses OSM-specific tags and the POI-proximity mechanism). — Owner: user. Block: yes (blocks S-07 re-planning only).

5. **Delivery timeline.** `delivery_weeks` is open-ended (after-hours, no hard deadline). An estimate would complete the PRD frontmatter. — Owner: user. Block: no.

## Parked

- **Multiple route proposals per request** — Why parked: PRD §Non-Goals ("still one route generated per request; multiple-proposal support deferred to v3 once the algorithm is proven at scale").
- **Library search or filter** — Why parked: PRD §Non-Goals ("flat list sorted by date; search and filter deferred to v3").
- **Social feed / community features / public route discovery** — Why parked: PRD §Non-Goals ("no browsing other users' routes, no following, no community feed; route sharing is link-only").
- **Point-to-point routes** — Why parked: PRD §Non-Goals ("loop routes only; point-to-point deferred").
- **Imperial units** — Why parked: PRD §Non-Goals ("kilometres only; miles deferred").
- **Offline-first / PWA** — Why parked: PRD §Non-Goals ("app requires a network connection").
- **Strava Segments API** — Why parked: PRD §Constraints ("requires OAuth and is not free/public; OSM is the only data source for routing improvements in v2").
- **Library pagination, search, filter** — Why parked: PRD §Non-Goals ("flat list is acceptable for v2 volume; search/filter deferred to v3").
- **Start-point wiggle** — Why parked: captured during `routing-quality-osm` (S-07) planning (2026-07-26); shifting the actual start/end coordinate toward a higher-quality direction changes a user-visible contract (GPX/marker no longer matches the entered point exactly) and needs its own scoping (radius, opt-in vs. default, interaction with the distance constraint) before it can be planned. S-07 keeps the start/end pinned exactly to user input; see `context/foundation/route-enhancement-ideas.md` Idea #7.
- **S-07: Routing quality — OSM scenic/low-traffic + cyclist POIs** — Why parked: implemented (Phases 1-4, `routing-quality-osm`) then reverted 2026-08-05 after the public Overpass API proved unreliable under real usage (repeated 504s / connect stalls across `overpass-api.de` and most checked mirrors — see S-07 Blockers). PRD's OSM-only data-source constraint (FR-010/FR-011) needs revisiting before this can be re-planned: multi-mirror fallback, self-hosting, or dropping OSM in favor of ORS's own `extra_info` are the live options. Code reverted via clean `git revert`, all tests green post-revert; planning artifacts archived 2026-09-07 to `context/archive/2026-07-26-routing-quality-osm/` — read them for reference before re-planning in whichever direction is chosen.

## Done

- **F-01: (foundation) Clerk wired with email OTP; `@clerk/nextjs` in Next.js App Router; JWT validation via JWKS in .NET backend; auth middleware configured so anonymous route endpoints stay unprotected** — Archived 2026-07-10 → `context/archive/2026-07-04-auth-provider-scaffold/`. Lesson: Entra External ID was the 2026-07-04 decision; the "Azure for Students" region policy blocked CIAM tenant creation and forced the switch to Clerk on 2026-07-07 — verify provider tenant provisioning against the actual subscription before committing a foundation slice to it.
- **F-02: (foundation) Postgres DB deployed and reachable from the .NET backend; schema with `users` and `routes` tables plus migrations; DB client wired and connection-tested; account hard-delete cascade configured (deleting a user row removes all associated route rows).** — Archived 2026-07-11 → `context/archive/2026-07-10-data-layer-schema/`. Lesson: —.
- **S-01: user can sign up by entering their email address and receiving a magic link; log in to an existing account by clicking the link, with a clear expiry error message and one-click re-send option; and log out.** — Archived 2026-07-18 → `context/archive/2026-07-15-magic-link-auth/`. Lesson: —.
- **S-02: authenticated user can save a generated route to their personal library with one click; the route is auto-named with date + distance (e.g. "2026-07-04 • 42 km"); the user can optionally edit the name and optionally add tags before saving. (Editing a route's name or tags *after* it is saved was in the original outcome text but shipped separately as S-08 on 2026-09-08 via `PATCH /routes/{id}`.)** — Archived 2026-07-18 → `context/archive/2026-07-18-save-route/`. Lesson: —.
- **S-03: authenticated user can view their route library as a flat list sorted by date (no search or filter); open any saved route to see it on an interactive map; and download its GPX file.** — Archived 2026-07-18 → `context/archive/2026-07-18-route-library/`. Lesson: —.
- **S-04: authenticated user can delete a saved route from their library after confirming a prompt; the deletion is immediate and irreversible (hard delete, no recovery).** — Archived 2026-07-22 → `context/archive/2026-07-18-delete-route/`. Lesson: —.
- **S-06: authenticated user can permanently delete their account and all associated data (email address + all saved routes) self-serve from account settings, with no support contact required; the deletion is immediate and irreversible.** — Archived 2026-09-08 → `context/archive/2026-07-26-account-deletion/`. Lesson: —.
- **S-08: authenticated user can rename a saved route and change its tags from the library, after it has been saved; the change persists and is reflected in the library list, the route detail view, and any active public share.** — Archived 2026-09-08 → `context/archive/2026-09-08-edit-route/`. Lesson: —.
- **S-05: authenticated user can generate a public shareable link for a saved route, and later revoke ("stop sharing") it; anyone with an active link can view the route (live geometry read from the owner's saved route, not a re-generation) on an interactive map, without logging in. The link is tied to the source route's lifetime — deleting the route also removes the share.** — Archived 2026-09-08 → `context/archive/2026-07-26-public-route-sharing/`. Lesson: —.
