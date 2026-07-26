---
project: "VeloRoute"
version: 2
status: draft
created: 2026-07-04
context_type: brownfield
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: small
timeline_budget:
  delivery_weeks: null
  hard_deadline: null
  after_hours_only: true
---

## Current System Overview

VeloRoute v1 is a free road-cycling loop-route planner. A user enters a starting point (search bar + map confirmation) and a distance range in km; the backend calls OpenRouteService to generate one loop route using a 3-bearing triangular waypoint approach, applying a paved-surface preference and ≤10% segment-overlap constraint. The resulting route is displayed on an interactive MapLibre map with total distance shown; the user can download it as a GPX file. No authentication, no persistence, no user accounts. Fully stateless by design.

Tech stack: Next.js 15 / React 19 / TypeScript (deployed on Azure Static Web Apps) + ASP.NET Core .NET 10 minimal API (deployed on Azure App Service) + OpenRouteService HTTP API. GitHub Actions CI/CD on both. 43 backend unit + integration tests.

Users today: individual road cyclists, anonymous, no account required.

## Problem Statement & Motivation

Two gaps drive v2:

1. **No persistence.** Routes generated in v1 vanish when the session ends. Cyclists who want to repeat a favourite route or compare options must regenerate from scratch or manage GPX files manually. The missing capability is a personal route library tied to an account.

2. **Route quality.** The current algorithm uses ORS road classification and surface data but does not account for cyclist-relevant OSM attributes: scenic or low-traffic road tags, or proximity to POIs (cafes, water points, rest stops). Routes are valid loops but may not feel like good cycling roads.

v2 adds: user accounts with a personal route library (save, name/tag, delete, share publicly) and an improved routing algorithm that draws on OSM scenic/low-traffic tags and routes near cyclist POIs — using only free, publicly accessible data.

Pain category: persistence gap (routes lost) + workflow friction (route quality requires manual curation).

## User & Persona

**Primary persona (unchanged):** Individual road cyclist, solo, planning a ride shortly. Still reaches the product anonymously for a one-off route — anonymous generation is preserved in v2.

**Extended persona (new in v2):** The returning cyclist who has built a collection of routes over several rides and wants to manage, name, and revisit them without maintaining a folder of GPX files. They also want to share a specific loop with a friend without exporting a file.

**Must preserve:** Anonymous route generation (no forced signup), GPX export compatibility (existing v1-generated files remain valid), and public sharing links once introduced must remain stable.

## Success Criteria

### Primary

A user can create an account via magic link, generate a loop route (start point + km range), save it to their personal library, navigate to their library, and download the GPX from there — without the anonymous route generation or GPX export flows breaking for unauthenticated users.

### Secondary

The "My Routes" library page renders within 2 seconds.

### Guardrails

- Anonymous route generation must continue to work in v2 without login.
- GPX export from the map page must remain accessible without authentication.

## User Stories

### US-01: User creates account, saves route, and downloads GPX from library

- **Given** an unauthenticated user on the VeloRoute route planner page
- **When** they sign up via magic link, generate a loop route, click "Save", navigate to "My Routes", open the saved route, and click "Download GPX"
- **Then** they see the route displayed on an interactive map with its auto-generated name and can download the GPX file — and unauthenticated users on the same site can still generate routes and download GPX without logging in

#### Acceptance Criteria

- Magic link signup and login work end-to-end
- Generated route can be saved with one click; auto-name (date + distance) is applied
- "My Routes" library shows the saved route in a flat list
- Opening a saved route shows the interactive map view and a GPX download button
- GPX downloaded from the library is importable to Strava, Garmin, and Komoot without modification
- Anonymous route generation and GPX export continue to work without login

## Scope of Change

### Authentication (new)

- [new] User can sign up by entering an email address and receiving a magic link. Priority: must-have.
  > Socrates: Counter-argument considered: "email delivery failure leaves user unable to create account." Resolution: magic link only; email delivery is a solved infrastructure problem. No fallback auth in v2.

- [new] User can log in to an existing account via magic link; a stale or expired link shows a clear error with a one-click re-send option. Priority: must-have.
  > Socrates: Counter-argument considered: "stale link produces confusing error with no recovery." Resolution: updated to require clear expiry messaging and re-request flow as part of the capability.

