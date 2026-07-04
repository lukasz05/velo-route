---
project: "VeloRoute"
version: 2
status: draft
created: 2026-07-04
updated: 2026-07-04
prd_version: 2
main_goal: quality
top_blocker: decisions
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
| F-01 | `auth-provider-scaffold` | (foundation) magic link provider wired; session/token handling + auth middleware in .NET backend; anonymous route endpoints stay unprotected | — | FR-001, FR-002, FR-003, FR-012, FR-013, Access Control | blocked |
| F-02 | `data-layer-schema` | (foundation) Postgres DB deployed; users + routes schema + migrations; DB client wired to backend | — | FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, NFR (account deletion) | blocked |
| S-07 | `routing-quality-osm` | generate routes that prefer OSM scenic/low-traffic roads and pass near cyclist POIs (cafes, water, rest stops) — best-effort; distance constraint always wins | — | FR-010, FR-011, FR-012, FR-013 | ready |
| S-01 | `magic-link-auth` | sign up by entering an email (receive magic link), log in via magic link (clear expiry error + one-click re-send), and log out | F-01, F-02 | FR-001, FR-002, FR-003, US-01 | blocked |
| S-02 | `save-route` | save a generated route to their personal library (one-click; auto-name date + distance; optional user-editable name and tags) | S-01 | FR-004, FR-005, US-01 | blocked |
| S-06 | `account-deletion` | permanently delete their account and all associated data (email + saved routes) self-serve from account settings | S-01, F-02 | FR-003, NFR (account deletion) | blocked |
| S-03 | `route-library` | view My Routes as a flat list sorted by date, open a saved route on an interactive map, and download its GPX | S-02 | FR-007, FR-008, US-01 | blocked |
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

- **Outcome:** (foundation) magic link provider integrated; token issuance and session verification wired in .NET backend; route-level auth middleware configured so that `POST /routes/loop` and `POST /routes/gpx` remain accessible to unauthenticated users and new library endpoints require a valid session.
- **Change ID:** `auth-provider-scaffold`
- **PRD refs:** FR-001, FR-002, FR-003, FR-012, FR-013, Access Control section ("unauthenticated users retain full access to route generation and GPX export")
- **Unlocks:** S-01 (magic link auth UI requires token issuance and session verification to be in place)
- **Prerequisites:** —
- **Parallel with:** F-02 (data layer schema has no dependency on auth provider choice)
- **Blockers:** —
- **Unknowns:**
  - Which magic link provider? Options include Supabase Auth, NextAuth.js + email provider, Resend + custom JWT, Azure AD B2C, Lucia. Choice affects both the .NET backend (token verification) and the Next.js frontend (SDK integration). — Owner: user. Block: yes.
- **Risk:** Auth provider choice locks in the token format and session model for all downstream slices. Changing providers after S-01 is implemented requires re-plumbing both projects. Decide before planning, not mid-build.
- **Status:** blocked

### F-02: Data layer schema

- **Outcome:** (foundation) Postgres DB deployed and reachable from the .NET backend; schema with `users` and `routes` tables plus migrations; DB client wired and connection-tested; account hard-delete cascade configured (deleting a user row removes all associated route rows).
- **Change ID:** `data-layer-schema`
- **PRD refs:** FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, NFR ("when a user deletes their account, all associated data is permanently deleted")
- **Unlocks:** S-01 (user row created on first sign-in), S-06 (account deletion cascade requires schema in place)
- **Prerequisites:** —
- **Parallel with:** F-01
- **Blockers:** —
- **Unknowns:**
  - Which DB host? Options include Postgres on Azure Database for PostgreSQL Flexible Server, Supabase (managed Postgres + auth SDK), Azure SQL, or SQLite for local-only development first. Choice affects hosting cost, CI/CD DB provisioning, and whether Supabase doubles as the auth provider (collapsing F-01 + F-02). — Owner: user. Block: yes.
- **Risk:** If Supabase is chosen, F-01 and F-02 collapse into a single setup — the auth schema (users table, tokens) is managed by Supabase Auth. Choosing Azure DB for PostgreSQL means implementing token verification and user management independently. Resolving both unknowns (auth provider + DB host) together is the highest-leverage planning decision in this roadmap.
- **Status:** blocked

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

