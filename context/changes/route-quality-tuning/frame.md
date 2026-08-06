# Frame Brief: Route Quality — Spiky Shapes, Arbitrary Roads, Missed POIs

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

Across manual testing of generated loop routes, three symptoms were reported:

1. Roads still felt arbitrary — no perceptible sense that route selection favors
   better cycling roads.
2. Routes did not pass through well-known local cyclist landmarks (named
   examples: "Góra Kawiarnia", "Czterdziesty piąty kilometr" — Warsaw-area
   cycling rest stops).
3. Route shape is visibly "spiky" — sharp, out-of-place detours breaking an
   otherwise loop-shaped route.

User-confirmed priority: **#3 (spiky shape) matters most**, and — critically —
**it is not a regression**: it was already present in v1, before any OSM/Overpass
work existed. `routing-quality-osm` (the OSM scenic/low-traffic + POI-proximity
slice) was fully implemented across 4 phases and then reverted separately, for
unrelated public-Overpass-reliability reasons (see `context/changes/routing-quality-osm/change.md`).

## Initial Framing (preserved)

- **User's stated cause or approach**: none initially — "I wasn't satisfied with
  results with routing after improvement" was the opening statement; the three
  concrete symptoms above were elicited through follow-up questions, not
  self-diagnosed.
- **User's proposed direction**: re-frame and re-research before re-attempting
  any route-quality work; no solution committed.
- **Pre-dispatch narrowing**: spiky shape ranked worst, explicitly confirmed as
  pre-existing in v1, not introduced by the OSM work.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Waypoint-placement heuristic is road-network-blind** — `WaypointCalculator.DestinationPoint`
   is pure haversine bearing/radius math with no awareness of the actual road
   network; `LoopRouteGenerator.FetchCandidatesAsync` forces ORS through
   `[start, wp1, wp2, start]` where wp1/wp2 can land anywhere, including sparse
   or dead-end road areas.
2. **Shape-quality signal is diluted and deprioritized** — `SmoothnessScore`
   (`sharpTurns / (coords.Count - 2)`) statistically dilutes one dramatic local
   spike across hundreds of coordinate points on a long route, and `SelectBestRoute`
   ranks it third, after `pavedRatio` — a spikier-but-more-paved candidate
   routinely wins.
3. **Small, fixed candidate pool (N=3)** — only 3 candidates (120°-wide sectors)
   are ever generated per request; even a perfect scoring function has limited
   ability to avoid a bad waypoint region if all 3 land badly.
4. **DIY waypoint-stitching vs. ORS's own purpose-built round-trip algorithm** —
   ORS Directions API has a native `options.round_trip: {length, points, seed}`
   mode (server-side `Algorithms.ROUND_TRIP`, confirmed against current ORS
   source, not memory) designed for exactly "loop from one start point"; `points`
   is documented as directly controlling circularity/smoothness. This was
   evaluated and explicitly rejected for v1 — not for shape reasons, but for
   distance-precision and repetition-control reasons.
5. **OSM/Overpass scenic + POI layer (separate, orthogonal)** — explains
   symptoms #1 and #2, not #3. Scenic scoring (as designed) only re-ranks 3
   whole candidates post-hoc; it can never bias ORS's turn-by-turn road choice
   within one candidate — so "roads feel arbitrary" is partly an architectural
   ceiling, not just missing/unreliable data. POI-nudging only fires when a
   match falls within one 120°-wide sector and a `[0.5,1.5]×radius` band —
   routing through one specific named landmark is closer to a coin flip than
   an intentional choice. This dimension is already parked (Overpass
   reliability) and out of scope for fixing #3.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| 1. Waypoint placement is road-network-blind, causing forced backtrack near dead-ends | `WaypointCalculator.cs:14-34` (pure trig, no road data); `LoopRouteGenerator.cs:44-53` (`[start, wp1, wp2, start]` forced through-points) | STRONG — mechanically sufficient to produce an out-and-back spike whenever a waypoint lands near a cul-de-sac/sparse area |
| 2. Shape-quality signal (`SmoothnessScore`) too diluted + too low priority to catch it | `SmoothnessCalculator.cs:5-21` (count/total, no locality weighting); `LoopRouteGenerator.cs:74-79` (`OrderByDescending(pavedRatio).ThenByDescending(smoothnessScore)`) | STRONG — direct code evidence of both the dilution and the priority-ordering problem |
| 3. Small candidate pool (N=3) limits recovery even with perfect scoring | `LoopRouteGenerator.cs:8` (`BearingCount = 3`) | WEAK-to-MODERATE — plausible contributing factor, not independently sufficient; not the primary driver |
| 4. ORS native `round_trip` as an alternative generation strategy | ORS server source (`RouteRequestOptions.java`, `RouteRequestRoundTripOptions.java`, `RoutingRequest.java:605` — `Algorithms.ROUND_TRIP`), confirmed live via `gh search code` against `GIScience/openrouteservice`, not training-data memory; **prior project decision** `context/foundation/loop-route-algorithm.md:27` (rejected for v1: "`length` is a preferred value only — deviation can be ±20–30%. Minimal control over repetition... too unpredictable") | STRONG evidence the option exists and targets this exact symptom; MEDIUM confidence it's the right fix — the original rejection reasons (distance precision, repetition control) were about *different* constraints than shape, and the app now already has the post-hoc distance-range filter + `OverlapDetector` overlap-ratio check that could mitigate round_trip's imprecision the same way they gate today's DIY candidates — but this needs empirical re-measurement, not assumption |
| 5. OSM/Overpass layer explains #1/#2, not #3 | `routing-quality-osm/plan.md:189-207` (scenic score is a candidate-level tie-break, never a road-level bias); Phase 2 contract (`plan.md:155-159`, sector-gated + distance-banded POI nudge) | STRONG for #1/#2; NOT APPLICABLE to #3 (shape) — confirms user's own observation that spikes predate and are independent of OSM work |

