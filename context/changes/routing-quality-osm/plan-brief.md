# Routing Quality — OSM Scenic/Low-Traffic + Cyclist POI Proximity — Plan Brief

> Full plan: `context/changes/routing-quality-osm/plan.md`
> Research: `context/changes/routing-quality-osm/research.md`

## What & Why

Route generation should prefer OSM-tagged scenic/low-traffic roads and pass near cyclist POIs (cafes, water points, rest stops), on a best-effort basis, without ever loosening the user's min–max km distance constraint. Today's algorithm uses only ORS road-classification data and has no OSM-tag or POI awareness — routes are valid loops but don't account for what actually makes a road pleasant to cycle.

## Starting Point

`LoopRouteGenerator` fires 3 parallel ORS calls through geometrically-placed waypoints, then picks a winner via a hard distance filter followed by a soft `pavedRatio → smoothness → closeness-to-target` tie-break. No OSM/Overpass integration exists anywhere in the codebase today — this is greenfield. The existing ORS client (options/DI/resilience/error-handling pattern) is the direct template for the new Overpass client.

## Desired End State

A cyclist generating a loop in a well-OSM-tagged area (e.g. Warsaw) gets a route that visibly favors cycleways/low-traffic roads and passes near cafes/water/rest stops, with the same distance guarantee as today. In sparsely-tagged areas, the route is unchanged from v1 (silent, graceful no-op). The API response carries a new `osmEnriched` flag so this fallback behavior is observable in tests and the network tab. The user's entered start/end point is never moved.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Scenic/low-traffic signal source | New Overpass way-tag query, matched to route segments via spatial index | Fits the existing scoring-tie-break pattern exactly; the bbox query is independent of ORS output so it can run in parallel with zero added latency | Plan (user-confirmed) |
| POI proximity mechanism | Waypoint bearing nudging (aim toward nearest in-sector POI, same radius) | Actually steers the route toward POIs per FR-011, not just scoring what ORS already returned | Plan (user-confirmed) |
| Latency architecture | Split by dependency: scenic call parallel with ORS, POI call sequential-before-ORS with its own short timeout | Matches the real data-dependency shape — POI lookup must precede waypoint placement, scenic scoring doesn't | Plan (user-confirmed) |
| Overpass hosting | Public `overpass-api.de`, short timeout, zero retries, its own circuit breaker | Zero infra cost, matches existing config pattern; retries would waste an already-short best-effort budget | Plan (user-confirmed) |
| Caching | None for this slice | Route generation is deliberately DB-free/stateless today; ship simplest version first | Plan (user-confirmed) |
| POI/scenic tag set | Exactly `route-enhancement-ideas.md`'s existing list (cafes+bicycle=yes, water, viewpoints, peaks, nature reserves, beaches; cycleway/designated/lcn·rcn·ncn) | Already vetted in an existing design doc — avoids re-litigating scope or under/over-shooting FR-010/011 | Plan (user-confirmed) |
| Start/end coordinate | Stays pinned exactly to user input | Moving it is a bigger, separate UX/contract decision — parked as a new roadmap idea (Idea #7) instead of folded into this slice | Plan (user, mid-planning) |
| Observability | New `osmEnriched` boolean on the `/routes/loop` response | Makes best-effort/fallback behavior testable end-to-end without any UI change | Plan (user-confirmed) |
| Acceptance bar | Keep the 4 existing live-smoke thresholds unchanged + one new presence-only assertion (`osmEnriched: true` at least once) | Avoids the open-ended-tuning trap that left the prior calibration phase permanently unfinished | Plan (user-confirmed) |
| Test strategy | Fake client (mirrors `FakeOpenRouteServiceClient`) + one skipped live-smoke test | Fits 100% with existing conventions; real wire-format risk accepted and handled via one manual verification step instead | Plan (user-confirmed) |

## Scope

**In scope:**
- New `IOverpassClient`/`OverpassClient` with options, DI, resilience, and a fake test double
- POI-directed bearing nudging in candidate generation (sequential, own timeout, per-sector fallback)
- Scenic/low-traffic way-tag scoring wired into the existing candidate tie-break (parallel, own timeout)
- `osmEnriched` flag on the API response, mirrored into the frontend type
- Composed request timeout budget (ORS + POI-lookup timeouts)
- One presence-only live-smoke assertion; doc sync for the new dependency

**Out of scope:**
- Moving the user's start/end coordinate ("start-point wiggle") — parked as a new roadmap idea
- OSM cycling-route-relation seeding, elevation scoring, iterative nudging, ORS-`RoadClass`-based road-type scoring
- Caching, multi-host Overpass fallback, user-configurable tags/radii
- Any frontend UI surfacing of `osmEnriched` or OSM data
- Persisting OSM data on saved/shared routes

## Architecture / Approach

Two Overpass-backed mechanisms, each hooked in at the point matching its data dependency: POI lookup runs sequentially before ORS (it changes ORS's inputs), scenic-way lookup runs in parallel with ORS (it only needs the start point/radius). Both share one new `OverpassClient` built on the same typed-HTTP-client + `RoutingResult<T>` pattern as the existing ORS client. The distance hard-filter and one-route-per-request behavior are untouched — scenic scoring is one more tie-break field, POI nudging is one more input to waypoint placement, and both degrade silently to today's behavior on failure or absent data.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Overpass client foundation | Typed client, config, DI, resilience, fake test double | Overpass wire-format mismatch, same bug class that bit the original ORS integration |
| 2. POI-directed bearing nudging | Routes steer toward nearby cyclist POIs | Sequential call adds real latency; must degrade cleanly on timeout |
| 3. Scenic/low-traffic way-tag scoring | New tie-break preferring cycleways/low-traffic roads | Spatial-matching bug class (lat/lon correction) that already bit this codebase once |
| 4. `osmEnriched` flag | API-level observability of fallback behavior | None significant — additive, backward-compatible field |
| 5. Testing & doc sync | Locked acceptance bar, updated docs | Re-opening the open-ended-tuning trap if the bar isn't kept minimal |

**Prerequisites:** None — S-07 has no auth/data dependency and can start immediately (roadmap Stream D).
**Estimated effort:** ~5 phases, roughly one focused session per phase given the greenfield-but-well-templated nature of the work.

## Open Risks & Assumptions

- Real-world Overpass latency is unverified until Phase 2's manual measurement — the composed ~6s timeout budget is a design-time estimate, not yet confirmed against production-like conditions.
- OSM tag density is genuinely sparse in some regions (accepted per PRD as an OK no-op), so perceived quality improvement will vary a lot by geography — Warsaw-area manual testing will look better than rural test locations by design, not by bug.
- Public Overpass instance rate limits are unquantified; no caching means repeated nearby requests always hit Overpass fresh — acceptable for now per the No-caching decision, but the first thing to revisit if real usage causes 429s.

## Success Criteria (Summary)

- A loop route generated in a well-OSM-tagged area visibly favors cycleways/low-traffic roads and passes near at least one cyclist POI, with `osmEnriched: true` in the response
- A loop route in a sparsely-tagged area, or with Overpass unreachable, is unchanged from today's behavior (`osmEnriched: false`), with no error and no distance-constraint violation
- All existing quality thresholds (paved ratio, overlap, aspect ratio, distance accuracy) continue to hold on whichever route is selected
