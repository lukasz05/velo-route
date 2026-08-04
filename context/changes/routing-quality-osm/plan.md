# Routing Quality — OSM Scenic/Low-Traffic + Cyclist POI Proximity Implementation Plan

## Overview

Add a best-effort OSM preference layer on top of the existing loop-route generation pipeline: routes prefer scenic/low-traffic-tagged OSM ways and pass near cyclist POIs (cafes, water points, rest stops) where reachable, without ever loosening the user's min–max km distance constraint. Data source is OSM-only (Overpass API), per PRD constraint. The user's start/end coordinate stays pinned exactly as entered — only the intermediate waypoints and post-hoc scoring change.

## Current State Analysis

`LoopRouteGenerator` (`src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`) generates 3 candidate loops per request via parallel calls to `IOpenRouteServiceClient`, each through 2 geometrically-placed intermediate waypoints (`WaypointCalculator.DestinationPoint`, pure spherical bearing/radius math, no map data). `SelectBestRoute` then applies a hard distance `.Where()` filter followed by a lexicographic `OrderByDescending(pavedRatio).ThenByDescending(smoothnessScore).ThenBy(closeness-to-target)` soft tie-break — this is the exact "hard constraint + best-effort preference" shape the new OSM signals need to extend.

`RouteResult` (`src/backend/VeloRoute/Routing/RouteResult.cs`) is serialized directly as the `/routes/loop` HTTP response body (`Results.Ok(result.Value)` in `Program.cs:350`) and is mirrored field-for-field in the frontend's `RouteResult` TypeScript interface (`src/frontend/src/types/route.ts:17-23`) — any new field added to the C# record must be mirrored there to stay usable by the client.

No OSM/Overpass integration exists anywhere in the codebase today. The existing `IOpenRouteServiceClient`/`OpenRouteServiceClient` pair (`src/backend/VeloRoute/Routing/OpenRouteServiceClient.cs`, `IOpenRouteServiceClient.cs`, `OpenRouteServiceOptions.cs`) is the template to mirror: typed HTTP client registered via `AddHttpClient<TInterface, TImpl>()`, options bound from `appsettings.json`, `AddStandardResilienceHandler` for retry/circuit-breaker, and a `RoutingResult<T>` discriminated-union return type so callers never need to catch exceptions for expected failures. `FakeOpenRouteServiceClient` (`src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:72-95`) plus `VeloRouteWebApplicationFactory`'s DI-swap (`TestInfrastructure.cs:136-190`) is the template for a new `FakeOverpassClient`.

The request-level timeout (`Program.cs:330-331`) wraps the *entire* `/routes/loop` call in one `CancellationTokenSource(TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds))` (default 4.5s) linked to the caller's own token. This single end-to-end budget is nearly the whole v1 "≤5s" NFR already, so a new external dependency cannot simply be added sequentially without exceeding it — it must run in parallel where possible and degrade silently on its own short timeout where it can't.

### Key Discoveries:

- `SelectBestRoute`'s distance filter operates on ORS's *actual returned* route distance (`LoopRouteGenerator.cs:75`), which runs *after* waypoint placement — so any new waypoint-nudging mechanism is automatically bounded by the existing distance safety net; a nudge that produces an out-of-range route is simply filtered out like any other candidate today.
- `PavedRatioCalculator.cs:28-29` and `SmoothnessCalculator.cs:25-26` apply a `cos(latitude)` correction before computing planar coordinate deltas — this correction bug (raw degree deltas without it caused ~35% miscounts) was only caught in review on the prior `loop-algorithm-tuning` change, not by tests. Any new distance/proximity math in this plan must apply the same correction from the start.
- `OverlapDetector.cs` already uses a NetTopologySuite `STRtree` for spatial segment-proximity matching (route self-overlap) — this is the pattern to reuse for matching route segments against externally-fetched OSM way geometries, rather than inventing new spatial-indexing code.
- `NetTopologySuite` 2.5.0 and `Microsoft.Extensions.Http.Resilience` 10.6.0 are already referenced in `VeloRoute.csproj` — no new NuGet packages are required.
- Overpass QL syntax (confirmed against the OSM wiki, not assumed from memory per the lesson from the original ORS field-name mismatch): `[out:json]; way(around:<radius>,<lat>,<lon>)[<tag filter>]; out geom;` for ways, `node(around:<radius>,<lat>,<lon>)[<tag filter>]; out geom;` for POIs. Response JSON has top-level `elements[]`, each with `type`, `id`, `lat`/`lon` (nodes) or `geometry: [{lat,lon}, ...]` (ways with `out geom`), and `tags: {}`. Public endpoint: `POST https://overpass-api.de/api/interpreter` with the query as the request body.

