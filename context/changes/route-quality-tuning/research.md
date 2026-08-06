---
date: 2026-08-05T20:00:36Z
researcher: Łukasz Orawiec (with Claude Sonnet 5)
git_commit: 8c610d98ad73b7997d56f26bbfd3bb7a36f19d4d
branch: route-quality-tuning
repository: lukasz05/velo-route
topic: "Base loop-generation shape quality: ORS round_trip vs. tuning the current waypoint-stitching approach"
tags: [research, codebase, loop-route-generation, ors, round-trip, overlap-detector, smoothness-calculator]
status: complete
last_updated: 2026-08-05
last_updated_by: Łukasz Orawiec (with Claude Sonnet 5)
---

# Research: Base loop-generation shape quality — ORS round_trip vs. tuning the current approach

**Date**: 2026-08-05T20:00:36Z
**Researcher**: Łukasz Orawiec (with Claude Sonnet 5)
**Git Commit**: 8c610d98ad73b7997d56f26bbfd3bb7a36f19d4d
**Branch**: route-quality-tuning
**Repository**: lukasz05/velo-route

## Research Question

`context/changes/route-quality-tuning/frame.md` concluded the root cause of
"spiky"/arbitrary-feeling routes is v1's own waypoint-placement + shape-scoring
machinery (independent of the separately-parked OSM/Overpass work), but left a
MEDIUM-confidence fork open: tune the existing DIY waypoint-stitching heuristic,
or switch to ORS's native `round_trip` mode (previously evaluated and rejected
for v1 on different grounds — distance precision, repetition control). This
research live-measures ORS `round_trip`'s actual behavior and assesses the
code-level feasibility of both paths, to resolve that fork before `/10x-plan`.

## Summary

Live testing against the real ORS API (23 calls: 13 `round_trip`, 10 DIY
candidates, across the 3 existing Polish test locations) found:

- **`round_trip` overshoots distance on every single sample** (13/13, zero
  undershoots), from +20.6% to +106.0%, growing worse with higher `points`
  values — far beyond the ±20-30% the ORS docs and this project's own
  `loop-route-algorithm.md` decision record describe. Fixed-ratio
  pre-compensation narrows but does not tame the variance.
