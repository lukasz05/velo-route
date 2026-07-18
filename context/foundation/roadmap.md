---
project: "VeloRoute"
version: 2
status: draft
created: 2026-07-04
updated: 2026-07-18
prd_version: 2
main_goal: quality
top_blocker: none
---

# Roadmap: VeloRoute v2

> Derived from `context/foundation/prd-v2.md` (v2) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

VeloRoute v1 lets anonymous cyclists generate a loop route and download it as GPX — no account required. Two gaps remain: routes vanish when the session ends (no persistence), and the routing algorithm doesn't leverage OSM scenic or low-traffic road tags or route near cyclist POIs (cafes, water, rest stops). v2 closes both: a personal route library tied to a magic-link account, and an improved algorithm that draws on OSM data. Anonymous route generation is preserved without login.

## North star

**S-03: route-library** — the smallest complete proof that the core v2 loop works.

> "North star" here means the smallest end-to-end slice whose successful delivery proves the core product hypothesis — placed as early as its Prerequisites allow because everything else only matters if this works. The v2 hypothesis is that authenticated users will save routes and access them from a personal library. Nothing is validated until the full cycle is closed: sign up via magic link → save a generated route → navigate to My Routes → open the saved route → download GPX.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| F-01 | `auth-provider-scaffold` | (foundation) Microsoft Entra External ID wired; OIDC/MSAL in Next.js; JWT validation via JWKS in .NET backend; auth middleware configured so anonymous route endpoints stay unprotected | — | FR-001, FR-002, FR-003, FR-012, FR-013, Access Control | done |
| F-02 | `data-layer-schema` | (foundation) Azure Database for PostgreSQL Flexible Server deployed; users + routes schema + migrations; DB client wired to backend | — | FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, NFR (account deletion) | done |
| S-07 | `routing-quality-osm` | generate routes that prefer OSM scenic/low-traffic roads and pass near cyclist POIs (cafes, water, rest stops) — best-effort; distance constraint always wins | — | FR-010, FR-011, FR-012, FR-013 | ready |
| S-01 | `magic-link-auth` | sign up by entering an email (receive a magic link), log in via the link with a clear expiry error message and one-click re-send option, and log out | F-01, F-02 | FR-001, FR-002, FR-003, US-01 | done |
| S-02 | `save-route` | save a generated route to their personal library (one-click; auto-name date + distance; optional user-editable name and tags) | S-01 | FR-004, FR-005, US-01 | done |
| S-06 | `account-deletion` | permanently delete their account and all associated data (email + saved routes) self-serve from account settings | S-01, F-02 | FR-003, NFR (account deletion) | ready |
| S-03 | `route-library` | view My Routes as a flat list sorted by date, open a saved route on an interactive map, and download its GPX | S-02 | FR-007, FR-008, US-01 | ready |
| S-04 | `delete-route` | delete a saved route after confirming a prompt (hard delete, no recovery) | S-02 | FR-006 | blocked |
| S-05 | `public-route-sharing` | share a saved route via a public link viewable without login; link shows the exact saved route snapshot, not a re-generation | S-02 | FR-009 | blocked |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme | Chain | Note |
|---|---|---|---|
| A | Auth + Library core | `F-01` → `S-01` → `S-02` → `S-03` | Main v2 pipeline; S-03 is the north star. Blocked until auth provider decided. |
| B | Data + Account lifecycle | `F-02` → `S-06` | F-02 joins Stream A at S-01 (prerequisite alongside F-01); S-06 can run parallel to S-02 once S-01 is done. |
| C | Route management | `S-04` / `S-05` | Both depend on S-02; parallel with S-03. No foundation prerequisite of their own. |
| D | Routing quality | `S-07` | Standalone; no auth/data dependency. Only stream that can start before the auth/data decisions are resolved. |

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
- **Blockers:** —
- **Unknowns:**
  - Which OSM data source for POI and scenic-tag queries? Overpass API is the standard free option; Nominatim covers geocoding only. Self-hosting is an option but adds infrastructure scope. — Owner: TBD. Block: no (resolvable during planning; Overpass API is the default path).
  - What is the latency impact of OSM Overpass queries on route generation time? The v1 ≤5 s NFR was not re-confirmed for v2 (PRD Open Question 1). — Owner: engineering. Block: no (measure during implementation; define threshold before shipping v2).
