# Frame Brief: User-Facing Direction/Destination Hint

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.
>
> **Revision note**: this brief was rewritten after Step 5 pressure-testing
> surfaced a credible alternative the first pass missed (see "Pressure-Test
> Finding" below). The conclusion changed materially — read that section
> before the rest.

## Reported Observation

Loop route generated for a Wilanów (Warsaw) start point, 80-100km distance range,
does not head toward Góra Kalwaria — the user's expected "obvious" direction for
that start point and distance. GPX analysis confirmed the winning candidate's
farthest waypoint sits at bearing 300.66° (NW), not south. This persisted after
`routing-quality-osm` Phase 2's POI-nudge distance-band fix landed and was verified
(via the same GPX) to not be the cause — the observed bearing matches the un-nudged
geometric sector center almost exactly, meaning no POI hijacked it.

Confirmed via an independent, unbiased chain-trace (fresh sub-agent, no prior
framing supplied) plus direct user confirmation: the GPX was produced by a plain
**"Generate"** click, not "Re-roll". On that path `RouteForm.tsx:35-39` sends no
`seed`, so `LoopRouteGenerator.cs:41` sets `baseBearing=0` deterministically —
giving sector centers at exactly **60°/180°/300°**. South (180°, the Góra Kalwaria
direction) *was* one of the three candidates generated this run, not missing.

## Initial Framing (preserved)

- **User's stated cause or approach**: not explicitly stated by the user at first
  report ("obvious choice should be route to Góra Kalwaria but some weird route
  gets generated"). The assistant's mid-investigation hypothesis was that
  `RouteForm.tsx:43`'s per-request `Math.floor(Math.random()*360)` seed randomizes
  which 3 sectors get proposed, so no candidate is guaranteed to face south.
- **User's proposed direction**: none committed initially — user chose "Stop and
  re-plan" over continuing `routing-quality-osm` Phase 2 or patching inline.
- **Pre-dispatch narrowing**: "Not sure / haven't separated them yet" — user had not
  distinguished single-symptom vs. class-of-symptoms framing at the outset.

## Dimension Map

The observation could originate at any of these dimensions (revised after the
unbiased chain-trace agent and the Step 5 pressure-test — see below):

1. **Candidate coverage / random seed rotation** — applies only to the **Re-roll**
   path (`RouteForm.tsx:41-44`, `Math.floor(Math.random()*360)`). On plain
   **Generate** (`RouteForm.tsx:35-39`, no `seed` sent), `LoopRouteGenerator.cs:41`
   sets `baseBearing=0` deterministically, so sectors are always exactly
   60°/180°/300° — south is *never* missing on a first Generate. Confirmed
   inapplicable to this specific GPX (user confirmed it was a Generate click).
2. **`SelectBestRoute` scoring has no "known-good-direction"/scenic signal** —
   `LoopRouteGenerator.cs:136-179` ranks by paved ratio → smoothness → overlap →
   distance-closeness only. Confirmed proximate cause: south (180°) *was* generated
   this run (per dimension 1) and still lost the selection, meaning it scored worse
   on those metrics than the NW (300°) candidate.
3. **`routing-quality-osm` Phase 3 (scenic/low-traffic scoring) is planned but not
   yet implemented** — its contract (`plan.md:189-207`) inserts `scenicScore` as the
   **first** tie-break, ahead of paved ratio, specifically to prefer
   cycleway/designated-bicycle/cycle-network roads over merely-more-paved ones.
   `FindScenicWaysAsync` (`OverpassClient.cs:26-33`) is fully built but never called
   yet — the mechanism exists, isn't wired in. This is the leading candidate: it
   directly targets dimension 2's gap and was already scoped before this
   observation occurred.
4. **Missing OSM cycling-route-relation seeding** — `route-enhancement-ideas.md`
   Idea #2 (priority #2, above Idea #3/scenic-scoring) would query Overpass for
   `route=bicycle` relations and "replace geometric bearing logic entirely." More
   powerful than dimension 3 but explicitly deferred (`plan.md:34`) as a separate,
   larger mechanism — not required if dimension 3 alone resolves the observation.
5. **No user-facing direction/destination input** — `RouteForm.tsx` exposes only
   start point, `minKm`, `maxKm`. No field lets a rider bias direction. Real and
   user-confirmed as a want, but — per the pressure-test — not required to explain
   or fix *this* observation; dimension 3 (already planned) is sufficient if the
   target road is OSM-tagged.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| 1. Candidate coverage / random seed | `RouteForm.tsx:35-44`; `LoopRouteGenerator.cs:41`; user confirmed this GPX came from Generate (seed=null), not Re-roll | RULED OUT for this observation — south was generated, not missing |
| 2. `SelectBestRoute` scoring gap (proximate cause) | `LoopRouteGenerator.cs:136-179`; south candidate existed (per #1) yet NW won, and scoring has zero direction/scenic signal today | STRONG — confirmed mechanical cause of this specific result |
| 3. Phase 3 (scenic scoring) not yet implemented — **leading fix candidate** | `routing-quality-osm/plan.md:189-207` (scenicScore as first tie-break, explicit intent: "prefer scenic/low-traffic roads as the new primary preference layer"); `OverpassClient.cs:26-33` (`FindScenicWaysAsync` built, unused); current change is mid-flight at Phase 2 | STRONG — directly resolves dimension 2's gap, already scoped and half-built, no new change needed if it works |
| 4. Missing OSM route-relation seeding (Idea #2) | `route-enhancement-ideas.md:14-22`; `research.md:135`; `plan-brief.md:23`; `plan.md:34` (explicit deferral) | STRONG evidence it's unimplemented, but not required — dimension 3 is a lighter-weight fix for the same symptom class, already in flight |
| 5. No user-facing direction/destination input | `RouteForm.tsx` field list; user directly confirmed wanting this | STRONG as a genuine, separate want — but NOT evidenced as necessary to fix this observation |

## Pressure-Test Finding (Step 5 — changes the conclusion)

Searching `context/changes/routing-quality-osm/` for prior scoring-design decisions
surfaced `plan.md:189-207` (Phase 3, "Scenic/low-traffic way-tag scoring" — **planned
but not yet implemented**; the current change is mid-flight at Phase 2). Its contract
is unambiguous:

> "insert it as the **first** `OrderByDescending` in both the primary and fallback
> chains, ahead of `pavedRatio` — v2's stated purpose is to prefer scenic/low-traffic
> roads as the new primary preference layer"

This is precisely the mechanism that would fix the confirmed proximate cause
(dimension 2: `SelectBestRoute` picked the NW candidate over the available south
candidate on paved-ratio/smoothness alone). If the Wisła riverside path toward Góra
Kalwaria carries any of Phase 3's target tags (`highway=cycleway`, `bicycle=designated`,
`network=lcn|rcn|ncn` — plausible for a known Warsaw riverside route), a south
candidate following it would likely outscore a merely-more-paved NW candidate once
`scenicScore` becomes the primary tie-break.

Also confirmed by the trace agent: `FindScenicWaysAsync` is fully built
(`OverpassClient.cs:26-33`) but **never called** — not abandoned, simply not wired in
yet, exactly matching "Phase 3 not started."

**This means the `loop-direction-hint` reframe was premature.** It jumped to "build a
new feature" without first checking whether the already-planned next phase of the
*current* change resolves this specific symptom. Per the frame skill's own guardrail
("manufactured reframings are worse than no frame"), that check should have happened
before writing a new change folder.

## Narrowing Signals

- User's own answer to the direct disambiguating question: *"I want some way to
  steer/hint direction myself"* — explicitly selected over "I expect it to
  recognize known cycling corridors" (dimension 3) and over "any good-quality
  direction would be fine" (dimensions 1/2 combined, i.e. purely a quality/coverage
  fix without user control).
- Cross-check: neither `prd-v2.md` nor `roadmap.md` nor `route-enhancement-ideas.md`
  contains any prior mention of a direction/destination-hint control — confirming
  this is a genuinely new, previously-unscoped gap rather than something already
  considered and rejected (unlike Idea #2, which *was* considered and explicitly
  deferred with reasoning on record).

## Cross-System Convention

No existing convention in this codebase for user-directed route steering — the
loop generator has always been purely geometric/random-seeded, with no notion of
rider intent beyond start point and distance range. The one related, already-scoped
idea (start-point wiggle, Idea #7) is orthogonal: it would shift the *pinned
coordinate*, not add a direction parameter, and was itself deferred pending its own
scoping questions (wiggle radius, opt-in vs. default, interaction with the distance
constraint) — a precedent for treating rider-steering features as needing dedicated
scoping rather than folding them into `routing-quality-osm`.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: the Wilanów/Góra Kalwaria observation
> is best explained as `routing-quality-osm` simply not having reached Phase 3 yet —
> `SelectBestRoute` currently has no scenic/low-traffic signal, so a merely-more-paved
> candidate can beat a more-scenic one on a direction a human would obviously prefer.
> Phase 3 (already planned, not yet built) targets exactly this gap. **The initial
> `routing-quality-osm` framing was correct**; no new change is evidenced as necessary
> to fix this specific observation.

Separately — and this does NOT depend on the above — the user confirmed a genuine,
independent want: some way to steer/hint direction themselves. That's real product
signal, but the evidence does not show it's *required* to fix the Góra Kalwaria case;
Phase 3 might resolve it on its own if the target road carries OSM scenic/cycleway
tags. Manufacturing a new change now, before Phase 3 is even tried, would be
building ahead of evidence.

## Confidence

**HIGH** on the proximate-cause diagnosis (dimension 2, confirmed) and on Phase 3
being the correct next step to try (dimension 3, directly evidenced by the existing
plan's own contract). **MEDIUM** on whether Phase 3 alone fully resolves the Góra
Kalwaria case specifically — depends on real OSM tagging data for that road, which
hasn't been checked live yet (verification step below).

## What Changes for /10x-plan

No new plan needed yet. Recommended sequence: (1) resume `routing-quality-osm` —
commit Phase 2, implement Phase 3 (scenic scoring) as already specified in
`plan.md:189-207`; (2) re-run the Wilanów/Góra Kalwaria manual check against Phase 3;
(3) only if the south candidate *still* loses even with `scenicScore` active, that's
new decisive evidence for either dimension 4 (route-relation seeding) or dimension 5
(direction hint) — re-open a frame at that point with that evidence in hand, rather
than now.

The direction-hint want (dimension 5) is worth keeping on record as a parked idea
(alongside Idea #7 in `route-enhancement-ideas.md`) for future prioritization, but
this brief does not recommend planning it now.

## References

- Source files: `src/frontend/src/components/RouteForm.tsx:12,35-44`,
  `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:8-9,41-47,136-179`,
  `src/backend/VeloRoute/Routing/OverpassClient.cs:26-33`
- Related research: `context/changes/routing-quality-osm/research.md:135`,
  `context/changes/routing-quality-osm/plan-brief.md:23,45,59`,
  `context/changes/routing-quality-osm/plan.md:34,189-207`
- Related deferred idea: `context/foundation/route-enhancement-ideas.md:14-22`
  (Idea #2), `:72-89` (Idea #7, orthogonal)
- Investigation: one independent Explore sub-agent (unbiased request→direction
  chain trace, no prior framing supplied) + direct user confirmation (Generate vs.
  Re-roll) + direct file reads for the Step 5 pressure-test