- **`round_trip`'s overlap ratio is dramatically better than the current DIY
  approach**: 0.001-0.039 vs. DIY's 0.028-0.384 across the same locations/distances.
  `round_trip` also confirmed to return `extra_info` (surface/waytype) correctly
  on the public hosted API — an upstream ORS bug (issues #1976/#1529) that
  is fixed in the currently-deployed version.
- **The current DIY approach's candidates fail the primary overlap threshold
  (≤10%) far more often than assumed**: only 2 of 10 live-tested candidates
  cleared it; most land 14-38%. One test location (Gdynia, coastal) had 2 of 3
  candidate sectors fail outright with ORS 404 ("no routable point") because
  blind bearing math pointed those waypoints out over water.
- **New finding, not anticipated by the frame**: because most real requests
  therefore hit `SelectBestRoute`'s *fallback* path, and that fallback path
  orders candidates by `overlapRatio` alone — completely ignoring `pavedRatio`
  and `smoothnessScore` — the "prefers most paved, then smoothest" selection
  logic the app advertises is largely inactive in practice. This is a more
  direct, more severe explanation for "roads feel arbitrary" than the
  tie-break-*priority* framing in `frame.md` alone.
- **The 0.40 fallback overlap threshold is not enforced anywhere in production
  code.** It exists only as a test assertion value and inside a log-warning
  message string (`LoopRouteGenerator.cs:92-93`). The fallback path has no
  overlap ceiling at all — worst case, it will still return whatever candidate
  has the *least-bad* overlap, however bad that is.
- **`RadiusFactor`/`BearingCount` were explicitly never calibrated** —
  `context/archive/2026-06-20-loop-algorithm-tuning/calibration.md:7-10`
  states this was deferred at the time; only the downstream `pavedRatio`/
  overlap *thresholds* were reactively tuned around that un-calibrated geometry.
- Codebase integration for either path is low-risk: `RouteResult`/`OrsMapper`
  response mapping is already generation-strategy-agnostic (no assumption
  tying segment/geometry shape to waypoint count), and `SelectBestRoute`
  consumes `RouteResult` values regardless of how they were produced. Existing
  tests use a queue-based fake (`FakeOpenRouteServiceClient`) that doesn't
  inspect outbound request shape, so neither path breaks existing tests
  structurally — but no current test locks in *what* gets sent to ORS at the
  JSON level for either approach, so new coverage would be needed either way.

## Detailed Findings

### Live ORS `round_trip` behavior

23 live calls against `https://api.openrouteservice.org/v2/directions/cycling-road/geojson`
(key from `dotnet user-secrets`), 3 Polish locations (Warsaw/Białołęka 21.05,52.33;
Mazury/Olsztyn 20.49,53.78; Gdynia 18.53,54.52), at 25km and 90km targets.

| Test | Requested | Actual | Deviation | Coords | Smoothness | Sharp turns | Overlap |
|---|---|---|---|---|---|---|---|
| Warsaw 25k, pts=5, seed=1 | 25000 | 30146 | +20.6% | 1015 | 0.9793 | 21 | 0.024 |
| Warsaw 25k, pts=5, seed=42 | 25000 | 39630 | +58.5% | 1307 | 0.9785 | 28 | 0.019 |
| Warsaw 25k, pts=5, seed=999 | 25000 | 34997 | +40.0% | 981 | 0.9837 | 16 | 0.003 |
| Warsaw 25k, pts=3, seed=42 | 25000 | 34231 | +36.9% | 944 | 0.9788 | 20 | 0.011 |
| Warsaw 25k, pts=10, seed=42 | 25000 | 51512 | +106.0% | 1514 | 0.9630 | 56 | 0.019 |
| Warsaw 90k, pts=5, seed=42 | 90000 | 123004 | +36.7% | 2973 | 0.9899 | 30 | 0.003 |
| Mazury 90k, pts=5, seed=42 | 90000 | 117404 | +30.4% | 2196 | 0.9918 | 18 | 0.014 |
| Gdynia 90k, pts=5, seed=42 | 90000 | 115402 | +28.2% | 2655 | 0.9876 | 33 | 0.003 |
| Warsaw 90k, pts=8, seed=42 | 90000 | 162612 | +80.7% | 3315 | 0.9840 | 53 | 0.012 |
| Warsaw 90k, pts=15, seed=42 | 90000 | 151415 | +68.2% | 3282 | 0.9802 | 65 | 0.009 |
| Mazury 25k, pts=5, seed=7 | 25000 | 40262 | +61.0% | 919 | 0.9716 | 26 | 0.026 |
| Mazury 25k, pts=5, seed=123 | 25000 | 35301 | +41.2% | 784 | 0.9808 | 15 | 0.001 |
| Gdynia 25k, pts=5, seed=7 | 25000 | 36721 | +46.9% | 969 | 0.9783 | 21 | 0.039 |

- **Distance**: 13/13 samples overshoot, mean ≈ +50%, worst +106%. Increasing
  `points` (3→5→10 at Warsaw 25k) makes deviation *worse* (+36.9% → +58.5% →
  +106.0%), contradicting the ORS docs' claim that higher `points` "creates
  more circular routes" — at least for these test locations, higher `points`
  correlated with both worse distance accuracy and worse smoothness (sharp
  turns 20→28→56 over the same 3-value sweep).
- **Fixed-ratio pre-compensation tested**: requesting `length=16250` (0.65× of
  a 25000 target) at Warsaw across 3 seeds returned 21222 (-15.1% vs. the
  *original* 25000 target), 27808 (+11.2%), 35909 (+43.6%) — narrows the worst
  case but the spread relative to target remains wide (~59 percentage points
  across 3 seeds), and can now undershoot. A fixed compensation factor alone
  would not reliably land inside a typical ±15% distance-accuracy band.
- **Smoothness**: consistently high in absolute terms (0.963-0.992) — as good
  as or better than DIY's 0.967-0.992 range (see below).
- **Overlap**: 0.001-0.039 across all 13 samples — an order of magnitude
  better than DIY's observed range.
- **`extra_info`**: `surface` and `waytype` both present in every response
  (`extras` keys: `['waytype', 'surface']`) — confirms the upstream ORS fix
  for "no extra_info on round_trip" (GitHub issues #1976, closed 2025-04-08;
  #1529, closed 2024-01-09) is live on the public hosted API used by this app.
- **Failure mode observed**: one `round_trip` call (Gdynia, seed=123) returned
  HTTP 500 "Could not find a valid point after 3 tries" — an occasional,
  seed-dependent failure, distinct from DIY's *structural* per-sector failure
  mode (below).

### Live DIY (current app method) baseline

10 valid candidates + 2 outright failures, same locations/distances, computed
via the exact algorithm in `WaypointCalculator.cs`/`LoopRouteGenerator.cs`
(`RadiusFactor=0.45`, sectors at 60°/180°/300°, `steepness_difficulty=1`,
`avoid_features=[steps,ferries]`).

| Test | Sector | Distance | Coords | Smoothness | Sharp turns | Overlap |
|---|---|---|---|---|---|---|
| Warsaw 25k | 60° | 27920 | 761 | 0.9895 | 8 | 0.089 |
| Warsaw 25k | 180° | 29724 | 804 | 0.9900 | 8 | 0.180 |
| Warsaw 25k | 300° | 35012 | 1010 | 0.9911 | 9 | 0.384 |
| Warsaw 90k | 60° | 99894 | 2523 | 0.9921 | 20 | 0.139 |
| Warsaw 90k | 180° | 98108 | 2361 | 0.9873 | 30 | 0.146 |
| Warsaw 90k | 300° | 107176 | 2175 | 0.9899 | 22 | 0.028 |
| Mazury 25k | 60° | 27526 | 718 | 0.9735 | 19 | 0.282 |
| Mazury 25k | 180° | 27224 | 731 | 0.9698 | 22 | 0.198 |
| Mazury 25k | 300° | 29912 | 772 | 0.9792 | 16 | 0.375 |
| Gdynia 25k | 60° | — | — | — | — | **HTTP 404** — no routable point within 350m |
| Gdynia 25k | 180° | 27457 | 703 | 0.9672 | 23 | 0.209 |
| Gdynia 25k | 300° | — | — | — | — | **HTTP 404** — no routable point within 350m |

- **Overlap**: only 2 of 10 valid candidates (0.089, 0.028) clear the
  `PrimaryOverlapThreshold = 0.10` (`LoopRouteGenerator.cs:9`). The rest range
  14.6%-38.4% — well into "most requests hit the fallback path" territory.
- **Gdynia structural failure**: 2 of 3 fixed sectors (60°, 300°) pointed the
  computed waypoint out over water with no nearby routable road — `WaypointCalculator`'s
  pure trigonometry has no awareness of the coastline or road network, so this
  is a deterministic, repeatable failure for any coastal/edge-of-network start
  point at those bearings, not a transient issue.
- **Distance accuracy**: tighter than `round_trip` in absolute terms — 25km
  targets landed 27224-35012 (+8.9% to +40.0%), 90km targets landed
  98108-107176 (+9.0% to +19.1%) — but still not uniformly within a ±15% band
  either; `SelectBestRoute`'s hard `[min,max]` filter is what actually
  enforces the user-facing constraint today, for both approaches.

### `SelectBestRoute` fallback path ignores paved/smoothness signals

`LoopRouteGenerator.cs:74-79` (primary path, requires `overlapRatio ≤ 0.10`):
```csharp
.OrderByDescending(c => c.pavedRatio)
.ThenByDescending(c => c.smoothnessScore)
.ThenBy(c => Math.Abs(c.distance - targetMidMeters))
```

`LoopRouteGenerator.cs:84-88` (fallback path, hit whenever no candidate clears
0.10 — per the live data above, most real requests):
```csharp
.OrderBy(c => c.overlapRatio)
.ThenBy(c => Math.Abs(c.distance - targetMidMeters))
```

The fallback path never considers `pavedRatio` or `smoothnessScore`. Per the
DIY-tuning agent's findings, this was a **known, deliberate deferral** in the
original `loop-algorithm-tuning` plan (`context/archive/2026-06-20-loop-algorithm-tuning/plan.md:204,346`:
"fallback path... is unchanged — it already orders by overlap"), not an
oversight — but it was never revisited after that plan shipped, and the live
overlap-failure-rate data now shows this path is hit far more often than the
"fallback" name implies.

### The 0.40 threshold is not a real ceiling

`LoopRouteGenerator.cs:92-93` only *logs a warning* when the selected
fallback candidate's overlap exceeds `PrimaryOverlapThreshold` (0.10) — there
is no second constant, no `.Where()` filter, and no rejection logic tied to
0.40 anywhere in `LoopRouteGenerator.cs`. The 0.40 figure exists only as:
- An integration-test assertion value (`context/archive/2026-06-20-loop-algorithm-tuning/plan.md:41`,
  `calibration.md:22`).
- Text inside the log-warning message at `LoopRouteGenerator.cs:92-93`.

In production, if all 3 (or N) candidates have high overlap, the fallback
path returns whichever is *least bad*, with no upper bound — a candidate with,
hypothetically, 60-80% overlap would still be returned as long as it's the
best of a bad set.

### Threshold/geometry calibration history

`context/archive/2026-06-20-loop-algorithm-tuning/calibration.md:17-23,32,37`
confirms the 0.10/0.40 pair was reactively set after a live Gdynia run showed
24.1% overlap ("which drove threshold change to 0.40"). But:
- Calibration covered only 3 cities, a single 20-30km range, one seed each —
  much narrower than this research's 25km/90km × 3-city × multi-seed sample.
- `calibration.md:7-10` explicitly states `RadiusFactor`/`BearingCount`
  calibration was **deferred**: "Unchanged from baseline — calibration
  deferred." The thresholds were tuned around geometry constants that were
  never themselves tuned.

### Codebase integration feasibility — round_trip

(Full findings from the codebase-integration agent; file:line citations as
reported.)

- `OrsDirectionOptions.cs:3-5` and the file-scoped `OrsOptions` DTO
  (`OpenRouteServiceClient.cs:136-151`) have no `RoundTrip` field today, but
  adding one is mechanically trivial — no structural blocker.
- The real friction point is `OrsDirectionsRequest.Coordinates`
  (`OpenRouteServiceClient.cs:120-134`, `required double[][]`, populated from
  `waypoints.Select(...)` at line 46) — round_trip wants exactly one
  `[lon,lat]` coordinate, which is shape-compatible (`double[][]` of length 1)
  but not self-documenting via the current `GetDirectionsAsync(IReadOnlyList<RouteCoordinate> waypoints, ...)`
  signature; a clearer API would add an explicit mode/overload rather than
  overload "waypoints" to sometimes mean "just the start."
- `MapToRouteResult` (`OpenRouteServiceClient.cs:101-115`) and
  `OrsMapper.BuildSegments` (`OrsMapper.cs:11-43`) read only the GeoJSON
  feature's `geometry`/`properties.summary`/`properties.extras` — confirmed
  **no hidden assumption** ties segment/geometry shape to input waypoint
  count. Response mapping is already generation-strategy-agnostic.
- `SelectBestRoute` (`LoopRouteGenerator.cs:58-101`) only reads
  `RouteResult`-derived fields (`DistanceMeters`, `Geometry.Coordinates` via
  `OverlapDetector`, `PavedRatio`, `SmoothnessScore`) — nothing inspects how
  a candidate was generated. 3 round_trip calls (varying `seed`) could
  structurally replace the 3 bearing-based calls in `FetchCandidatesAsync`
  (`LoopRouteGenerator.cs:38-56`) and flow through `SelectBestRoute` unchanged.
- Test impact: `LoopRouteGeneratorTests.cs` was deleted in an unrelated
  revert (abandoned OSM feature) and not replaced. `LoopRouteIntegrationTests.cs`
  and `RouteQualityTests.cs` both enqueue exactly 3 `FakeOpenRouteServiceClient`
  results per test and assert on `RouteResult` content, not on request shape
  — they would not *break* under a round_trip switch, but also provide zero
  coverage of what gets sent to ORS. `FakeOpenRouteServiceClient`
  (`TestInfrastructure.cs:72-95`) is a plain `ConcurrentQueue` dequeue that
  ignores its `waypoints`/`options` arguments beyond an artificial delay —
  accommodates either approach with zero changes, but also means **no
  existing test would catch a malformed round_trip request body**; new
  coverage (e.g. an `HttpMessageHandler`-capturing test) would be needed
  either way.

### Codebase integration feasibility — tuning the DIY approach

(Full findings from the DIY-tuning agent; file:line citations as reported.)

- **Locality-aware smoothness metric**: small-to-medium change. All data
  needed (per-index bearings) is already computed in `SmoothnessCalculator.cs:11-18`
  — swapping the aggregation (global count-average → max-consecutive-run or
  length-weighted) is a small change. Reusing `OverlapDetector`'s STRtree
  machinery for a "largest contiguous overlap segment" metric would be a
  moderate addition (new static method, same NTS dependency already present).
- **Fallback-ordering fix**: low risk, close to one-line. Both `pavedRatio`
  and `smoothnessScore` are already computed for every candidate
  (`LoopRouteGenerator.cs:69-70`); adding them as `.ThenByDescending(...)`
  keys after `overlapRatio` in the fallback ordering doesn't interact with
  the log-warning (reads only `overlapRatio`) or with `Program.cs`, which
  only branches on `result.IsSuccess`, not which selection path fired.
- **`BearingCount` increase**: isolated constant, no other code changes
  required. Calls run concurrently (`Task.WhenAll`, `LoopRouteGenerator.cs:55`)
  under one shared 4.5s budget (`OpenRouteServiceOptions.cs:7`,
  `Program.cs:330`) — latency impact of going 3→5-6 is bounded by the
  *slowest single call*, not the sum, but call-volume cost scales linearly
  (67-100% more ORS calls per request) with undocumented rate-limit
  exposure (no ORS concurrency/rate-limit config found anywhere in this
  repo). No client-side throttling exists in `OpenRouteServiceClient.cs`.
- **Graceful degradation on partial candidate failure**: already present.
  `SelectBestRoute` filters `results.Where(r => r.IsSuccess)`
  (`LoopRouteGenerator.cs:61-63`) before scoring — losing 1-2 of N candidates
  (as at Gdynia) degrades gracefully as long as ≥1 succeeds and clears the
  distance window. Total wipeout (all N fail) surfaces only the *first*
  underlying error (`LoopRouteGenerator.cs:98`), discarding the rest — a
  pre-existing minor gap, more likely to matter if `BearingCount` increases.

## Code References

- `src/backend/VeloRoute/Routing/WaypointCalculator.cs:14-34` — pure
  haversine bearing/radius math, no road-network awareness
- `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:7-9` — `RadiusFactor`,
  `BearingCount`, `PrimaryOverlapThreshold` constants
- `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:38-56` — `FetchCandidatesAsync`
- `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:58-101` — `SelectBestRoute`
  (primary path 74-79, fallback path 84-88, warning 92-93, no-result 98-100)
- `src/backend/VeloRoute/Routing/SmoothnessCalculator.cs:5-21` — global
  count-averaged sharp-turn metric
- `src/backend/VeloRoute/Routing/OverlapDetector.cs` — STRtree spatial-proximity
  pattern, reusable for a locality-aware metric
- `src/backend/VeloRoute/Routing/OpenRouteServiceClient.cs:37-115` —
  `GetDirectionsAsync`, `MapToRouteResult`
- `src/backend/VeloRoute/Routing/OpenRouteServiceClient.cs:120-151` —
  `OrsDirectionsRequest`, `OrsOptions` file-scoped DTOs
- `src/backend/VeloRoute/Routing/OrsDirectionOptions.cs:3-5` — public options record
- `src/backend/VeloRoute/Routing/OrsMapper.cs:11-43` — `BuildSegments`
- `src/backend/VeloRoute/Routing/OpenRouteServiceOptions.cs:7` — `TimeoutSeconds`
- `src/backend/VeloRoute/Program.cs:330` — request timeout construction
- `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:72-95` —
  `FakeOpenRouteServiceClient`
- `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs` — 3-candidate
  assumption in test setup (e.g. lines 38-40, 57-59, 75-77, 92-94)
- `src/backend/VeloRoute.Tests/Routing/RouteQualityTests.cs` — same pattern
  (e.g. lines 60-63, 172-174, 195-198)

## Architecture Insights

- `RouteResult`/`OrsMapper`/`SelectBestRoute` are already fully decoupled from
  *how* a candidate's geometry was produced — this is a genuine strength that
  makes either fork (tune DIY, adopt round_trip, or run both as parallel
  candidate sources) equally easy to wire into the existing pipeline.
- The "primary path / fallback path" split in `SelectBestRoute` is doing more
  work than its name suggests: live data shows the *fallback* is the common
  case, not primary — a naming/design mismatch worth resolving regardless of
  which generation strategy is chosen, since fallback quietly drops the app's
  main advertised preference (paved roads).
- The project's calibration history has a recurring pattern: thresholds get
  reactively tuned around live data, while the geometry constants generating
  that data are explicitly deferred ("`RadiusFactor`/`BearingCount`...
  calibration deferred," `calibration.md:7-10`). This research's live data is
  the first time those deferred constants themselves were empirically
  exercised at scale (25km + 90km, 3 cities, multiple candidates each).

## Historical Context (from prior changes)

- `context/foundation/loop-route-algorithm.md` (2026-05-30, v1 decision) —
  rejected `round_trip` for v1 on distance-precision (±20-30% documented,
  now confirmed empirically as often worse) and repetition-control grounds.
  Flagged "Route quality depends on waypoint placement heuristic — first
  iteration may need tuning" as an accepted, unresolved risk — this research
  is the first revisit of that flag, 14+ months later.
- `context/archive/2026-06-20-loop-algorithm-tuning/` — added `pavedRatio`/
  `smoothnessScore`, locked the 0.10/0.40 overlap thresholds and other
  quality thresholds, but explicitly deferred `RadiusFactor`/`BearingCount`
  calibration and left the fallback-path ordering gap undisturbed
  (`plan.md:204,346`).
- `context/changes/route-quality-tuning/frame.md` (2026-08-05) — the framing
  step that produced this research question; identified the DIY-vs-round_trip
  fork as MEDIUM confidence pending this live data.
- `context/changes/loop-direction-hint/frame.md` (2026-08-04) — a related,
  since-superseded frame that diagnosed a different symptom (wrong direction
  chosen among 3 candidates) via the primary-path tie-break; concluded
  `routing-quality-osm` Phase 3 (scenic scoring, since reverted) was the
  fix — moot now that Phase 3 no longer exists, but its dimension-map
  reasoning about `SelectBestRoute`'s primary path remains accurate.
- `context/changes/routing-quality-osm/` — the separately-parked OSM/Overpass
  scenic+POI slice; unrelated to this research's shape-quality question per
  the frame's own conclusion.
- `context/foundation/route-enhancement-ideas.md` idea #8 (2026-08-05) —
  post-hoc POI detour insertion, captured and deliberately deferred out of
  this change's scope during framing.

## Related Research

- `context/changes/route-quality-tuning/frame.md`
- `context/changes/loop-direction-hint/frame.md`
- `context/changes/routing-quality-osm/research.md`, `plan.md` (parked OSM work)

## Open Questions

1. **Combined-candidate option, not yet evaluated live**: rather than a strict
   either/or, the data suggests running a mix (e.g. 2 `round_trip` seeds + 1-2
   DIY sectors) through a *fixed* `SelectBestRoute` (fallback respecting
   paved/smoothness, plus a real overlap ceiling) could capture round_trip's
   much lower overlap while keeping DIY's tighter distance control as a
   fallback source. Not measured here — would need its own live test once a
   direction is chosen.
2. **round_trip retry-until-in-range cost is unmeasured**: given round_trip's
   wide distance variance, a retry loop (different seeds) to land inside
   `[min,max]` km would need real measurement of how many attempts are
   typically required, and whether that fits the 4.5s budget — not tested
   here (would require sequential retries against live ORS, which risks
   rate-limiting if run broadly).
3. **ORS rate limits under increased call volume** (relevant to both a
   `BearingCount` increase and a round_trip-retry strategy) are not
   documented anywhere in this repo and weren't probed in this research
   (would require sustained load testing, out of scope for a single research
   pass).
4. **Whether round_trip's distance/smoothness relationship holds at the
   20-30km range used by the live-smoke test suite** — this research
   concentrated samples at 25km and 90km; the existing `OrsLiveSmokeTests`
   locations use 20-30km ranges specifically, which is covered, but denser
   sampling across the full v1 min/max spectrum (the PRD doesn't fix a single
   range) wasn't attempted.