- [new] Authenticated user can log out. Priority: must-have.
  > Socrates: Counter-argument considered: "short-lived sessions make explicit logout unnecessary." Resolution: kept; users on shared devices need it. Minimal implementation cost.

### Route library (new)

- [new] Authenticated user can save a generated route to their personal library (manual action; not automatic). Priority: must-have.
  > Socrates: Counter-argument considered: "auto-save avoids user forgetting to save." Resolution: manual save only; avoids library pollution from test or unwanted generations.

- [new] Saved routes receive an auto-generated name (date + distance, e.g. "2026-07-04 • 42 km") with optional user-editable name and optional tags. Priority: must-have.
  > Socrates: Counter-argument considered: "requiring a name adds friction; most users skip naming." Resolution: auto-name by default (date + distance); user can override. Tags remain optional.

- [new] Authenticated user can delete a saved route from their library after confirming a prompt (hard delete, no recovery). Priority: must-have.
  > Socrates: Counter-argument considered: "mis-tap loses a route permanently without soft-delete." Resolution: hard delete with a confirmation prompt is sufficient; soft-delete adds complexity not justified in v2.

- [new] Authenticated user can view their route library as a flat list sorted by date, with no search or filter. Priority: must-have.
  > Socrates: Counter-argument considered: "flat list breaks down past ~10 routes." Resolution: kept; search and filter deferred to v3. Acceptable for v2 volume.

- [new] Authenticated user can open a saved route to view it on an interactive map and download its GPX. Priority: must-have.
  > Socrates: Counter-argument considered: "users may just want GPX, not a map view." Resolution: kept map view; user may want to inspect before downloading. Same experience as the generation flow.

- [new] Authenticated user can share a saved route as a public link (viewable without login); the link reads the live saved route, not a re-generation — it always reflects the route's current name/tags/geometry as stored in the owner's library. Priority: must-have.
  > Socrates: Counter-argument considered: "live re-generation from same inputs may show a different route if the algorithm changes." Resolution: the link reads the persisted `Routes` row directly (not a re-generation from the original start point + km range), so algorithm changes never affect it. It is not a frozen snapshot, however: it is tied to the route row's lifetime.
  > Socrates: Counter-argument considered (2026-07-26, during S-05 planning): "should the link keep working if the owner later deletes the route from their library?" Resolution: **no** — a share is implemented as a lookup keyed to the live `Routes` row (no independent copy), so deleting the source route also removes the share; the link 404s thereafter. This narrows the stability guarantee below (see Constraints) but was chosen deliberately to avoid data duplication; revisit if user feedback shows this surprises recipients.
- [new] Authenticated user can revoke ("stop sharing") a previously shared route; the link 404s immediately, and re-sharing later issues a brand-new token (the old URL never comes back). Priority: must-have.
  > Socrates: Counter-argument considered (2026-07-26, during S-05 planning): "hard delete on revoke discards the old token, so a re-share can't restore the exact same link a recipient may have bookmarked." Resolution: accepted — hard delete matches the codebase's existing no-soft-delete philosophy (see delete-route, S-04); no requirement calls for preserving the exact same URL across a revoke/re-share cycle.

### Routing quality (modified)

- [modified] Route generation now prefers roads tagged as scenic or low-traffic in OSM, on a best-effort basis (graceful fallback where OSM tags are absent). Was: no scenic/low-traffic preference applied. Priority: must-have.
  > Socrates: Counter-argument considered: "OSM scenic tags are sparse — preference is a no-op in many regions." Resolution: best-effort preference accepted; algorithm falls back gracefully. Improvement is real where data exists.

- [modified] Route generation now routes near cyclist POIs (cafes, water points, rest stops) from OSM, on a best-effort basis; distance constraint takes priority and POIs are included only when reachable within the user's min–max km range. Was: no POI proximity routing. Priority: must-have.
  > Socrates: Counter-argument considered: "POI routing may push route outside the requested distance range." Resolution: distance constraint wins; POIs are best-effort and never override the km bounds.

### Non-functional (new)

