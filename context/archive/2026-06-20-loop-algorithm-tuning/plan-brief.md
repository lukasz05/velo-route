# Loop Algorithm Quality Tuning — Plan Brief

> Full plan: `context/changes/loop-algorithm-tuning/plan.md`

## What & Why

ORS already returns surface type data for every route segment, but
`LoopRouteGenerator` ignores it — route selection is purely by distance-to-midpoint.
PRD Business Logic requires deprioritising unpaved segments; S-03 closes that gap
by scoring candidates by paved fraction, exposing it in the UI, and locking
measurable quality thresholds with automated tests.

## Starting Point

`LoopRouteGenerator` makes 3 parallel ORS calls with triangular waypoints (radius
= midpoint × 0.45), picks the in-range + ≤10% overlap candidate closest to the
target distance, and returns it. `RouteWaySegment[]` with surface/waytype data is
built by `OrsMapper` but never used. `RouteInfoPanel` shows distance and a GPX
download button — no surface information.

## Desired End State

Generated routes prefer paved roads when multiple candidates meet distance and
overlap constraints. The API response includes `pavedRatio`; the RouteInfoPanel
shows "X% paved". Waypoint geometry parameters are named constants tuned to
Polish test cities. Four acceptance thresholds (paved ratio, distance accuracy,
compactness, overlap) are locked in automated integration tests with no live ORS
dependency.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| Surface preference mechanism | Score (sort by pavedRatio desc), not hard-exclude | Hard exclude fails in rural areas with few paved roads | Plan |
| PavedRatio exposure | Computed property on `RouteResult` record | No constructor-signature change; zero test breakage | Plan |
| Paved surface definition | Paved, Asphalt, Concrete, Cobblestone, Metal, PavingStones | Road-bikeable; Gravel/Dirt/Ground excluded | Plan |
| Selection sort order | pavedRatio desc → distance-to-midpoint asc | Paved preference is primary; distance accuracy is tiebreaker | Plan |
| Shape compactness | Bbox aspect ratio ≤ 3.0, test-only assertion | Crude but cheap; no runtime cost; convex hull deferred | Plan |
| Test cities | Warsaw outskirts (Białołęka), Mazury, Gdynia | Polish geography matching expected user base; varied terrain | Plan |
| Geometry tuning approach | Extract constants → calibrate live → bake optimal values | Avoids runtime configurability; values settled by data | Plan |
| Frontend scope | "X% paved" line in RouteInfoPanel only | PRD calls for it; minimal UI work; full breakdown deferred | Plan |

## Scope

**In scope:**
- `PavedRatioCalculator` static helper
- `RouteResult.PavedRatio` computed property
- `pavedRatio` in API JSON response + TypeScript type
- "X% paved" in `RouteInfoPanel`
- `LoopRouteGenerator` selection sorted by paved ratio
- `RadiusFactor` and `BearingCount` as named constants; calibration run
- `RouteQualityTests` (5 fake-ORS tests)
- `OrsLiveSmokeTests` (3 live tests, `[Fact(Skip)]`)

**Out of scope:**
- Per-request paved threshold (v2)
- Traffic-volume data (PRD: "optionally")
- Convex-hull compactness in production
- Full road-type breakdown + warnings in UI
- Multiple route proposals (v2)

## Architecture / Approach

Paved ratio flows from existing ORS data through a new static calculator class,
surfaces as a computed property on `RouteResult` (serialised automatically by
System.Text.Json), propagates to the frontend via the unchanged Next.js proxy
route, and is displayed in `RouteInfoPanel`. Selection logic in
`LoopRouteGenerator` changes only the `OrderBy` chain — no new ORS calls, no
new data contracts.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Paved ratio exposed | `pavedRatio` in API + "X% paved" in UI | Empty segments (no ORS extra info) must show "Unknown" not "0%" |
| 2. Selection scoring | Most-paved candidate chosen among valid routes | If all 3 candidates have identical pavedRatio, ordering is unchanged — acceptable |
| 3. Geometry tuning | Named constants; calibrated RadiusFactor + BearingCount | ORS road-snapping may negate geometric improvements in certain geographies |
| 4. Quality tests | 5 fake-ORS tests + 3 live smoke tests locking thresholds | Fake test geometries must be realistic enough to catch real regressions |

**Prerequisites:** `loop-route-generation` done (S-01); `testing-backend-bootstrap`
done (F-02); `route-generation-integration-tests` done (F-03).

**Estimated effort:** ~3 sessions across 4 phases. Phase 3 calibration may require
2–3 live ORS test rounds.

## Open Risks & Assumptions

- ORS snaps waypoints to the nearest road — ideal geometric placement doesn't
  guarantee a loop-shaped route. Radius factor tuning has diminishing returns if
  local road topology forces linear routes.
- Calibration thresholds (0.95 paved / 0.15 distance / 3.0 aspect) are proposed
  values — the Phase 3 calibration run may require adjustments before they're
  reachable.
- `BearingCount = 4` adds one ORS call (total: 4 parallel vs 3). Still within
  the 4.5-second timeout in practice, but should be verified in the live runs.

## Success Criteria (Summary)

- `dotnet test` passes with ≥ 5 new `RouteQualityTests` green; `OrsLiveSmokeTests` skipped
- Live ORS smoke tests pass manually for Białołęka, Mazury, Gdynia (pavedRatio ≥ 0.90)
- RouteInfoPanel shows ≥ 90% paved for a real generated route on paved roads
