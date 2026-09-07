# Route Generation Enhancement Ideas

Captured 2026-06-21 during loop-algorithm-tuning.

## 1. Smoothness Metric (turn-rate scoring)

Count bearing changes >90° across coordinate triplets from `RouteGeometry.Coordinates`.
Normalize to [0,1]. Use as tertiary sort after paved ratio.

No new dependencies — ORS coordinates already returned.

---

## 2. OSM Cycling Route Seeding (highest quality ceiling)

Query Overpass API for `type=route, route=bicycle` relations near the start point.
Extract waypoints from matching route segments of target length, close the loop.
Replaces geometric bearing logic entirely for areas with mapped cycling routes.

- API: Overpass (`overpass-api.de` or self-hosted) — free, no auth
- Query: `relation[route=bicycle](around:<radius>,<lat>,<lon>)`
- Fallback: current geometric bearing logic when no OSM routes found nearby

---

## 3. POI-Directed Bearings

Instead of arbitrary bearings, aim waypoints toward OSM POIs attractive to cyclists:
- `tourism=viewpoint`, `natural=peak`, `leisure=nature_reserve`
- `amenity=cafe` + `bicycle=yes`
- `water=lake`, `natural=beach`

Query Overpass for top-N POIs within `radius * 2` of start, use their bearings as
candidates instead of equally-spaced geometric offsets. Scoring: paved ratio +
smoothness + "passes near POIs."

- API: same Overpass endpoint as above
- Adds one Overpass call per request (~100ms, cacheable by area)

---

## 4. Elevation Profile Scoring

ORS already returns elevation data. For road cycling, prefer routes with:
- Low gradient variance (no punchy rollers)
- Total elevation gain proportional to distance (not excessive for the range)

Metric: standard deviation of per-segment gradient, normalized by distance.
Could be user-configurable ("hilly" vs "flat" preference in v2 UI).

---

## 5. Iterative Waypoint Nudging

Generate initial loop → score it → perturb waypoints by ±10–20% → re-query ORS →
keep best result. 2–3 rounds within the 4.5s timeout.

Dramatically increases quality ceiling for any scoring metric without changing the
fundamental generation approach. Most effective when combined with POI-directed
bearings or elevation scoring.

---

## 6. Road Type Scoring

Extend `PavedRatioCalculator` into a broader surface score that weights road class:
`cycleway > residential > unclassified > tertiary > secondary > primary/trunk`.
ORS `waytype` already returned in `RouteWaySegment.RoadClass`.

---

## 7. Start-Point Wiggle

Captured 2026-07-26 during `routing-quality-osm` (S-07) planning. Distinct from #3
(POI-directed bearings), which only re-aims the intermediate waypoints (`wp1`/`wp2`) —
the literal start/end coordinate the user entered stays fixed as both loop endpoints.

Idea: let the actual start/end coordinate used for route generation (and therefore
the GPX start point and map marker) shift within a small bounded radius toward a
nearby higher-quality direction (scenic road, cluster of POIs), instead of being
pinned exactly to the user-entered point.

Deliberately deferred from S-07: changes a user-visible contract (the returned route
no longer necessarily starts exactly where the user clicked/typed), which needs its
own scoping before implementation — open questions: wiggle radius, whether the shifted
point is shown/confirmed to the user before generation, opt-in vs. default-on, and
how it interacts with the min–max km distance constraint (does the wiggle count
against the distance budget?). S-07 keeps the start/end pinned exactly to the input
coordinate; only the road chosen leaving/returning to it may bend (via #3's mechanism).

---

## Priority Order

1. **Smoothness** — zero new dependencies, cheapest to add
2. **OSM cycling route seeding** — highest quality ceiling, clean fallback story
3. **POI-directed bearings** — improves route interest, same Overpass dependency as #2
4. **Elevation scoring** — user-configurable in v2 UI
5. **Iterative nudging** — amplifier, works best on top of #2/#3
6. **Road type scoring** — marginal gain over paved ratio
7. **Start-point wiggle** — needs its own scoping (radius, UX contract) before estimation; not yet a roadmap slice