## Narrowing Signals

- User explicitly ranked spiky shape as the top-priority symptom and explicitly
  stated it is *not* a regression — it existed in v1, before any OSM/Overpass
  code existed. This single fact rules dimension 5 out as an explanation for
  symptom #3 and points squarely at dimensions 1–4 (all pre-existing v1
  machinery).
- `loop-route-algorithm.md:33` ("Route quality depends on waypoint placement
  heuristic — first iteration may need tuning") — the v1 design doc itself
  flagged this exact risk as accepted-but-unresolved at launch (2026-05-30) and
  it was never revisited; `loop-algorithm-tuning` (archived 2026-06-20) added
  `pavedRatio`/`smoothnessScore` and locked thresholds for paved-fraction,
  distance accuracy, and bbox aspect ratio — none of which directly measure
  "one severe local spike on an otherwise smooth loop."

## Cross-System Convention

No test or locked threshold in this codebase currently targets spike/backtrack
detection specifically. The closest existing metrics (`OverlapDetector`'s
aggregate self-overlap ratio, `SmoothnessCalculator`'s count-based sharp-turn
fraction) are both aggregate/statistical measures that dilute a single severe
local defect on a long route — neither was designed to catch "one bad
out-and-back," and `loop-algorithm-tuning`'s locked thresholds (pavedRatio
≥0.90/0.80, overlap ≤0.10/0.40, aspect ≤3.0, distance accuracy ≤15%) don't
close that gap.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: v1's loop-generation architecture
> (hand-rolled waypoint placement forced through 2 blind geometric points, plus
> a shape-quality signal that's too diluted and too low-priority to catch the
> result) is the root cause of "spiky"/arbitrary-feeling routes — a risk the
> original v1 design doc explicitly flagged and never revisited. This is
> independent of, and should be fixed before, any OSM/Overpass scenic or POI
> work resumes (that layer is separately parked for reliability reasons and,
> even working, only explains symptoms #1/#2, not #3).

The original framing ("dissatisfied with routing after the OSM improvement")
undersold the real scope: the OSM layer's reversion is unrelated to the
symptom the user cares most about. The reframe narrows and redirects the
target: fix the base loop-generation shape-quality problem first (dimensions
1–4), and treat OSM/scenic/POI work (dimension 5) as a later, separate
decision — consistent with `roadmap.md` S-07 already being parked.

## Confidence

**HIGH** that the root cause of symptom #3 (spikes) is v1's own
waypoint-placement + shape-scoring machinery, not the OSM work. **MEDIUM** on
which specific fix is correct: re-tuning the existing DIY heuristic/tie-break
vs. switching to ORS's native `round_trip` mode both have real, evidenced
merit and real, evidenced open questions (round_trip's distance/repetition
imprecision was a considered, documented rejection reason for v1 — worth
re-testing empirically now that post-hoc distance/overlap filtering already
exists, rather than assuming the original rejection still holds or assuming
it doesn't).

## What Changes for /10x-plan

Not ready for `/10x-plan` yet — the MEDIUM-confidence fork (tune the DIY
heuristic vs. adopt ORS `round_trip`) should go through `/10x-research` first:
live-measure ORS `round_trip`'s actual distance deviation and repetition
ratio for the 3 existing Polish test locations (mirroring `OrsLiveSmokeTests`),
and confirm current extra_info (surface/waytype) support for `round_trip`
requests on the public hosted API (ORS's own issue history shows this was
broken and fixed upstream — needs re-confirming against the live public
endpoint, not assumed). That research answers the fork; `/10x-plan` then
targets whichever approach the data supports.

OSM/Overpass scenic+POI work (dimension 5) stays out of scope for this
change — it's a separate, already-parked decision (see `roadmap.md` S-07)
and isn't evidenced as necessary to fix the symptom the user prioritized.

## References

- Source files: `src/backend/VeloRoute/Routing/WaypointCalculator.cs:14-34`,
  `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:8,38-56,74-79`,
  `src/backend/VeloRoute/Routing/SmoothnessCalculator.cs:5-21`,
  `src/backend/VeloRoute/Routing/OverlapDetector.cs`
- Prior decision record: `context/foundation/loop-route-algorithm.md` (v1
  round_trip-vs-stitching decision, 2026-05-30)
- Prior tuning work: `context/archive/2026-06-20-loop-algorithm-tuning/plan.md`
  (added pavedRatio/smoothnessScore, locked thresholds — did not address
  spike/backtrack detection)
- Related, superseded frame: `context/changes/loop-direction-hint/frame.md`
  (2026-08-04 — diagnosed a related but distinct symptom, wrong direction
  chosen among 3 candidates, since made moot by `routing-quality-osm`'s
  reversion)
- Parked OSM work: `context/changes/routing-quality-osm/change.md`,
  `context/foundation/roadmap.md` S-07
- External verification: ORS server source confirmed via `gh search code
  --repo GIScience/openrouteservice` (`RouteRequestOptions.java`,
  `RouteRequestRoundTripOptions.java`, `RoutingRequest.java:605`), not
  training-data memory, per this project's own ORS-field-name-mismatch lesson
- Investigation: direct file reads (no sub-agent dispatch needed — codebase
  surface was small and already read firsthand this session) + external
  source verification via `gh` and `WebFetch`
