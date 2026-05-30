---
decision: loop-route-generation-approach
status: decided
created: 2026-05-30
---

# Loop Route Algorithm — Decision

## Decision

**ORS multi-waypoint stitching** is the chosen approach for S-01 (`loop-route-generation`).

## Approach

1. Generate 2–3 intermediate waypoints at `distance / N` from the start point, spread across varied compass bearings (trigonometry from start lat/lon).
2. Chain them via ORS Directions API: `start → wp1 → [wp2] → start` using the `cycling-road` profile.
3. Validate total route distance against the user's min/max km range; retry with adjusted waypoints if outside bounds.
4. Return the first valid result; the ORS response already includes geometry, surface info, and total distance.

**ORS profile:** `cycling-road` with `steepness_difficulty` weighting (fitness level configurable).  
**ORS extra info to request:** `surface`, `waycategory` — to support road-quality filtering in future.

## Why not the alternatives

| Option | Why rejected |
|---|---|
| **ORS `round_trip` built-in** | `length` is a preferred value only — deviation can be ±20–30%. Minimal control over repetition (PRD requires ≤10% repetition). Rejected as too unpredictable for v1. |
| **Itinero (C# library)** | Requires hosting + updating OSM regional `.pbf` files; Itinero 2 is still in early development (19 stars); loop algorithm is fully custom. Too high operational and implementation cost given `top_blocker: skills`. |

## Trade-offs accepted

- 2–3 ORS HTTP calls per user request (vs. 1 for round_trip built-in). Still well within the 5s NFR for typical regional distances.
- Route quality depends on waypoint placement heuristic — first iteration may need tuning. Seed/direction variation can be added as a retry strategy.
- ORS free tier rate limits apply. If this becomes a bottleneck, self-hosted ORS is the upgrade path (same API contract).

## Key constraints from PRD

- Loop must start and end at the same point (FR-004).
- ≤ 10% of route distance may overlap/repeat (Business Logic).
- Result within 5 seconds (NFR).
- No server-side persistence of location inputs (NFR privacy).

## Implementation notes for `/10x-plan loop-route-generation`

- Waypoint generation: place points at `(distance/2) * 0.45` radius from start in 2–4 directions (e.g. N/S or NE/SW/NW). Use haversine for lat/lon offset.
- ORS call: `POST /v2/directions/cycling-road` with `coordinates: [[start], [wp1], [wp2], [start]]`.
- Distance validation: sum segment distances from ORS response; if outside [min_km, max_km], adjust radius and retry (max 3 attempts).
- Repetition check: can be approximated by checking bounding-box overlap of segments; full polyline intersection is a stretch goal.