- **Risk:** OSM scenic tag density varies widely by region — improvement may be imperceptible in areas with sparse tagging. Algorithm must fall back gracefully so route quality never regresses below v1. Acceptable-quality definition should be agreed before starting to avoid open-ended tuning (same risk that required explicit acceptance thresholds in the v1 S-03 loop-algorithm-tuning slice).
- **Status:** ready

### S-01: Magic link auth

- **Outcome:** user can sign up by entering their email address and receiving a magic link; log in to an existing account by clicking the link, with a clear expiry error message and one-click re-send option; and log out.
- **Change ID:** `magic-link-auth`
- **PRD refs:** FR-001, FR-002, FR-003, US-01
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - ~~Email code (OTP) vs magic link?~~ — **Resolved 2026-07-15:** magic link (Clerk `email_link` strategy), prebuilt components in modal mode. Matches the change-id and the PRD's Access Control section; the roadmap's earlier "6-digit one-time code" wording was an unresolved carry-over from F-01 planning and has been corrected here.
  - Link expiry window — Clerk default expiry is provider-configured; confirm exact value in Clerk dashboard during implementation. Block: no.
- **Risk:** Email delivery reliability is a dependency outside the app's control; deliverability must be verified with Clerk's free-tier email sending limits before shipping.
- **Status:** done

### S-02: Save route

- **Outcome:** authenticated user can save a generated route to their personal library with one click; the route is auto-named with date + distance (e.g. "2026-07-04 • 42 km"); the user can optionally edit the name and optionally add tags before or after saving.
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
- **Status:** ready

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
- **Status:** ready

### S-04: Delete route

- **Outcome:** authenticated user can delete a saved route from their library after confirming a prompt; the deletion is immediate and irreversible (hard delete, no recovery).
- **Change ID:** `delete-route`
- **PRD refs:** FR-006
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Hard delete with no undo. Confirmation prompt is the only safeguard. PRD explicitly rejects soft-delete ("soft-delete adds complexity not justified in v2"); do not re-introduce it here.
- **Status:** blocked

### S-05: Public route sharing

- **Outcome:** authenticated user can generate a public shareable link for a saved route; anyone with the link can view the route as a snapshot (exact saved geometry, not a re-generation) on an interactive map, without logging in.
- **Change ID:** `public-route-sharing`
- **PRD refs:** FR-009
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-04
- **Blockers:** —
- **Unknowns:**
  - What is the URL shape for public links? A random opaque token (e.g. `/r/<uuid>`) is the standard privacy-safe approach — avoids leaking route IDs in sequential enumeration. — Owner: TBD. Block: no (resolvable during planning; opaque token is the default path).
  - Should public links be revocable? PRD requires they "must remain stable — once shared, a URL must remain valid." Revocation would contradict this. — Owner: user. Block: no (PRD is clear: links are not revocable in v2).
- **Risk:** Public snapshot links expose route geometry (coordinates) to anyone with the URL. The snapshot must store the geometry at save time, not re-query the DB at view time — otherwise a deleted route's link would 404 unexpectedly. The PRD's "snapshot sharing" requirement implies the geometry is stored with the share record, not just a pointer to the route row.
- **Status:** blocked

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| F-01 | `auth-provider-scaffold` | Auth provider scaffold — Clerk + email OTP + .NET JWT middleware | yes | Run `/10x-plan auth-provider-scaffold`; provider decided: Clerk (superseded Entra External ID 2026-07-07, Azure region policy blocker) |
| F-02 | `data-layer-schema` | Data layer — Azure Postgres schema + EF Core migrations (users + routes) | yes | Run `/10x-plan data-layer-schema`; host decided: Azure Database for PostgreSQL Flexible Server |
| S-07 | `routing-quality-osm` | Routing quality — OSM scenic/low-traffic preference + cyclist POI proximity | yes | Run `/10x-plan routing-quality-osm`; no auth/data dependency |
| S-01 | `magic-link-auth` | Magic link auth — signup, login, logout (FR-001–FR-003) | yes | Run `/10x-plan magic-link-auth`; F-01 + F-02 done, unblocked |
| S-02 | `save-route` | Save route to personal library — one-click, auto-name, optional tags (FR-004–FR-005) | yes | Run `/10x-plan save-route`; S-01 done, unblocked |
| S-06 | `account-deletion` | Account deletion — self-serve hard delete of account + all routes (NFR) | yes | Run `/10x-plan account-deletion`; S-01 + F-02 done, unblocked; parallel with S-02 |
| S-03 | `route-library` | My Routes library — flat list, open saved route on map, GPX download (FR-007–FR-008) | no | North star; depends on S-02 |
| S-04 | `delete-route` | Delete route — confirmation prompt + hard delete (FR-006) | no | Depends on S-02; parallel with S-03 |
| S-05 | `public-route-sharing` | Public route sharing — shareable link, snapshot, no login required (FR-009) | no | Depends on S-02; parallel with S-03 and S-04 |