## Desired End State

A cyclist generating a loop route gets a route that, on a best-effort basis, favors OSM-tagged scenic/low-traffic ways and passes near cyclist POIs (cafes, water, rest stops) — while the distance range, single-route-per-request behavior, and start/end pinning are unchanged from v1. When OSM data isn't available or Overpass is slow/unreachable, the route falls back to today's ORS-only behavior with no user-visible error. The API response carries a new `osmEnriched` boolean so the fallback behavior is observable in tests and via the network tab.

Verification: `dotnet test` passes (existing + new tests); one manual generation in a well-OSM-tagged area (e.g. Warsaw) shows `osmEnriched: true` and a route that visibly differs from disabling the new scoring; a manual Overpass-unreachable simulation (e.g. wrong `Overpass:BaseUrl`) still returns a valid route with `osmEnriched: false`.

## What We're NOT Doing

- Not shifting the user-entered start/end coordinate ("start-point wiggle") — parked as a separate future idea in `context/foundation/route-enhancement-ideas.md` (Idea #7) and `context/foundation/roadmap.md` Parked section.
- Not implementing OSM cycling-route-relation seeding (`route=bicycle` relations) — a separate, larger mechanism from the design doc's Idea #2; out of scope for this slice.
- Not adding elevation scoring, iterative waypoint nudging, or road-type scoring (design doc Ideas #4, #5, #6) — separate ideas, not part of FR-010/FR-011.
- Not caching Overpass responses (in-memory or Postgres) — ships without a cache; revisit only if real traffic shows rate-limit or latency pressure.
- Not adding multiple Overpass host fallback/round-robin — single public `overpass-api.de` instance for this slice.
- Not making POI categories, scenic tags, or query radii user-configurable — fixed values per this plan, to avoid the open-ended-tuning trap documented in the `loop-algorithm-tuning` history.
- Not adding any frontend UI display of the new `osmEnriched` flag or scenic/POI data — API-level observability only; a UI surface is a separate future decision.
- Not persisting `osmEnriched` (or any OSM-derived field) on saved routes — this plan only touches the anonymous `/routes/loop` generation path, not `save-route`/`route-library`.

## Implementation Approach

Two independent Overpass-backed mechanisms are added, each hooking into the pipeline at the point that matches its data dependency:

1. **POI-directed bearing nudging** — runs *before* the ORS calls, since it changes what waypoints are sent to ORS. Sequential with its own short hard timeout; on timeout/failure/no-match, a sector silently falls back to its current pure-geometric bearing.
2. **Scenic/low-traffic way-tag scoring** — runs *in parallel* with the ORS `Task.WhenAll`, since it only needs a bounding box around the start point (known before ORS responds), not the ORS output itself. Its own short timeout, wrapped so its cancellation can never fail the overall request the way ORS timeout does.

Both mechanisms share one new `OverpassClient` (two methods, one per query shape) built with the exact DI/options/resilience/`RoutingResult<T>` pattern already used for ORS. The distance hard-filter in `SelectBestRoute` is untouched; scenic scoring adds one more scoring dimension to the existing lexicographic tie-break, POI nudging only changes an input to candidate generation.

## Critical Implementation Details

### Timing & lifecycle

The scenic-tag Overpass call must not let its own timeout/cancellation propagate as an unhandled `OperationCanceledException` up to `Program.cs`'s top-level `catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)` — that catch is specifically for ORS's timeout meaning "the whole request failed." The scenic call needs its own internal try/catch that swallows both `OperationCanceledException` and any other exception, returning "no scenic data found" instead, regardless of which token (its own short-lived one or the shared request one) triggered the cancellation. It should be started alongside (not after) the ORS `Task.WhenAll` and joined via a second `Task.WhenAll` so total latency is `max(ORS time, min(scenic call time, its own timeout))`, not the sum.

The POI-lookup Overpass call is sequential and must complete (or time out and fall back) *before* `WaypointCalculator.DestinationPoint` is called for any sector, since its result changes which bearing is used as an input to that calculation.

The overall request timeout budget in `Program.cs` (currently `orsOpts.Value.TimeoutSeconds` alone) must widen to account for the sequential POI-lookup call: `TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds + overpassOpts.Value.PoiLookupTimeoutSeconds)`. This is the concrete resolution to the PRD's open latency question — composed from two configured values (4.5s + 1.5s = 6s worst case) rather than an arbitrary new constant, and should be re-measured against real Overpass latency during Phase 2's manual verification.

## Phase 1: Overpass client foundation

### Overview

Add the typed Overpass HTTP client, its config/DI/resilience wiring, and domain types — mirroring the existing ORS integration exactly — plus its test double. No behavioral change to route generation yet; this phase only makes the client available.

### Changes Required:

#### 1. Overpass options

**File**: `src/backend/VeloRoute/Routing/OverpassOptions.cs` (new)

**Intent**: Config POCO for the Overpass integration, following `OpenRouteServiceOptions.cs` exactly.

**Contract**: `public sealed class OverpassOptions { public string BaseUrl { get; set; } = "https://overpass-api.de/api/interpreter"; public double PoiLookupTimeoutSeconds { get; set; } = 1.5; public double ScenicLookupTimeoutSeconds { get; set; } = 2.0; }`. No API key — the public instance is keyless.

#### 2. Domain types

**File**: `src/backend/VeloRoute/Routing/OsmPoi.cs`, `src/backend/VeloRoute/Routing/OsmWay.cs` (new)

**Intent**: Internal domain records so `OverpassClient` is the sole owner of Overpass wire-format knowledge, matching how `RouteResult`/`RouteWaySegment` isolate ORS's shape today.

**Contract**: `public sealed record OsmPoi(RouteCoordinate Location, string Category);` and `public sealed record OsmWay(IReadOnlyList<RouteCoordinate> Geometry);` — `Category` is a short internal label (e.g. `"cafe"`, `"water"`, `"rest_stop"`) the client assigns from the matched tag filter, not the raw OSM tag value.

#### 3. Overpass client interface and implementation

**File**: `src/backend/VeloRoute/Routing/IOverpassClient.cs`, `src/backend/VeloRoute/Routing/OverpassClient.cs` (new)

**Intent**: Two independent lookups — POIs near a point, and scenic/low-traffic ways near a point — each returning a `RoutingResult<T>` so callers use the same success/failure handling as the ORS client. Internally issues `POST` to `OverpassOptions.BaseUrl` with an Overpass QL query body (`[out:json]; ...; out geom;`), deserializes the `elements[]` array via `System.Text.Json`, and maps to the domain records above.

**Contract**:
```csharp
public interface IOverpassClient
{
    Task<RoutingResult<IReadOnlyList<OsmPoi>>> FindPoisAsync(
        RouteCoordinate center, double radiusMeters, CancellationToken cancellationToken);

    Task<RoutingResult<IReadOnlyList<OsmWay>>> FindScenicWaysAsync(
        RouteCoordinate center, double radiusMeters, CancellationToken cancellationToken);
}
```
`FindPoisAsync` queries `node(around:<radius>,<lat>,<lon>)[...]` for the fixed tag set below (union of `amenity=cafe` filtered client-side to those also tagged `bicycle=yes` where present, `amenity=drinking_water`, `tourism=viewpoint`, `natural=peak`, `leisure=nature_reserve`, `natural=beach`) — matching `route-enhancement-ideas.md` Idea #3's list exactly, no additions or removals. `FindScenicWaysAsync` queries `way(around:<radius>,<lat>,<lon>)[...]` for `highway=cycleway`, `bicycle=designated`, and `network` in `lcn`/`rcn`/`ncn` — Idea #6's low-traffic-adjacent tags reinterpreted as OSM-sourced rather than ORS-`RoadClass`-sourced, per the `ScenicSrc` decision. Error handling follows `OpenRouteServiceClient.cs:86-98`'s pattern: catch `OperationCanceledException` (rethrow only if the caller's own token, not an internal one, was cancelled) and generic `Exception` (log, return `RoutingError("PROVIDER_ERROR", ...)`), never leak raw exception text into the result.

#### 4. DI, config, and resilience registration

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Register `OverpassOptions` and the typed `IOverpassClient` exactly like the ORS registration at `Program.cs:31-54`.

**Contract**: Add `builder.Services.Configure<OverpassOptions>(builder.Configuration.GetSection("Overpass"));` and `builder.Services.AddHttpClient<IOverpassClient, OverpassClient>().ConfigureHttpClient((sp, client) => client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OverpassOptions>>().Value.BaseUrl)).AddStandardResilienceHandler(options => { options.Retry.MaxRetryAttempts = 0; options.CircuitBreaker.FailureRatio = 0.5; options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30); options.CircuitBreaker.MinimumThroughput = 3; options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30); });` — zero retries (unlike ORS's 2), since Overpass is a best-effort call on an already-short timeout and a retry would only spend more of that budget on a shared public service.

#### 5. appsettings

**File**: `src/backend/VeloRoute/appsettings.json`

**Intent**: Add the `"Overpass"` section alongside `"ORS"`.

**Contract**: `"Overpass": { "BaseUrl": "https://overpass-api.de/api/interpreter", "PoiLookupTimeoutSeconds": 1.5, "ScenicLookupTimeoutSeconds": 2.0 }`.

#### 6. Fake test double

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: `FakeOverpassClient` mirroring `FakeOpenRouteServiceClient`'s queued-response pattern, plus DI-swap wiring in `VeloRouteWebApplicationFactory` so integration tests can inject canned Overpass responses without network access.

**Contract**: `internal sealed class FakeOverpassClient : IOverpassClient { public ConcurrentQueue<RoutingResult<IReadOnlyList<OsmPoi>>> PoiResults { get; } = new(); public ConcurrentQueue<RoutingResult<IReadOnlyList<OsmWay>>> ScenicWayResults { get; } = new(); public TimeSpan Delay { get; set; } = TimeSpan.Zero; ... }` — same dequeue-or-fail-with-`"EMPTY"` behavior as the existing fake. Add `FakeOverpassClient FakeOverpassClient { get; }` to `VeloRouteWebApplicationFactory` and remove/re-add the `IOverpassClient` service descriptor in `ConfigureServices`, matching lines 140-145's pattern for `IOpenRouteServiceClient`.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build` (from `src/backend/`)
- Unit tests pass: `dotnet test --filter FullyQualifiedName~OverpassClientTests` (new test class covering query construction and response mapping, mirroring `OrsMapperTests.cs`'s pure-function style)
- Full backend test suite passes: `dotnet test` (from `src/backend/`)

#### Manual Verification:

- With a valid `Overpass:BaseUrl`, a manual call to `IOverpassClient.FindPoisAsync` against real Overpass (e.g. via a scratch console call or the dev-only smoke pattern used for ORS) returns at least one POI for a known-dense area (central Warsaw)
- Confirm the exact Overpass QL query text sent (via logging or a debugger breakpoint) matches the intended tag filters — this is the step that catches wire-format mistakes before they're load-bearing, per the ORS `waytype`/`waytypes` lesson

---

## Phase 2: POI-directed waypoint bearing nudging

### Overview

Before calling ORS, query Overpass once for nearby cyclist POIs and substitute each bearing sector's *direction* (not its radius) with the bearing toward the nearest matching POI in that sector, when one exists — falling back to the current pure-geometric bearing per sector otherwise.

### Changes Required:

#### 1. POI-aware bearing selection

**File**: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`

**Intent**: `FetchCandidatesAsync` currently computes each sector's bearing purely geometrically (`baseBearing + phaseOffset + angularSpacing * i`). Before building the waypoint list, query `IOverpassClient.FindPoisAsync(start, radius * 2, ...)` once (not once per sector) with the `OverpassOptions.PoiLookupTimeoutSeconds` hard timeout; for each of the 3 sectors, if any returned POI's bearing-from-start falls within that sector's angular half-width **and** its distance from `start` falls within `[0.5, 1.5] * radius`, use the bearing of the POI whose distance is closest to `radius` in place of the geometric one for that sector only — same `radius` magnitude as before, so the overall loop shape/distance budget is unaffected. Sectors with no matching POI keep today's geometric bearing unchanged.

**Contract**: The Overpass call and its timeout/fallback wrapping happen inside `LoopRouteGenerator`, not in `Program.cs` — on timeout, failure, or zero results, `FetchCandidatesAsync` proceeds exactly as it does today (all 3 sectors geometric), and this must never surface as a request error. A private helper (e.g. `SelectBearing(start, radius, sector center bearing, half-width, List<OsmPoi>) -> double?`) computing bearing-from-start for each POI via the same spherical math as `WaypointCalculator` (not a planar approximation) is the natural extraction point.

**Revision (found during Phase 2 manual verification)**: the original "nearest POI wins" tie-break was found live-broken for large loops — a plain nearest-by-distance pick always favors amenities within a few hundred metres of `start` (e.g. a drinking fountain 105m away) regardless of sector, because Overpass's `radius * 2` search window returns hundreds of POIs for a big loop and the closest one to `start` is essentially uncorrelated with the loop's actual scale. Manually reproduced at Wilanów (Warsaw), 80–100km range: nearest sector-matching POI was 105m away at bearing 88°, overriding the sensible 180°-south geometric bearing toward Góra Kalwaria with a degenerate near-start waypoint. Fixed by adding the `[0.5, 1.5] * radius` distance band (excludes near-start POIs entirely) and switching the tie-break from nearest-to-start to nearest-to-`radius` (favors POIs sitting close to where the geometric waypoint would already land, so the nudge only fires when a POI is meaningfully near the loop's arc).

#### 2. Compose the widened request timeout

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: The `/routes/loop` endpoint's timeout budget must account for the new sequential POI-lookup call.

**Contract**: Change the `timeoutCts` construction at `Program.cs:330` from `TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds)` to `TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds + overpassOpts.Value.PoiLookupTimeoutSeconds)`, requiring `IOptions<OverpassOptions> overpassOpts` added to the endpoint delegate's parameters.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `dotnet test --filter FullyQualifiedName~LoopRouteGeneratorTests` (new or extended test class covering bearing substitution logic in isolation)
- Integration tests pass: `dotnet test --filter FullyQualifiedName~LoopRouteIntegrationTests` (extended to cover: POI found → nudged bearing used; POI lookup times out → falls back to geometric; POI lookup returns error → falls back to geometric)
- Full backend test suite passes: `dotnet test`

#### Manual Verification:

- Generate a route near a real cafe/water point cluster (e.g. central Warsaw) and confirm the returned route's early/late segments visibly pass closer to at least one such POI than the same request would without this change
- Simulate Overpass unavailability (invalid `Overpass:BaseUrl` in local config) and confirm `/routes/loop` still returns HTTP 200 with a valid geometric-fallback route within the new combined timeout budget
- Manually measure and record actual p95 latency for a handful of requests in a well-tagged region — confirms the composed 6s budget is realistic before Phase 5 locks it as a tested expectation

---

## Phase 3: Scenic/low-traffic way-tag scoring

### Overview

Add a new scoring dimension based on OSM way tags (cycleway, designated-bicycle, cycle-network membership) fetched via a single Overpass bbox query that runs in parallel with the existing ORS `Task.WhenAll`, then match each candidate route's segments against the fetched ways to compute a `scenicScore`, feeding it into `SelectBestRoute`'s existing tie-break chain.

### Changes Required:

#### 1. Scenic score calculator

**File**: `src/backend/VeloRoute/Routing/ScenicScoreCalculator.cs` (new)

**Intent**: Compute the fraction of a candidate route's length that runs within a small tolerance distance of any fetched `OsmWay`, analogous to how `PavedRatioCalculator` computes the paved fraction of route length — but matching against externally-fetched way geometries instead of ORS-provided segment tags.

**Contract**: `public static double Compute(RouteResult route, IReadOnlyList<OsmWay> scenicWays)` — build an NTS `STRtree` over the `scenicWays` line segments (same indexing approach as `OverlapDetector.cs`), then for each consecutive coordinate pair in `route.Geometry.Coordinates`, check proximity within a fixed tolerance (reuse `OverlapDetector`'s `ToleranceDeg = 0.000135` constant/rationale — valid for the same European-latitude assumption already documented there) against nearby indexed way segments; sum matched length as scenic length. Return `0.0` when `scenicWays.Count == 0` (explicitly "no data," not conflated with "found zero scenic tags" — the caller distinguishes these via the Overpass call's own success/failure, not this return value alone, avoiding the `pavedRatio === 0` ambiguity trap documented in the prior calibration review).

#### 2. Parallel scenic-tag fetch and scoring integration

**File**: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`

**Intent**: Kick off `IOverpassClient.FindScenicWaysAsync(start, radius * 1.5, ...)` (a bbox large enough to cover all 3 candidate loops, since it only depends on `start`/`radius`, known before any ORS call) concurrently with `FetchCandidatesAsync`'s `Task.WhenAll`, wrapped in its own try/catch per the Critical Implementation Details timing note above. Once both complete, compute `scenicScore` per candidate via `ScenicScoreCalculator.Compute` and add it as a new tie-break to `SelectBestRoute`.

**Contract**: In `GenerateAsync`, replace the single `await FetchCandidatesAsync(...)` with a joined `Task.WhenAll` of the existing candidates task and a new wrapped scenic-ways task; pass the resolved `IReadOnlyList<OsmWay>` (empty list on any failure/timeout) into `SelectBestRoute`. In `SelectBestRoute`'s candidate projection, add `scenicScore = ScenicScoreCalculator.Compute(route, scenicWays)`; insert it as the **first** `OrderByDescending` in both the primary and fallback chains, ahead of `pavedRatio` — v2's stated purpose is to prefer scenic/low-traffic roads as the new primary preference layer on top of the unchanged v1 baseline, and reordering is safe because the existing locked live-smoke thresholds (pavedRatio ≥0.90, overlap ≤0.40, aspect ≤3.0, distance accuracy ≤15%) are properties the *selected* route must satisfy regardless of which tie-break field picked it, not properties of the ordering itself.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `dotnet test --filter FullyQualifiedName~ScenicScoreCalculatorTests` (new test class covering: route entirely near scenic ways → score near 1.0; route entirely far → score near 0.0; empty `scenicWays` list → score is 0.0; latitude-correction sanity check at a non-equatorial test latitude, guarding against the degree-delta bug class caught in the prior calibration review)
- Integration tests pass: `dotnet test --filter FullyQualifiedName~RouteQualityTests` (extended: scenic data present and differentiates candidates → higher-scenic candidate wins over higher-pavedRatio candidate; scenic fetch fails/times out → selection falls back to today's paved/smoothness ordering unchanged)
- Full backend test suite passes: `dotnet test`

#### Manual Verification:

- Generate a route in an area with known cycle-route/cycleway tagging (e.g. Warsaw's Wisła riverside paths) and confirm the winning candidate differs from a pre-change run of the same request (seed held constant) in a way attributable to scenic scoring
- Generate a route in a sparsely-tagged rural area and confirm behavior is unchanged from pre-Phase-3 (graceful no-op, per FR-010's accepted sparse-tagging case)

---

## Phase 4: `osmEnriched` observability flag and response contract

### Overview

Surface whether OSM enrichment was actually applied to the winning route, so the best-effort/fallback behavior is testable end-to-end and inspectable via the network tab, without any UI change.

### Changes Required:

#### 1. Add the flag to the winning-route contract

**File**: `src/backend/VeloRoute/Routing/RouteResult.cs`

**Intent**: `RouteResult` is serialized directly as the `/routes/loop` response body — add a field indicating whether either scenic scoring or POI-directed nudging found and used real OSM data for the winning route.

**Contract**: Add `bool OsmEnriched` to the `RouteResult` record's parameter list (after `Segments`). `LoopRouteGenerator` sets it to `true` when the winning candidate's `scenicScore > 0` (from real fetched data, not the empty-list `0.0` case — track this via a separate "scenic data was available" boolean alongside the score rather than inferring it from the score value, matching the "no data vs. zero" distinction from Phase 3) or when that candidate's waypoints were POI-nudged; `false` otherwise.

#### 2. Mirror the field in the frontend type

**File**: `src/frontend/src/types/route.ts`

**Intent**: Keep the TypeScript `RouteResult` interface in sync with the backend contract, per the project's existing 1:1 mirroring convention for this type.

**Contract**: Add `osmEnriched: boolean;` to the `RouteResult` interface (`route.ts:17-23`).

### Success Criteria:

#### Automated Verification:

- Unit/integration tests pass: `dotnet test --filter FullyQualifiedName~LoopRouteIntegrationTests` (extended: response body includes `osmEnriched: true` when fake Overpass data is queued and used; `osmEnriched: false` when Overpass fakes are empty/failing)
- Frontend type check passes: `npm run build` or `npx tsc --noEmit` (from `src/frontend/`)
- Full backend test suite passes: `dotnet test`

#### Manual Verification:

- In the browser network tab, confirm a `/routes/loop` response for a well-tagged area includes `"osmEnriched": true`
- Confirm a response for a sparsely-tagged area or with Overpass misconfigured includes `"osmEnriched": false` and the request still succeeds

---

## Phase 5: Testing, acceptance lock-in, and doc sync

### Overview

Close the loop on the one open PRD question (latency) and the calibration-history lesson (avoid inventing an unvalidated quality threshold) by locking a minimal, presence-based live-smoke assertion; extend existing test coverage; update stale docs per the project's doc-accuracy workflow rule.

### Changes Required:

#### 1. Presence-only live-smoke assertion

**File**: `src/backend/VeloRoute.Tests/Routing/OrsLiveSmokeTests.cs`

**Intent**: Add one new assertion to the existing skipped live-smoke tests (still `[Fact(Skip = "Live ORS/Overpass — run manually")]`, run manually pre-release) proving the Overpass integration is wired correctly, without inventing a subjective "quality improved" threshold — directly addressing the `loop-algorithm-tuning` lesson that an open-ended tuning target risks never being met.

**Contract**: For at least one of the 3 existing Polish test locations (Warsaw is the densest-tagged and most likely to have real data), assert `result.Value!.OsmEnriched == true` — i.e., real OSM data was found and applied at least once, not a specific score value. The existing 4 locked thresholds (pavedRatio ≥0.90, overlap ≤0.40, aspect ≤3.0, distance accuracy ≤15%) remain unchanged and continue to apply to whichever route wins.

#### 2. Doc sync

**File**: `context/foundation/backend/tech-stack.md`

**Intent**: This doc predates ORS and NetTopologySuite entirely and doesn't mention OSM/Overpass — per the project's "keep docs accurate" workflow rule, update it to reflect the new Overpass dependency in the same commit that lands the feature.

**Contract**: Add a short note listing Overpass API as a second external routing-data dependency, alongside the existing ORS mention.

#### 3. Roadmap status update

**File**: `context/foundation/roadmap.md`

**Intent**: Mark S-07 done and move its entry to the Done section, per the pattern used for every other completed slice in this file.

**Contract**: Follow the existing `## Done` section's format (archived-link + one-line lesson) once this change is archived via `/10x-archive`.

### Success Criteria:

#### Automated Verification:

- Full backend test suite passes: `dotnet test` (from `src/backend/`)
- Linting passes: `npm run lint` (from `src/frontend/`, confirms the type-only change in Phase 4 introduces no lint violations)
- Backend build succeeds: `dotnet build`

#### Manual Verification:

- Run the previously-skipped live-smoke tests manually against real ORS + Overpass (`dotnet test --filter FullyQualifiedName~OrsLiveSmokeTests`, with `ORS:ApiKey` set) and confirm the new `OsmEnriched` assertion passes for at least the Warsaw location
- Re-read `context/foundation/backend/tech-stack.md`, root `README.md`, `src/backend/VeloRoute/README.md` for any other now-stale "no OSM integration" or dependency-count claims and update them in this same commit

---

## Testing Strategy

### Unit Tests:

- Overpass query construction and response-to-domain-record mapping (`OverpassClientTests`, mirroring `OrsMapperTests.cs`'s pure-function style)
- `ScenicScoreCalculator`: full-match, no-match, empty-input, and latitude-correction cases
- Bearing-substitution logic in `LoopRouteGenerator` (POI-in-sector → nudged; no POI-in-sector → unchanged; multiple sectors independently resolved)

### Integration Tests:

- `LoopRouteIntegrationTests`: POI lookup succeeds/times out/errors → correct fallback behavior and correct `osmEnriched` value in each case
- `RouteQualityTests`: scenic-tag data present and differentiating → correct candidate selection; scenic-tag data absent → selection unchanged from pre-feature behavior
- Existing 4 quality-threshold assertions (paved ratio, overlap, aspect ratio, distance accuracy) continue passing unmodified — these are the non-regression bar

### Manual Testing Steps:

1. Generate a route in central Warsaw (dense OSM tagging) — confirm `osmEnriched: true` and a visibly different route from a pre-feature baseline
2. Generate a route in a sparsely-tagged rural area — confirm graceful no-op (`osmEnriched: false`, route quality unchanged from v1)
3. Misconfigure `Overpass:BaseUrl` and confirm `/routes/loop` still returns HTTP 200 within the new ~6s combined timeout budget
4. Measure and record actual latency distribution across several real requests to confirm the composed timeout budget is realistic in practice, not just on paper

## Performance Considerations

The composed request timeout (`OpenRouteServiceOptions.TimeoutSeconds + OverpassOptions.PoiLookupTimeoutSeconds`, ~6s by default) is a worst-case ceiling, not a typical latency — the scenic-tag call is parallel and adds no time when it completes before ORS does, and the POI-lookup call only adds its full timeout duration when Overpass is genuinely slow or down. Phase 2 and Phase 5's manual verification steps exist specifically to confirm real-world latency stays well under this ceiling in the common case; if measurement shows otherwise, revisit before considering this slice done rather than shipping an unverified budget.

## Migration Notes

No data migration — route generation remains fully stateless/DB-free. The only wire-contract change is additive (`osmEnriched: boolean` on `RouteResult`), so existing clients ignoring unknown fields are unaffected; the frontend is updated in the same change.

## References

- Research: `context/changes/routing-quality-osm/research.md`
- Prior ORS wiring template: `context/archive/2026-05-30-routing-api-wiring/`
- Prior calibration lessons (latitude-correction bug, deferred-calibration precedent): `context/archive/2026-06-20-loop-algorithm-tuning/calibration.md`, `reviews/impl-review.md`
- Design doc this plan draws mechanisms from: `context/foundation/route-enhancement-ideas.md` (Ideas #3, #6 adapted, #7 newly added and parked)
- `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:58-101` — `SelectBestRoute`, the extension point for `scenicScore`
- `src/backend/VeloRoute/Routing/OverlapDetector.cs` — STRtree spatial-matching pattern reused in `ScenicScoreCalculator`
- `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:72-95,111-190` — fake-client and DI-swap pattern reused for `FakeOverpassClient`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Overpass client foundation

#### Automated

- [x] 1.1 Build succeeds: `dotnet build` — 2925574
- [x] 1.2 Unit tests pass: `dotnet test --filter FullyQualifiedName~OverpassClientTests` — 2925574
- [x] 1.3 Full backend test suite passes: `dotnet test` — 2925574

#### Manual

- [x] 1.4 Manual `FindPoisAsync` call against real Overpass returns at least one POI for central Warsaw — 2925574
- [x] 1.5 Confirm exact Overpass QL query text matches intended tag filters — 2925574

### Phase 2: POI-directed waypoint bearing nudging

#### Automated

- [x] 2.1 Unit tests pass: `dotnet test --filter FullyQualifiedName~LoopRouteGeneratorTests`
- [x] 2.2 Integration tests pass: `dotnet test --filter FullyQualifiedName~LoopRouteIntegrationTests`
- [x] 2.3 Full backend test suite passes: `dotnet test`

#### Manual

- [x] 2.4 Route near real POI cluster visibly passes closer to a POI than pre-change baseline
- [x] 2.5 Overpass-unavailable simulation still returns HTTP 200 with geometric fallback within combined timeout
- [x] 2.6 Manual p95 latency measurement recorded for the composed timeout budget

### Phase 3: Scenic/low-traffic way-tag scoring

#### Automated

- [ ] 3.1 Unit tests pass: `dotnet test --filter FullyQualifiedName~ScenicScoreCalculatorTests`
- [ ] 3.2 Integration tests pass: `dotnet test --filter FullyQualifiedName~RouteQualityTests`
- [ ] 3.3 Full backend test suite passes: `dotnet test`

#### Manual

- [ ] 3.4 Route in well-tagged area differs from pre-change baseline, attributable to scenic scoring
- [ ] 3.5 Route in sparsely-tagged area behaves as graceful no-op

### Phase 4: `osmEnriched` observability flag and response contract

#### Automated

- [ ] 4.1 Integration tests pass: `dotnet test --filter FullyQualifiedName~LoopRouteIntegrationTests`
- [ ] 4.2 Frontend type check passes: `npx tsc --noEmit` (from `src/frontend/`)
- [ ] 4.3 Full backend test suite passes: `dotnet test`

#### Manual

- [ ] 4.4 Network tab shows `osmEnriched: true` for a well-tagged area
- [ ] 4.5 Network tab shows `osmEnriched: false` when Overpass is misconfigured, request still succeeds

### Phase 5: Testing, acceptance lock-in, and doc sync

#### Automated

- [ ] 5.1 Full backend test suite passes: `dotnet test`
- [ ] 5.2 Linting passes: `npm run lint` (from `src/frontend/`)
- [ ] 5.3 Backend build succeeds: `dotnet build`

#### Manual

- [ ] 5.4 Live-smoke tests run manually with real ORS + Overpass; new `OsmEnriched` assertion passes for Warsaw
- [ ] 5.5 Doc staleness check completed across tech-stack.md, root README.md, backend README.md
