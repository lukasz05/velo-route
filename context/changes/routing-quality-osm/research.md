---
date: 2026-07-26T00:00:00Z
researcher: Claude
git_commit: 70d5338c298b9a8177c52df739d9835cf840f7ee
branch: main
repository: lukasz05/velo-route
topic: "Routing quality — OSM scenic/low-traffic preference + cyclist POI proximity (S-07)"
tags: [research, codebase, routing, osm, overpass, loop-route-generator]
status: complete
last_updated: 2026-07-26
last_updated_by: Claude
---

# Research: Routing quality — OSM scenic/low-traffic preference + cyclist POI proximity (S-07)

**Date**: 2026-07-26
**Researcher**: Claude
**Git Commit**: 70d5338c298b9a8177c52df739d9835cf840f7ee
**Branch**: main
**Repository**: lukasz05/velo-route

## Research Question

What does the codebase and prior project history tell us about implementing S-07 (`routing-quality-osm`): route generation that prefers OSM scenic/low-traffic-tagged roads (best-effort) and routes near cyclist POIs (cafes, water points, rest stops) from OSM (best-effort), without ever violating the user's min–max km distance constraint? Goal: produce a complete, evidence-based input for `/10x-plan routing-quality-osm`.

## Summary

The feature is a **greenfield integration** — no OSM/Overpass code exists anywhere in the codebase today. It slots into an existing, well-understood pipeline (`LoopRouteGenerator` → 3 parallel ORS calls → `SelectBestRoute` scoring/filtering) that already treats distance as a hard filter and other quality signals (paved ratio, smoothness, overlap) as soft lexicographic tie-breakers — the exact shape S-07's "distance always wins, preferences best-effort" requirement needs. A design doc already exists (`context/foundation/route-enhancement-ideas.md`) proposing the two concrete Overpass-based mechanisms this feature should implement. The prior `routing-api-wiring` change (v1 ORS integration) is a ready-made template for building a second typed HTTP client (`OverpassClient`) with the same options/DI/resilience/error-handling conventions. The prior `loop-algorithm-tuning` change is a cautionary tale: a planned calibration phase was silently dropped, and two latitude-correction geometry bugs slipped past tests into review — both are directly relevant risks for any new OSM-distance/proximity math. The single open engineering question — v1's ≤5s latency NFR was never reconfirmed for OSM querying — is real: the existing ORS timeout budget (4.5s) already consumes nearly the whole v1 NFR, so a second sequential external call will not fit without either running Overpass in parallel with the ORS `Task.WhenAll`, giving it its own short sub-timeout with silent fallback, or renegotiating the NFR.

## Detailed Findings

### Current routing pipeline (where S-07 hooks in)

- HTTP entry `POST /routes/loop` validates min/max km and lat/lon bounds inline, then wraps the whole call in a single `CancellationTokenSource(TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds))` — currently 4.5s — linked with the request's own token ([`Program.cs:322-356`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Program.cs#L322-L356)).
- `LoopRouteGenerator.GenerateAsync` computes a radius (`(minKm+maxKm)/2 * RadiusFactor`, `RadiusFactor = 0.45`) and fires exactly `BearingCount = 3` **parallel** ORS calls via `Task.WhenAll`, each a 4-point loop through two geometrically-placed waypoints ([`LoopRouteGenerator.cs:7-9`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L7-L9), [`LoopRouteGenerator.cs:24-56`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L24-L56)).
- **A scoring/preference layer already exists** in `SelectBestRoute` ([`LoopRouteGenerator.cs:58-101`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L58-L101)) and is the exact pattern S-07 must extend, not invent:
  ```csharp
  var primary = candidates
      .Where(c => c.distance >= minMeters && c.distance <= maxMeters && c.overlapRatio <= PrimaryOverlapThreshold)
      .OrderByDescending(c => c.pavedRatio)
      .ThenByDescending(c => c.smoothnessScore)
      .ThenBy(c => Math.Abs(c.distance - targetMidMeters))
      .FirstOrDefault();
  ```
  Distance is a **hard `.Where()` filter**; quality (paved ratio, smoothness, closeness to target) is a **lexicographic soft tie-break**. If nothing is both in-range and low-overlap, a fallback path relaxes overlap but never distance; if nothing is in the distance range at all, it's `NO_VALID_RESULT`. This is already the "distance constraint always wins, other preferences best-effort" pattern the PRD asks for — a new `scenicScore`/POI-related field is a natural additional `.ThenByDescending()`.
