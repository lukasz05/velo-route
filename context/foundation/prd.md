---
project: "VeloRoute"
version: 1
status: draft
created: 2026-05-20
context_type: greenfield
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: low
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

## Vision & Problem Statement

A road cyclist who wants to go for a ride often has no ready-made route in mind. Planning one manually — searching forums, piecing together maps, or adapting others' routes — costs meaningful time and still carries the risk of disappointment: a route that turns out to be heavily trafficked, poorly surfaced, or simply the wrong length for the day.

Existing tools (Komoot, Strava, Google Maps) exist but either charge for quality route planning or treat loop routes of a specified length as a secondary concern. VeloRoute is entirely free and optimises specifically for this use case: given a starting point and a desired distance range, it generates road-bike-appropriate loop routes — paved, low-traffic — back to the start.

## User & Persona

**Primary persona:** An individual road cyclist, anywhere, who rides solo and wants to plan rides on their own terms. They are not a casual cyclist — they care specifically about surface quality and traffic levels, which rules out generic navigation apps. They reach for this product when they want to go for a ride soon and don't already have a route picked. They want a good option fast, not an infinite feed to browse.

## Success Criteria

### Primary
- A user can enter a start point and a distance range (km), receive at least 1 road-bike loop-route proposal displayed on an interactive map, and download it as a valid GPX file — without creating an account.

### Secondary
- The results page loads and displays the proposal within 5 seconds of submitting the form.

### Guardrails
- The exported GPX file must be importable to Strava, Garmin, and Komoot without modification.
- At most 10% of the route length (configurable) may repeat (no significant out-and-back segments).

## User Stories

### US-01: User generates and exports a cycling route

- **Given** a user on the route planner page who has entered a starting point (loop route — start = end) and set a length range in kilometres
- **When** they trigger route generation
- **Then** they see at least 1 route proposal displayed on an interactive map, with its total length shown, and an option to download it as a GPX file

#### Acceptance Criteria
- At least 1 distinct route proposal is shown
- The proposal's total length (in km) is visible alongside the map
- The proposal can be downloaded as a GPX file
- The exported GPX is importable to Strava, Garmin, and Komoot without modification
- At most 10% of the route length (default; configurable) may repeat within the proposal
- The route is a loop: the end point is the same as the start point

## Functional Requirements

### Route input
- FR-001: User can enter a starting point via a search bar and confirm it on a map. Priority: must-have
  > Socrates: Counter-argument considered: "a full interactive map is over-engineering the input for v1 — a text address is enough." Resolution: revised to search-bar-first with map confirmation; avoids requiring the user to click a map but still shows them where the start point landed.

- FR-002: User can specify a minimum and maximum route length in kilometres. Priority: must-have
  > Socrates: Counter-argument considered: "a km/miles toggle adds a UI element without changing core value." Resolution: km only for v1; miles support added in v2.

### Route generation & display
- FR-003: User can trigger route generation and receive at least 1 loop-route proposal. Priority: must-have
  > Socrates (former FR-002): Counter-argument considered: "a separate end point is rarely used — most road cyclists do loops." Resolution: FR-002 dropped; v1 generates loop routes only (start = end). Point-to-point added in v2.
  > Socrates (former FR-004): Counter-argument considered: "generating 3+ proposals is expensive if the routing API charges per call." Resolution: revised to 1 proposal for v1; multiple proposals added in v2 once the algorithm is proven.

- FR-004: User can view the proposal displayed on an interactive map. Priority: must-have
  > Socrates: Counter-argument considered: "static image or external link is sufficient." Resolution: kept; an interactive map display is essential to the product's value.

- FR-005: User can see the total length of the proposal. Priority: must-have
  > Socrates: Counter-argument considered: "length is redundant given the input range." Resolution: kept; showing the exact generated length is minimal and confirms the proposal is within bounds.

### Export
- FR-006: User can download the GPX file for the proposal. Priority: must-have
  > Socrates: Counter-argument considered: "GPX is niche; a direct import URL to Strava/Komoot would be more useful." Resolution: kept GPX as the core mechanism; it's the most universally compatible format across Strava, Garmin, and Komoot.

## Non-Functional Requirements

- The app is usable on the latest two major versions of Chrome, Firefox, Safari, and Edge.
- The app renders correctly and is fully usable on small-screen mobile devices (smartphones).
- Location inputs submitted during route generation leave no trace in operator-accessible storage after the request that consumed them completes.

## Business Logic

VeloRoute decides which road-network segments form a loop route that a road bicycle can ride comfortably, within the user's specified distance range.

The decision draws on surface type and road classification data drawn from publicly available road-network datasets, and optionally traffic-volume data from available public sources. Segments on unpaved or low-quality surfaces and segments with heavy motor traffic are deprioritised or excluded. The generated route is constrained to loop back to the start point, stay within the user's minimum–maximum length bounds, and keep repetition (retracing of the same segment) to at most 10% of the total route length (default; configurable).

The user encounters the rule's output as: (a) a route displayed on a map, (b) a downloadable GPX file, and (c) a short route summary listing road types used, with warnings for any problematic segments that could not be avoided.

## Access Control

v1 — no authentication required. Route generation and GPX export are available to all users without an account. No sign-up, no login, no session.

Auth is explicitly deferred to v2, where saving routes to a personal catalogue and managing a route library will require an account. At that point: a flat user model applies (all authenticated users have the same capabilities), self-serve sign-up, no admin role.

## Non-Goals

- **No point-to-point routes in v1.** Only loop routes (start = end) are generated. Point-to-point support is deferred to v2. Rationale: simplifies route generation and aligns with the most common road-cyclist use case.
- **No user accounts, saved routes, or route library in v1.** Authentication and persistence are deferred to v2. Rationale: GPX export serves as the persistence mechanism for v1.
- **No miles / imperial units in v1.** Kilometres only. Miles support is deferred to v2. Rationale: avoids a unit-toggle UI element without reducing core value.
- **No multiple proposals in v1.** A single route proposal is generated per request. Multiple-proposal support is deferred to v2 once the algorithm is proven. Rationale: reduces complexity for the initial build.
- **No social or sharing features.** No public route sharing, no community feed, no collaborative planning. These are explicitly out of scope.
- **No offline-first or PWA capability in v1.** The app requires a network connection to generate routes.

## Open Questions

No open questions at this time. All shaping decisions were captured and accepted during the `/10x-shape` session (`quality_check_status: accepted`).