## Open Roadmap Questions

1. ~~**Which magic link provider?**~~ — **Resolved 2026-07-04:** Microsoft Entra External ID + email OTP. **Superseded 2026-07-07:** Clerk + email OTP. Entra CIAM tenant creation blocked by the available Azure subscription's system-enforced region policy (no override available on the "Azure for Students" offer). Clerk removes the Azure dependency entirely; OIDC/JWKS validation in .NET unchanged in shape.

2. ~~**Which DB + host?**~~ — **Resolved 2026-07-04:** Azure Database for PostgreSQL Flexible Server. EF Core migrations; JSONB for route geometry.

3. **Route generation latency under OSM POI querying.** The v1 ≤5 s NFR was not re-confirmed after adding Overpass API queries to the route generation path. Measure during S-07 implementation and define an acceptable threshold before shipping v2. — Owner: engineering. Block: no (does not block S-07 planning; defines the done-condition).

4. **Delivery timeline.** `delivery_weeks` is open-ended (after-hours, no hard deadline). An estimate would complete the PRD frontmatter. — Owner: user. Block: no.

## Parked

- **Multiple route proposals per request** — Why parked: PRD §Non-Goals ("still one route generated per request; multiple-proposal support deferred to v3 once the algorithm is proven at scale").
- **Library search or filter** — Why parked: PRD §Non-Goals ("flat list sorted by date; search and filter deferred to v3").
- **Social feed / community features / public route discovery** — Why parked: PRD §Non-Goals ("no browsing other users' routes, no following, no community feed; route sharing is link-only").
- **Point-to-point routes** — Why parked: PRD §Non-Goals ("loop routes only; point-to-point deferred").
- **Imperial units** — Why parked: PRD §Non-Goals ("kilometres only; miles deferred").
- **Offline-first / PWA** — Why parked: PRD §Non-Goals ("app requires a network connection").
- **Strava Segments API** — Why parked: PRD §Constraints ("requires OAuth and is not free/public; OSM is the only data source for routing improvements in v2").
- **Library pagination, search, filter** — Why parked: PRD §Non-Goals ("flat list is acceptable for v2 volume; search/filter deferred to v3").

## Done

- **F-01: (foundation) Microsoft Entra External ID wired; OIDC/MSAL in Next.js; JWT validation via JWKS in .NET backend; auth middleware configured so anonymous route endpoints stay unprotected** — Archived 2026-07-10 → `context/archive/2026-07-04-auth-provider-scaffold/`. Lesson: —.
- **F-02: (foundation) Postgres DB deployed and reachable from the .NET backend; schema with `users` and `routes` tables plus migrations; DB client wired and connection-tested; account hard-delete cascade configured (deleting a user row removes all associated route rows).** — Archived 2026-07-11 → `context/archive/2026-07-10-data-layer-schema/`. Lesson: —.
- **S-01: user can sign up by entering their email address and receiving a magic link; log in to an existing account by clicking the link, with a clear expiry error message and one-click re-send option; and log out.** — Archived 2026-07-18 → `context/archive/2026-07-15-magic-link-auth/`. Lesson: —.
- **S-02: authenticated user can save a generated route to their personal library with one click; the route is auto-named with date + distance (e.g. "2026-07-04 • 42 km"); the user can optionally edit the name and optionally add tags before or after saving.** — Archived 2026-07-18 → `context/archive/2026-07-18-save-route/`. Lesson: —.