- ORS is given a **fixed waypoint list per call and returns one deterministic route** — there is no ORS "alternatives" API being used. "Selection" today means choosing among the 3 bearing-sector candidates, not among ORS-provided route alternatives for one sector. This has a direct implication for POI-proximity: it must be expressed as **waypoint placement** (nudging `wp1`/`wp2` toward a nearby OSM POI before calling ORS), not as post-hoc route selection.
- Config/DI pattern to mirror for a new `OverpassOptions`/`OverpassClient`: POCO options class → `Configure<T>(builder.Configuration.GetSection("Overpass"))` → `AddHttpClient<IOverpassClient, OverpassClient>()` with `ConfigureHttpClient` + `AddStandardResilienceHandler` ([`Program.cs:31-54`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Program.cs#L31-L54), [`OpenRouteServiceOptions.cs`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/OpenRouteServiceOptions.cs)).
- Route generation is **fully DB-free** — `/routes/loop` takes no `AppDbContext`, and none of the routing test classes wire Postgres/Testcontainers. New S-07 tests can follow the existing `FakeOpenRouteServiceClient`-in-`ConcurrentQueue` pattern with a new `FakeOverpassClient`, no Postgres dependency needed.
- Test tiers to mirror: unit (`OrsMapperTests.cs`, pure functions), integration via `VeloRouteWebApplicationFactory` with faked external client (`LoopRouteIntegrationTests.cs`, `RouteQualityTests.cs`), and a `[Fact(Skip = "Live ... — run manually")]` live-smoke tier (`OrsLiveSmokeTests.cs`) asserting quality thresholds against the real API.

### Existing resilience/timeout pattern (the latency constraint S-07 must respect)

- All resilience for ORS is HttpClient-level, not inside the client class: `AddStandardResilienceHandler` with `Retry.MaxRetryAttempts = 2` (restricted to timeout/5xx, **must explicitly exclude 429** — the default doesn't, per a caught review finding), `CircuitBreaker.FailureRatio = 0.5` over a 30s window ([`Program.cs:44-54`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Program.cs#L44-L54)).
- The **per-request deadline is a single end-to-end budget for the whole `/routes/loop` call** (all 3 parallel ORS calls together) — `OpenRouteServiceOptions.TimeoutSeconds` defaults to **4.5s** ([`OpenRouteServiceOptions.cs:7`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/OpenRouteServiceOptions.cs#L7), `appsettings.json` `"ORS": { "TimeoutSeconds": 4.5 }`). This is effectively the *entire* v1 "≤5s" NFR already consumed by ORS alone.
- **Implication for S-07**: adding a second sequential external HTTP dependency (Overpass) inside the same request lifecycle, without widening the budget, will not fit inside 5s. Options to reconcile, to be decided during planning: (a) run Overpass calls in parallel with the ORS `Task.WhenAll` in `FetchCandidatesAsync`, (b) give Overpass its own short sub-timeout with silent/graceful fallback so a slow or failed Overpass call never fails the whole request (unlike ORS failure, which does fail the request today, per `LoopRouteIntegrationTests.cs`), or (c) re-negotiate the NFR itself. Overpass failure must degrade to "no OSM preference applied," never to a request error — this matches the "graceful fallback where OSM data absent" requirement directly.
- Error-handling convention to reuse: `RoutingResult<T>` discriminated union (`Value`/`Error`, `IsSuccess`, `Success`/`Failure` factories) plus `RoutingError(string Code, string Message)` — a new `OverpassClient` should return the same shape for consistency with the existing `OpenRouteServiceClient`.

### Prior art already scoped for this exact feature

- `context/foundation/route-enhancement-ideas.md` is a pre-existing design doc (written during the `loop-algorithm-tuning` change) that already proposes the two S-07 mechanisms concretely:
  - **"OSM Cycling Route Seeding"**: query Overpass for `type=route, route=bicycle` relations near the start (`relation[route=bicycle](around:<radius>,<lat>,<lon>)`), extract waypoints from matching segments, fall back to current geometric bearing logic when none found nearby.
  - **"POI-Directed Bearings"**: aim waypoints toward OSM POIs (`tourism=viewpoint`, `natural=peak`, `leisure=nature_reserve`, `amenity=cafe`+`bicycle=yes`, `water=lake`, `natural=beach`) — query Overpass for top-N POIs within `radius * 2` of start, use their bearings as candidate waypoint directions instead of equally-spaced geometric offsets. Cost estimate noted at the time: "~100ms, cacheable by area" per request.
  - A third, ORS-only idea ("Road Type Scoring": weight `RoadClass` cycleway > residential > unclassified > tertiary > secondary > primary/trunk) could partly satisfy the "low-traffic" preference **without any new external dependency** — worth considering as either a complement or a cheaper first increment.
- `context/foundation/loop-route-algorithm.md` (the original v1 decision record) confirms **Itinero** (a C# routing library requiring self-hosted OSM `.pbf` files) was already evaluated and rejected for v1 as too costly operationally — precedent against any heavyweight OSM-processing library for S-07; a thin Overpass HTTP client is the validated-cheap path, consistent with the roadmap's stated default.
- `context/archive/2026-05-30-loop-route-generation/research.md` (S-01 original research) notes prior art for using Overpass to snap to actual road nodes (not just tag/POI lookup), and a hard usage-policy constraint: **Nominatim explicitly prohibits per-keystroke/autocomplete-style requests**; Overpass's public instance (`overpass-api.de`, the roadmap's named default) has its own rate-limit policy that should be checked during planning, since nothing in the roadmap or PRD addresses it yet.

### Prior tuning history — thresholds and lessons to carry forward

- Current baked-in geometry constants (never empirically calibrated — the planned calibration phase in `loop-algorithm-tuning` was explicitly deferred and never completed): `RadiusFactor = 0.45`, `BearingCount = 3`, `PrimaryOverlapThreshold = 0.10` ([`LoopRouteGenerator.cs:7-9`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L7-L9)).
- Locked live-smoke acceptance thresholds (`OrsLiveSmokeTests.cs`, calibrated against real ORS data for Warsaw/Olsztyn/Gdynia): pavedRatio ≥ 0.90, overlapRatio ≤ 0.40, bbox aspect ratio ≤ 3.0, distance accuracy ≤ 15% of target midpoint. These define what "no regression from v1" means and are a template for whatever S-07 quality threshold (e.g. minimum scenic-tag hit rate, or simply "no regression on the existing four") gets locked for the live-smoke tier.
- **Direct geometry-bug risk for S-07**: `PavedRatioCalculator` and `SmoothnessCalculator` both originally used raw lon/lat degree deltas without a `cos(latitude)` correction, causing paved-ratio miscounts up to ~35% and bearing errors of ~13° at 52°N — caught only in review, not by tests. Fix pattern now in production: `dx = (B.Lon - A.Lon) * Math.Cos(avgLat_radians)` before distance/bearing math (`PavedRatioCalculator.cs:28-29`, `SmoothnessCalculator.cs:25-26`). **Any new POI-distance or scenic-segment-length calculation in S-07 must apply the same latitude correction from the start**, and the plan should call for a review focus on this specifically since it slipped past tests twice already.
- Related trap: a `pavedRatio === 0` check once conflated "no data" with "genuinely 0%" (fixed to check `segments.length == 0` instead). A new `scenicScore`/`poiCount` field should not repeat this — "no OSM tags nearby" must be distinguishable from "found tags, score is legitimately zero," so the fallback-to-neutral behavior is unambiguous.
- Explicit lesson from the same change's plan-brief: the intent was to avoid runtime-configurable tuning knobs entirely ("bake optimal values" rather than exposing parameters) — "avoids infinite parameter surface = infinite tuning loop." S-07 should scope tightly (e.g., fixed Overpass query radius/POI categories, not user-configurable) and treat "measure and tune" as its own hard-gated step, since the precedent shows it can silently not happen otherwise.

### PRD constraints that bound the design space

- **FR-010**: scenic/low-traffic preference, best-effort, graceful fallback; PRD explicitly accepts "may be a no-op in sparsely-tagged regions" as OK — no minimum coverage guarantee required.
- **FR-011**: POI proximity (cafes, water, rest stops), best-effort; **"distance constraint takes priority and POIs are included only when reachable within the user's min–max km range."**
- **Hard constraint** (not just a default choice): "OSM is the only data source for routing improvements in v2" — Strava Segments API is explicitly excluded (OAuth, not free/public). No other data source is permitted for S-07.
- **Open Question 1** (authoritative, non-blocking for planning but must be resolved during implementation): "Route generation latency for v2. The v1 ≤5s NFR was not re-confirmed after adding OSM POI querying... measure during implementation; define before shipping v2."
- One-route-per-request stays in force (no multiple proposals — v3 deferred), so OSM scoring must still converge on a single winning route via the same `SelectBestRoute`-style mechanism, not branch into a multi-candidate UI.

### Template for the second HTTP integration (from `routing-api-wiring`, the original ORS wiring)

- Typed client + interface (`IOverpassClient` / `OverpassClient : IOverpassClient`), registered via `AddHttpClient<TInterface, TImpl>()`, options bound from `appsettings.json` with an env-var override convention (`Overpass__BaseUrl`, mirroring `ORS__ApiKey`).
- **Non-obvious lesson from the ORS wiring itself**: a request/response field-name mismatch (`"waytype"` singular in the request vs `"waytypes"` plural in the response) was caught only in plan review and would have silently mapped all data to `Unknown`. Applies directly to Overpass: verify exact Overpass QL/response field names against real captured payloads before locking any mapper contract — don't trust memory for API shapes.
- Data-contract discipline: `OverpassClient` should be the sole owner of Overpass QL/JSON knowledge; only internal domain types (e.g. an `OsmPoi` / `ScenicSegment` record) should cross into `LoopRouteGenerator`, mirroring how `OpenRouteServiceClient` isolates all ORS-specific shapes today.
- Retry-safety lesson: raw exception messages must never leak into HTTP responses; cancellation must be explicitly re-thrown, not swallowed into a generic failure.

### Available packages (no new geospatial library needed)

`VeloRoute.csproj` already includes `NetTopologySuite` 2.5.0, `NetTopologySuite.IO.GeoJSON4STJ` 4.0.0, and `Microsoft.Extensions.Http.Resilience` 10.6.0 — sufficient for a plain `HttpClient`-based Overpass QL client with `System.Text.Json` deserialization (Overpass is typically queried with `[out:json]`, not GeoJSON, so the GeoJSON package may not even be needed unless Overpass Turbo's GeoJSON export is used specifically). No `OsmSharp` or `.pbf`-processing library exists or is implied to be needed — consistent with the earlier rejection of self-hosted OSM processing (Itinero) for v1. `Npgsql`/EF Core is already wired if S-07 wants to cache Overpass POI/tag results by region (worth considering given the latency and Overpass rate-limit concerns above); no distributed-cache package (e.g. Redis) exists if that route is chosen.

### Documentation currently stale relative to this feature

`context/foundation/backend/tech-stack.md` predates ORS and NetTopologySuite entirely and does not mention OSM/Overpass — per the project's "keep docs accurate" workflow rule, this should be updated in S-07's final commit once the Overpass client and any new packages land.

## Code References

- [`src/backend/VeloRoute/Program.cs:322-356`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Program.cs#L322-L356) — `/routes/loop` endpoint: validation + single end-to-end timeout budget
- [`src/backend/VeloRoute/Program.cs:31-54`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Program.cs#L31-L54) — ORS options/DI/resilience registration, the template for `OverpassClient`
- [`src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:7-9`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L7-L9) — baked-in, never-calibrated geometry constants
- [`src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:24-56`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L24-L56) — candidate generation (3 parallel ORS calls); waypoint-placement hook for POI-directed bearings
- [`src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:58-101`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/LoopRouteGenerator.cs#L58-L101) — `SelectBestRoute`: distance hard-filter + soft lexicographic scoring, the hook for a new scenic-score tie-break
- [`src/backend/VeloRoute/Routing/OpenRouteServiceOptions.cs`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/OpenRouteServiceOptions.cs) — options POCO pattern to mirror for `OverpassOptions`
- [`src/backend/VeloRoute/Routing/PavedRatioCalculator.cs:28-29`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/PavedRatioCalculator.cs#L28-L29), [`SmoothnessCalculator.cs:25-26`](https://github.com/lukasz05/velo-route/blob/70d5338c298b9a8177c52df739d9835cf840f7ee/src/backend/VeloRoute/Routing/SmoothnessCalculator.cs#L25-L26) — latitude-correction pattern that must be reused for any new distance math
- `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs` — `FakeOpenRouteServiceClient` pattern to mirror with a `FakeOverpassClient`
- `src/backend/VeloRoute.Tests/Routing/OrsLiveSmokeTests.cs` — locked quality thresholds and live-smoke test pattern
- `src/backend/VeloRoute/VeloRoute.csproj` — confirms `NetTopologySuite`, `NetTopologySuite.IO.GeoJSON4STJ`, `Microsoft.Extensions.Http.Resilience` already available

## Architecture Insights

- The codebase already has the exact "hard constraint + soft best-effort tie-break" scoring shape S-07 needs (`SelectBestRoute`); this is an extension of an established pattern, not new architecture.
- ORS is called with fixed waypoints and returns one deterministic route per call — there is no route-alternatives mechanism to select from. Any new preference that isn't expressible as a scoring tie-break (like POI proximity) must be expressed as **waypoint placement before the ORS call**, not as post-hoc selection.
- External-dependency isolation is a firm convention: each HTTP integration (ORS today, Overpass tomorrow) owns all of its own wire-format knowledge behind a typed client interface; only internal domain records cross the boundary.
- Timeout budget is a single end-to-end allowance for the whole request, not per-external-call — a new external dependency must either share the parallel `Task.WhenAll` or get its own short, silently-degrading sub-timeout; it cannot simply be bolted on sequentially.

## Historical Context (from prior changes)

- `context/archive/2026-05-30-routing-api-wiring/` — original ORS client wiring; direct template for a second HTTP integration (options/DI/resilience/error-handling conventions, plus the caught request/response field-name-mismatch lesson).
- `context/archive/2026-06-20-loop-algorithm-tuning/calibration.md` — locked live-smoke thresholds; explicit record that the planned calibration phase was deferred and never completed (a scope-creep/follow-through risk to plan around explicitly this time).
- `context/archive/2026-06-20-loop-algorithm-tuning/reviews/impl-review.md` — the two latitude-correction geometry bugs and the `pavedRatio === 0` "no data vs. zero" trap, both directly relevant to new OSM-distance/proximity math.
- `context/foundation/route-enhancement-ideas.md` — pre-existing, already-prioritized design doc proposing the exact Overpass mechanisms ("OSM Cycling Route Seeding," "POI-Directed Bearings," and an ORS-only "Road Type Scoring" alternative) S-07 should draw its approach from.
- `context/foundation/loop-route-algorithm.md` — records the earlier rejection of a self-hosted OSM-processing library (Itinero) for cost reasons, bounding S-07 to a thin Overpass HTTP client rather than a heavier OSM-data approach.
- `context/archive/2026-05-30-loop-route-generation/research.md` — prior art on using Overpass for road-node snapping, and the Nominatim autocomplete usage-policy prohibition (a signal to check Overpass's own public-instance rate-limit policy before committing to `overpass-api.de` as the default host).

## Related Research

- No other `context/changes/**/research.md` currently exists for this topic; this is the first research artifact for `routing-quality-osm`.

## Open Questions

1. **Latency budget reconciliation** — how should the ~4.5s ORS budget and a new Overpass call coexist within (or replace) the v1 ≤5s NFR? Needs a decision during planning (parallel fetch vs. short sub-timeout vs. renegotiated NFR) — PRD explicitly defers this to implementation but it must be resolved before shipping.
2. **Overpass host and rate-limit policy** — the roadmap names `overpass-api.de` as the default candidate but no usage-policy check has been done; should the plan pin a specific public instance, self-host, or add caching to stay within rate limits?
3. **Scope choice between the two proposed mechanisms** — should S-07 implement both "OSM cycling route seeding" and "POI-directed bearings" from `route-enhancement-ideas.md`, or start with one plus the dependency-free "Road Type Scoring" (ORS-only) as a cheaper first increment? This is a planning-stage scope decision, not yet made anywhere in the project record.
4. **Definition of "no regression" / acceptance threshold for S-07** — per the `loop-algorithm-tuning` lesson, an explicit, locked acceptance threshold (e.g., minimum scenic/POI hit rate where data exists, or simply preserving the four existing live-smoke thresholds) should be defined before implementation starts, not discovered mid-tuning.