- **Outcome:** user can sign up by entering their email address and receiving a magic link; log in to an existing account via magic link with a clear expiry error message and one-click re-send option; and log out.
- **Change ID:** `magic-link-auth`
- **PRD refs:** FR-001, FR-002, FR-003, US-01
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - What is the magic link expiry window? PRD does not specify a duration. — Owner: user. Block: no (resolvable during planning; 15–30 minutes is a common default).
- **Risk:** Email delivery reliability is a dependency outside the app's control; the PRD explicitly accepts this ("email delivery is a solved infrastructure problem") but deliverability must be verified with the chosen provider's free-tier limits before shipping.
- **Status:** blocked

### S-02: Save route

- **Outcome:** authenticated user can save a generated route to their personal library with one click; the route is auto-named with date + distance (e.g. "2026-07-04 • 42 km"); the user can optionally edit the name and optionally add tags before or after saving.
- **Change ID:** `save-route`
- **PRD refs:** FR-004, FR-005, US-01
- **Prerequisites:** S-01
- **Parallel with:** S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The save action stores the full route geometry (coordinate list) in Postgres. Route geometry payloads can be large for long routes; the schema's data type for the geometry column (JSON array, PostGIS geometry, or encoded polyline) should be decided in F-02 to avoid a costly migration later.
- **Status:** blocked

### S-06: Account deletion

- **Outcome:** authenticated user can permanently delete their account and all associated data (email address + all saved routes) self-serve from account settings, with no support contact required; the deletion is immediate and irreversible.
- **Change ID:** `account-deletion`
- **PRD refs:** FR-003, NFR ("when a user deletes their account, all associated data is permanently deleted; account deletion is self-serve from account settings")
- **Prerequisites:** S-01, F-02
- **Parallel with:** S-02
- **Blockers:** —
- **Unknowns:**
  - Does the chosen auth provider (F-01) support programmatic user deletion via its admin API, or does the app need to manage the users table directly? — Owner: TBD. Block: no (resolvable during planning once auth provider is decided).
- **Risk:** Hard delete with no soft-delete buffer means a mis-click permanently destroys a user's route library. A confirmation prompt (e.g. "type DELETE to confirm") is the minimum safeguard; the PRD requires a prompt but does not specify its form.
- **Status:** blocked

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
- **Status:** blocked

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
| F-01 | `auth-provider-scaffold` | Auth provider scaffold — magic link + session middleware | no | Blocked: auth provider choice (Supabase Auth / NextAuth.js / Resend + JWT / other) must be decided first |
| F-02 | `data-layer-schema` | Data layer — Postgres schema + migrations (users + routes) | no | Blocked: DB host choice must be decided first; consider deciding F-01 + F-02 together if Supabase collapses both |
| S-07 | `routing-quality-osm` | Routing quality — OSM scenic/low-traffic preference + cyclist POI proximity | yes | Run `/10x-plan routing-quality-osm`; no auth/data dependency |
| S-01 | `magic-link-auth` | Magic link auth — signup, login, logout (FR-001–FR-003) | no | Depends on F-01 + F-02 |
| S-02 | `save-route` | Save route to personal library — one-click, auto-name, optional tags (FR-004–FR-005) | no | Depends on S-01 |
| S-06 | `account-deletion` | Account deletion — self-serve hard delete of account + all routes (NFR) | no | Depends on S-01 + F-02; parallel with S-02 once unblocked |
| S-03 | `route-library` | My Routes library — flat list, open saved route on map, GPX download (FR-007–FR-008) | no | North star; depends on S-02 |
| S-04 | `delete-route` | Delete route — confirmation prompt + hard delete (FR-006) | no | Depends on S-02; parallel with S-03 |
| S-05 | `public-route-sharing` | Public route sharing — shareable link, snapshot, no login required (FR-009) | no | Depends on S-02; parallel with S-03 and S-04 |

## Open Roadmap Questions

1. **Which magic link provider?** Options: Supabase Auth, NextAuth.js + email provider (Resend/Postmark/SES), Resend + custom JWT, Azure AD B2C, Lucia. Choice affects both the .NET backend (token verification) and the Next.js frontend (SDK integration). — Owner: user. Block: F-01, S-01, and all downstream library slices.

2. **Which DB + host?** Options: Azure Database for PostgreSQL Flexible Server, Supabase (managed Postgres + auth SDK — potentially collapses Q1 + Q2), Azure SQL, SQLite (dev-only). Choice affects hosting cost, CI/CD provisioning, and auth integration. — Owner: user. Block: F-02, S-01, S-02, S-03, S-04, S-05, S-06.

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

(Empty on first generation. `/10x-archive` appends an entry here when a change whose `Change ID` matches a roadmap item is archived.)