- [new] When a user deletes their account, all associated data (email address, saved routes) is permanently deleted. No third-party data sharing.
- [new] Account deletion is self-serve from account settings — no support contact required.
- [new] The "My Routes" library page renders within 2 seconds.

### Preserved

- [preserved] Anonymous user can generate a loop route without creating an account; no app-level rate limiting in v2 (ORS API limits act as the natural ceiling).
  > Socrates: Counter-argument considered: "unlimited anonymous generation is abusable." Resolution: ORS free-tier rate limits are the de-facto ceiling. App-level throttling deferred.

- [preserved] Anonymous user can download a GPX file without an account.
  > Socrates: Counter-argument considered: "no account means no contact point if GPX format breaks." Resolution: GPX 1.1 is a mature standard; format breakage is a deploy-time catch, not a per-user issue.

## Constraints & Compatibility

- Anonymous route generation must continue to work without an account in v2.
- GPX export format must remain unchanged — v1-generated GPX files must still be importable to Strava, Garmin, and Komoot.
- Public route sharing links introduced in v2 remain valid while the source route exists and the owner has not revoked the share; they are unaffected by edits to the route's name/tags. **Amended 2026-07-26:** the link is tied to the source route's lifetime, so deleting the route (S-04) also removes the share and the link 404s thereafter — links are not independent snapshots. **Amended 2026-07-26 (later same session):** links are also owner-revocable ("stop sharing"); a revoked link 404s immediately and re-sharing mints a new token, not the same URL.
- Strava Segments API is explicitly excluded — requires OAuth and is not free/public. OSM is the only data source for routing improvements.
- Routing improvement is OSM-only: scenic/low-traffic road tags + cyclist POIs (cafes, water, rest stops) from OpenStreetMap.
- The app is usable on the latest two major versions of Chrome, Firefox, Safari, and Edge. *(preserved from v1)*
- The app renders correctly and is fully usable on small-screen mobile devices. *(preserved from v1)*

## Business Logic Changes

The existing v1 domain rule is modified (not replaced):

**Current rule (v1):** VeloRoute decides which road-network segments form a loop route a road bicycle can ride comfortably, within the user's distance range — on paved or low-traffic roads, with at most 10% segment repetition.

**v2 modification:** The same constraints apply. v2 adds a preference layer: segments that are scenic or popular among cyclists (per OSM tags) are preferred; the route passes near cyclist POIs (cafes, water points, rest stops from OSM) where possible without violating the distance constraint. Both preferences are best-effort — the rule falls back gracefully where OSM data is absent.

The user encounters the rule's output as: (a) a route on an interactive map, (b) a downloadable GPX file, and (c) a route that visibly favours cycling-friendly roads over the v1 baseline.

## Access Control Changes

v1: no authentication. All routes generated anonymously. Anonymous generation is preserved in v2.

v2 adds:

- Magic link (passwordless email): user enters email address, receives a time-limited login link. No password stored.
- Self-serve signup: providing an email address is the full signup flow. No admin approval.
- Flat user model: all authenticated users have identical capabilities (save, name/tag, delete, share routes). No admin role in v2.
- Unauthenticated users retain full access to route generation and GPX export. Authentication is required only to save routes to the library and to share routes publicly.

## Non-Goals

- **No multiple route proposals per request.** Still one route generated per request. Multiple proposals deferred to v3 once the algorithm is proven at scale.
- **No library search or filter.** The "My Routes" library is a flat list sorted by date. Search and filter deferred to v3.
- **No social feed, community features, or public route discovery.** No browsing other users' routes, no following, no community feed. Route sharing is link-only; discovery is out of scope.
- **No point-to-point routes.** Loop routes only (start = end). Point-to-point deferred.
- **No imperial units.** Kilometres only. Miles support deferred.
- **No offline-first or PWA capability.** App requires a network connection.

## Open Questions

1. **Route generation latency for v2.** The v1 ≤5 s NFR was not re-confirmed after adding OSM POI querying. Owner: engineering. Block: no — measure during implementation; define before shipping v2.
2. **`delivery_weeks` not specified.** Timeline is open-ended (after-hours work, no hard deadline). No blocker — an integer estimate would complete the PRD frontmatter. Owner: user. Block: no.
