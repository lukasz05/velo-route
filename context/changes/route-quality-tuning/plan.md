# Route Quality Tuning — Implementation Plan

## Overview

Fix "spiky"/arbitrary-feeling loop routes by replacing the current pure-DIY
candidate-generation strategy with a combined batch (ORS native `round_trip`
+ existing bearing-based DIY sectors), fixing a scoring bug that makes the
app's advertised "prefer paved, then smooth" ranking largely inactive in
practice, adding a locality-aware spike-detection metric that the existing
aggregate `SmoothnessScore` cannot catch, and surfacing route quality
honestly to the user instead of only logging it server-side.

## Current State Analysis

`LoopRouteGenerator` fires 3 bearing-based DIY candidates per request
(`WaypointCalculator.DestinationPoint` — pure haversine trig, no road-network
awareness) and scores them with a broken two-tier selection: a "primary" path
requiring overlap ≤ 10% (orders by paved → smooth → distance), and a
"fallback" path hit whenever no candidate clears that bar — which, per this
session's live measurement (23 ORS calls, 3 Polish cities), is **most real
requests** (only 2 of 10 DIY candidates cleared 10% overlap). The fallback
path orders by overlap ratio alone, completely ignoring `pavedRatio` and
`smoothnessScore` — so the app's core "prefers paved roads" promise is inert
for the common case. A documented 0.40 overlap ceiling exists only as a test
assertion value and a log-warning string; nothing in production code enforces
it. `SmoothnessCalculator`'s global count-averaged sharp-turn metric dilutes
one severe local spike across hundreds of coordinates, so it cannot catch
"one bad out-and-back" — the exact symptom (#3, user's top priority) this
change targets.

Live testing this session confirmed ORS's native `round_trip` mode has
dramatically better overlap (0.001-0.039 vs. DIY's 0.028-0.384) but overshoots
distance on every sample (13/13, up to +106%), and that a sequential
retry-until-in-range strategy risks ORS rate-limiting (a live 429 was
reproduced after ~20 calls in a short span at one test location). A
single parallel batch mixing both sources — no retries — avoids that risk
while capturing round_trip's overlap advantage whenever it lands in range,
with DIY sectors providing the same structural safety net they provide today.

### Key Discoveries:

- `LoopRouteGenerator.cs:84-88` — fallback path ignores `pavedRatio`/
  `smoothnessScore`; this is the more severe, previously-undocumented
  explanation for "roads feel arbitrary" (research finding, not in the
  original frame).
- `LoopRouteGenerator.cs:92-93` — the 0.40 overlap ceiling is a log message
  only; no `.Where()` filter or rejection logic exists anywhere for it.
- `RouteResult.cs:3-10` — `PavedRatio`/`SmoothnessScore` are computed
  get-only properties that System.Text.Json serializes automatically;
  `Program.cs:350` returns `Results.Ok(result.Value)` with **no wrapper
  DTO** — any new field added the same way (a computed property on
  `RouteResult`) reaches the HTTP response and the frontend
  `RouteResult` type (`src/frontend/src/types/route.ts:17-23`) with zero
  additional plumbing.
- `OverlapDetector.ComputeOverlapRatio` and `SmoothnessCalculator`'s
  per-index bearing-delta loop are both already-computed, reusable building
  blocks — no new geometry math needed for the spike metric or the quality
  flag, only new aggregation/exposure of data already produced.
- `TestInfrastructure.cs:72-95` — `FakeOpenRouteServiceClient` dequeues from
  one shared `ConcurrentQueue<RoutingResult<RouteResult>>` regardless of
  which client method is called; extending the interface with a new
  round_trip method and having the fake dequeue from the same queue keeps
  every existing "enqueue N results" test pattern working unchanged (just
  with N=6 instead of N=3).
- Live measurement this session (not in prior research): a single parallel
  batch of 3-5 round_trip seeds completes in 0.15-1.0s wall time (well inside
  the 4.5s budget), but hit-rate for landing in the target `[min,max]` window
  is highly location-dependent (40-100% at Warsaw/Mazury, 0-20% at coastal
  Gdynia), and a *second* retry batch triggered `HTTP 429` — retries are a
  real rate-limit liability, not just an unmeasured cost.

## Desired End State

A loop-route request fires one parallel batch of 3 `round_trip` candidates
and 3 DIY-sector candidates (6 ORS calls total, same latency profile as
today's 3-call batch per this session's measurement). `SelectBestRoute` uses
one consistent paved → smooth → spike-freedom → distance ordering regardless
of whether a candidate clears the 10% overlap bar, so the "prefers paved
roads" behavior is active for every request, not just the ~20% that clear
the strict bar today. Every response carries `overlapRatio` and a
`qualityWarning` boolean (true when overlap exceeds 0.40), replacing the
log-only warning; the frontend shows a non-blocking notice when
`qualityWarning` is true. A new `maxConsecutiveSharpTurns` metric penalizes
one severe local spike that the old aggregate `smoothnessScore` couldn't see.

Verification: re-run this session's live smoke tests (3 cities, 25km +
90km) and confirm overlap ratios and spike counts improve versus the
baseline numbers captured in `research.md`, without regressing distance
accuracy or paved ratio.

### Key Discoveries:

(see Current State Analysis above — consolidated there per this plan's
research depth)

## What We're NOT Doing

- OSM/Overpass scenic-scoring and POI-proximity work (`routing-quality-osm`,
  roadmap S-07) — separately parked for Overpass reliability reasons, and
  the frame confirmed it doesn't explain the spike symptom this change
  targets.
- Sequential retry-until-in-range for `round_trip` seeds — measured this
  session to risk ORS rate-limiting; a single parallel batch is used
  instead, with no retry loop.
- Distributing requests across multiple public ORS instances/API keys for
  rate-limit headroom — explicitly flagged by the user as a later, separate
  decision.
- Increasing `BearingCount` beyond today's 3 DIY sectors — superseded by
  adding `round_trip` as a second candidate source instead.
- Hard-rejecting requests whose best candidate exceeds the 0.40 overlap
  ceiling — the user chose a response-level quality flag (best-effort) over
  a hard failure.
- Post-hoc POI-directed reroute-through insertion (`route-enhancement-ideas.md`
  idea #8) — captured and deferred during framing, unrelated to this
  change's scope.
- Load-testing ORS rate limits under concurrent multi-user traffic — flagged
  as an open risk (see below), would require sustained load testing outside
  this change's scope.
- Any change to `WaypointCalculator`'s bearing math or the DIY sectors
  themselves — they're reused as-is as one of the two candidate sources.

## Implementation Approach

Four phases, each independently testable: (1) teach the ORS client to speak
`round_trip`, proven via request-capturing unit tests since no existing test
inspects outbound request shape; (2) wire `round_trip` into the candidate
batch and fix the fallback-ordering bug + add the real overlap ceiling as a
response-level flag; (3) add the locality-aware spike metric research
identified as the only metric that actually targets the user's top-priority
symptom; (4) surface the new quality signal to the user and close the loop
with docs and a live re-validation against this session's baseline.

## Critical Implementation Details

### Round_trip pre-compensation and seeding

`round_trip` overshoots distance on every live sample (mean ≈ +50%). Send
`length = targetMidMeters * 0.70` (splits the difference between this
session's 0.75 test and prior research's 0.65 test — both narrowed but did
not eliminate variance) as the starting compensation factor; Phase 2's live
manual verification re-measures and adjusts this constant if needed, mirroring
how `PrimaryOverlapThreshold`/ceiling were originally calibrated
(`calibration.md`). Seeds must stay reproducible when the request supplies
`seed`: use `[seed, seed+1, seed+2]` when provided, else a fixed `[1, 2, 3]`
default — extending the existing seed parameter's effect (today it only
offsets DIY bearing) to also seed `round_trip`, rather than introducing an
unrelated second seeding scheme.

### Total-candidate-failure error aggregation

`LoopRouteGenerator.cs:98` returns only the *first* error when every
candidate fails (`results.FirstOrDefault(r => !r.IsSuccess)`), discarding the
rest — a pre-existing minor gap that becomes more visible now that a batch
has 6 candidates with two distinct failure modes (DIY's structural 404s at
coastal/edge locations, `round_trip`'s occasional seed-dependent 500).
`Program.cs:340-346` switches on `Error.Code` to pick an HTTP status — when
aggregating, keep the **first failure's `Code`** for that routing to stay
correct, and concatenate all failures' `Message` values for observability
only.

### Spike metric shares data with the existing smoothness calculator

`SmoothnessCalculator.Compute` already computes a per-index sharp-turn
boolean (bearing delta > 90°) that it currently only averages. Extract that
per-index computation into a shared internal helper so the new
`SpikeDetector` (longest consecutive run of sharp-turn flags) doesn't
duplicate the bearing/haversine trig — both should read from one loop over
`route.Geometry.Coordinates`.

## Phase 1: ORS round_trip client support

### Overview

Add `round_trip` request capability to `OpenRouteServiceClient` behind a new
interface method, so `LoopRouteGenerator` can request round_trip candidates
the same way it requests DIY ones. No candidate-generation behavior changes
yet — this phase is pure plumbing, proven correct via request-shape tests.

### Changes Required:

#### 1. `IOpenRouteServiceClient` — new round_trip method

**File**: `src/backend/VeloRoute/Routing/IOpenRouteServiceClient.cs`

**Intent**: Add a distinct method for round_trip requests rather than
overloading the existing `waypoints`-based method, per research's finding
that overloading "waypoints" to sometimes mean "just the start" is not
self-documenting.

**Contract**: `Task<RoutingResult<RouteResult>> GetRoundTripDirectionsAsync(RouteCoordinate start, OrsRoundTripOptions roundTrip, OrsDirectionOptions? options = null, CancellationToken cancellationToken = default)`.

#### 2. Round_trip options record

**File**: `src/backend/VeloRoute/Routing/OrsDirectionOptions.cs`

**Intent**: Public, immutable parameter object for round_trip's three ORS
fields, following the existing `OrsDirectionOptions` record pattern in the
same file.

**Contract**: `public sealed record OrsRoundTripOptions(int LengthMeters, int Points, int Seed);`

#### 3. `OpenRouteServiceClient` implementation

**File**: `src/backend/VeloRoute/Routing/OpenRouteServiceClient.cs`

**Intent**: Implement `GetRoundTripDirectionsAsync` by building an
`OrsDirectionsRequest` with a single-coordinate `Coordinates` array
(`[[start.Longitude, start.Latitude]]`) and a `round_trip` object nested
under `options`, reusing the existing `avoid_features`/`profile_params`
building logic from the waypoints overload (extract that shared bit into a
private helper rather than duplicating it) and the same error-handling /
`MapToRouteResult` path already used for the waypoints overload.

**Contract**: Add a file-scoped `OrsRoundTrip` DTO (`length`, `points`,
`seed` JSON properties) and a `round_trip` property on the existing
file-scoped `OrsOptions` class (`[JsonIgnore(Condition = WhenWritingNull)]`,
matching the existing nullable-field pattern in that class).

#### 4. Test double

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: `FakeOpenRouteServiceClient` must implement the new interface
method; it dequeues from the same `Results` queue as the existing methods so
no existing test-authoring pattern changes.

**Contract**: New method with the same dequeue-or-fail body as the existing
two overloads.

#### 5. Request-shape unit tests

**File**: `src/backend/VeloRoute.Tests/Routing/OpenRouteServiceClientTests.cs` (new file)

**Intent**: Close the gap research explicitly flagged — today, no test
inspects what's actually sent to ORS. Use a custom `HttpMessageHandler` that
captures the outgoing `HttpRequestMessage` body and asserts on its parsed
JSON.

**Contract**: At minimum, one test asserting a `round_trip` call's JSON body
has `coordinates` as a single `[lon, lat]` pair and `options.round_trip` with
correct `length`/`points`/`seed`, and one test asserting a waypoints call's
JSON body has the expected multi-point `coordinates` array and no
`round_trip` key (`JsonIgnore` working as expected).

### Success Criteria:

#### Automated Verification:

- Backend builds cleanly: `dotnet build` (from `src/backend/`)
- New and existing unit tests pass: `dotnet test --filter "FullyQualifiedName~OpenRouteServiceClientTests"`
- Full backend test suite still passes: `dotnet test` (from `src/backend/`)

#### Manual Verification:

- One-off manual live call (e.g. via the existing `ors_retry_measure.py`-style script or `curl`) confirms a `round_trip` request built via the new client method returns a 200 with the expected geometry shape against the real ORS endpoint.

---

## Phase 2: Combined-batch generation + fixed selection + real overlap ceiling

### Overview

Wire `round_trip` into `LoopRouteGenerator`'s candidate batch alongside the
existing DIY sectors, fix the fallback-ordering bug so paved/smoothness
preference is active for every request, and turn the 0.40 overlap ceiling
into a real, response-visible signal instead of a log-only warning.

### Changes Required:

#### 1. Combined candidate batch

**File**: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`

**Intent**: `FetchCandidatesAsync` fires 3 `round_trip` calls (length =
`targetMidMeters * 0.70`, `points = 5`, seeds per the seeding rule in
Critical Implementation Details) alongside the existing 3 DIY-sector calls,
all in one `Task.WhenAll` — no retries, no sequential batches, per the
measured rate-limit risk.

**Contract**: `FetchCandidatesAsync` returns the same
`Task<RoutingResult<RouteResult>[]>` shape (now length 6 instead of 3);
`SelectBestRoute`'s signature and candidate-shaping logic are otherwise
unaffected by which source produced a given `RouteResult`.

#### 2. Unified selection ordering

**File**: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`

**Intent**: Replace the current two-tier ordering (primary: paved → smooth →
distance; fallback: overlap-only) with two buckets that share one ordering
function — try the strict-overlap bucket (≤ `PrimaryOverlapThreshold`)
first, then all in-range candidates — so paved/smoothness preference is
active in both cases. Insert the new spike metric (`MaxConsecutiveSharpTurns`,
from Phase 3) as a tie-break after `smoothnessScore` and before
distance-closeness in both buckets.

**Contract**: Both buckets use
`.OrderByDescending(pavedRatio).ThenByDescending(smoothnessScore).ThenBy(maxConsecutiveSharpTurns).ThenBy(distanceCloseness)`;
only the bucket's overlap-ratio filter differs.

#### 3. Real overlap ceiling as a response-level flag

**File**: `src/backend/VeloRoute/Routing/RouteResult.cs`

**Intent**: Expose the already-computed overlap ratio and a derived quality
flag as computed properties on `RouteResult`, following the exact pattern
`PavedRatio`/`SmoothnessScore` already use — since `Program.cs:350` returns
`Results.Ok(result.Value)` with no wrapper DTO, this is the only change
needed to make the field reach the HTTP response.

**Contract**: `public double OverlapRatio => OverlapDetector.ComputeOverlapRatio(Geometry.Coordinates);` and `public bool QualityWarning => OverlapRatio > OverlapDetector.Ceiling;`. Promote the 0.40 figure from a log-message literal to a `public const double Ceiling = 0.40;` on `OverlapDetector`, replacing the duplicate literal in the log-warning call.

#### 4. Total-failure error aggregation

**File**: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`

**Intent**: When no candidate succeeds or clears the distance window,
surface all failure reasons, not just the first — per Critical
Implementation Details, keep the first failure's `Code` for HTTP-status
routing and concatenate messages.

**Contract**: `RoutingResult<RouteResult>.Failure` call's `Message` becomes
a join of all failed candidates' messages; `Code` stays the first failure's
code.

#### 5. Update existing tests for 6-candidate batches

**File**: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`, `src/backend/VeloRoute.Tests/Routing/RouteQualityTests.cs`

**Intent**: These tests enqueue a fixed number of `FakeOpenRouteServiceClient`
results per request (currently 3) and assert on the resulting `RouteResult`
JSON — update the enqueue counts to 6 and add assertions for the new
`overlapRatio`/`qualityWarning` fields where relevant (e.g. a new test
enqueuing 6 high-overlap candidates and asserting `qualityWarning: true`).

**Contract**: No structural test-pattern change — same
enqueue-then-POST-then-assert shape, just N=6 instead of N=3, plus new
assertions for the two new fields.

### Success Criteria:

#### Automated Verification:

- Backend builds cleanly: `dotnet build`
- Updated integration/quality tests pass: `dotnet test --filter "FullyQualifiedName~LoopRouteIntegrationTests|FullyQualifiedName~RouteQualityTests"`
- New unit tests for the unified ordering and overlap-ceiling flag pass (added to `RouteQualityTests.cs` or a new file)
- Full backend test suite passes: `dotnet test`

#### Manual Verification:

- Live smoke test at all 3 cities (`OrsLiveSmokeTests`, run manually per its own doc comment) shows `qualityWarning: false` for the large majority of requests, and confirms the 0.70 pre-compensation constant lands most `round_trip` candidates in-range; adjust the constant if live data suggests otherwise.
- Manually confirm via logs/response that a request whose best candidate still exceeds the 0.40 ceiling returns 200 with `qualityWarning: true` rather than failing.

---

## Phase 3: Locality-aware spike metric

### Overview

Add the metric research identified as the only one that actually targets
symptom #3 (spiky shape, the user's top priority) — a locality-aware measure
that a single severe local spike cannot hide inside, unlike the existing
global count-averaged `SmoothnessScore`.

### Changes Required:

#### 1. Shared per-index sharp-turn extraction

**File**: `src/backend/VeloRoute/Routing/SmoothnessCalculator.cs`

**Intent**: Extract the existing per-index bearing-delta/sharp-turn-flag
loop into a shared internal method so `SpikeDetector` doesn't duplicate the
trig, per Critical Implementation Details.

**Contract**: `internal static bool[] ComputeSharpTurnFlags(RouteResult route)` returning one flag per index `i` in `[0, coords.Count - 2)`; `Compute` becomes a thin average over this array.

#### 2. Spike detector

**File**: `src/backend/VeloRoute/Routing/SpikeDetector.cs` (new file)

**Intent**: Compute the longest run of consecutive sharp-turn flags — the
locality-aware signal that dilution-prone averaging cannot provide.

**Contract**: `internal static class SpikeDetector { public static int Compute(RouteResult route); }`, using `SmoothnessCalculator.ComputeSharpTurnFlags` internally.

#### 3. Expose on `RouteResult` and wire into scoring

**File**: `src/backend/VeloRoute/Routing/RouteResult.cs`, `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`

**Intent**: Add the metric to the response (same computed-property pattern
as `OverlapRatio`) and use it as the tie-break key specified in Phase 2's
unified ordering.

**Contract**: `public int MaxConsecutiveSharpTurns => SpikeDetector.Compute(this);` on `RouteResult`.

#### 4. Synthetic-fixture unit tests

**File**: `src/backend/VeloRoute.Tests/Routing/SpikeDetectorTests.cs` (new file)

**Intent**: Deterministic tests using synthetic coordinate fixtures with
known spike patterns (e.g. a route with one isolated 3-point out-and-back
amid an otherwise smooth loop) — no live ORS calls needed, unlike the
threshold-calibration step below.

**Contract**: At minimum, assert `Compute` returns 0 for a fully smooth
loop, and returns the correct run length for a fixture with a known
consecutive-sharp-turn run.

### Success Criteria:

#### Automated Verification:

- Backend builds cleanly: `dotnet build`
- New `SpikeDetectorTests` pass: `dotnet test --filter "FullyQualifiedName~SpikeDetectorTests"`
- Full backend test suite passes: `dotnet test`

#### Manual Verification:

- Run `OrsLiveSmokeTests` at all 3 cities and record `maxConsecutiveSharpTurns` for the selected route at each; compare against a route captured under the pre-Phase-2 (DIY-only) code path to confirm the new combined-batch + spike-aware ordering reduces or eliminates high consecutive-run counts, calibrating any downstream threshold this data suggests is worth locking (mirrors the project's existing `calibration.md` pattern).

---

## Phase 4: Surfacing + docs sync

### Overview

Close the loop: surface the new quality signal to the user, keep project
docs accurate per this repo's own convention, and re-validate live against
this session's baseline to confirm the overall change achieved its goal.

### Changes Required:

#### 1. Frontend type extension

**File**: `src/frontend/src/types/route.ts`

**Intent**: Mirror the three new backend `RouteResult` fields so the
frontend type stays accurate — the backend serializes them automatically
regardless of whether the frontend type declares them.

**Contract**: Add `overlapRatio: number; qualityWarning: boolean; maxConsecutiveSharpTurns: number;` to the `RouteResult` interface (`route.ts:17-23`).

#### 2. Quality-warning banner

**File**: `src/frontend/src/components/RouteInfoPanel.tsx`

**Intent**: Show a non-blocking notice when `route.qualityWarning` is true,
placed near the existing "Surface quality" line. Use a distinct visual
treatment (e.g. amber, not the existing red used for `saveError`/
`downloadError`) so users don't confuse a soft quality notice with a hard
failure.

**Contract**: One conditional paragraph following the existing
`saveError`/`downloadError` conditional-render pattern already in this
component (lines 137-139, 149-151).

#### 3. Docs sync

**File**: `context/foundation/loop-route-algorithm.md`, `context/foundation/roadmap.md`

**Intent**: Per this repo's workflow convention, update the v1 decision
record that rejected `round_trip` outright (now superseded by this change's
combined-batch approach) and any roadmap status referencing the resolved
spike/shape-quality risk.

**Contract**: Add a dated addendum to `loop-route-algorithm.md` noting the
combined-batch decision and linking to this change; update `roadmap.md`'s
relevant entry status if it currently references this open risk.

### Success Criteria:

#### Automated Verification:

- Frontend type-checks: `npm run build` (or `tsc --noEmit` if configured) from `src/frontend/`
- Frontend lints cleanly: `npm run lint` from `src/frontend/`
- Frontend unit tests pass: `npm test` from `src/frontend/`

#### Manual Verification:

- Generate a route in the browser (dev server) for a request expected to trigger `qualityWarning` (e.g. a very tight km range at a sparse-road location) and confirm the banner renders correctly and unobtrusively.
- Re-run all 3 `OrsLiveSmokeTests` locations at both a narrow (20-30km) and wide (80-100km) range; compare `overlapRatio`, `pavedRatio`, `maxConsecutiveSharpTurns`, and distance accuracy against the baseline numbers recorded in `research.md` and confirm net improvement with no regression.
- Confirm docs no longer describe `round_trip` as rejected/unused, and that no stale "3 candidates" or "DIY-only" claims remain in `context/foundation/` or component READMEs per this repo's doc-accuracy convention.

---

## Testing Strategy

### Unit Tests:

- Request-shape capture tests for both `round_trip` and waypoints ORS calls (Phase 1)
- Unified-ordering and overlap-ceiling-flag tests covering: strict-bucket win, fallback-bucket-now-respects-paved/smoothness, and quality-flag true/false boundary (Phase 2)
- Synthetic-fixture spike-detector tests: smooth loop (0), isolated spike (known run length), multiple separated spikes (longest run, not sum) (Phase 3)

### Integration Tests:

- `LoopRouteIntegrationTests`/`RouteQualityTests` updated for 6-candidate batches, including a case where only `round_trip` candidates clear the distance window and a case where only DIY candidates do (Phase 2)
- Frontend: no new integration tests planned beyond existing component tests; the banner is exercised via the existing `RouteInfoPanel` test file if present, else a new focused test

### Manual Testing Steps:

1. Run all 3 `OrsLiveSmokeTests` locations before starting Phase 2 (baseline is already captured in `research.md` — no need to re-run pre-change).
2. After Phase 2, re-run the same 3 locations and confirm `qualityWarning` is false for the large majority and the 0.70 pre-compensation constant is landing round_trip candidates in-range at an acceptable rate; tune if not.
3. After Phase 3, re-run and record `maxConsecutiveSharpTurns`; confirm no severe single-spike routes are being selected when a smoother alternative exists in the batch.
4. After Phase 4, generate a route in the browser end-to-end (dev server) and confirm the GPX export and save-to-library flows still work unchanged with the new `RouteResult` fields present.

## Performance Considerations

Candidate batch grows from 3 to 6 parallel ORS calls per request. Per this
session's live measurement, wall time for a 3-6 seed parallel batch stayed
under 1.1s even at the slowest tested location — well inside the existing
4.5s (`OpenRouteServiceOptions.TimeoutSeconds`) budget, since `Task.WhenAll`
latency is bounded by the slowest single call, not the sum. Call *volume*
doubles, which increases exposure to ORS rate limits under concurrent
multi-user load — an open risk (below), not mitigated in this change.

## Migration Notes

None. `RouteResult` is an in-memory DTO for the `/routes/loop` response —
it is not persisted to Postgres (saved routes use `SaveRouteRequest`/
`Data.Route` with raw coordinates, unaffected by this change). No EF Core
migration is needed.

## Open Risks & Assumptions

- **ORS rate limits under real concurrent load are still unmeasured.** This
  session confirmed a 429 is reachable with ~20 calls in a short span from a
  single client; doubling per-request call volume (3→6) increases exposure
  under multi-user production traffic, but no documented ORS concurrency
  limit exists for this repo's plan tier, and sustained load testing is out
  of scope for this change. If this becomes a real problem post-launch, the
  parked "distribute across multiple ORS instances/keys" idea is the
  documented next step.
- **The 0.70 pre-compensation factor and `points=5` are starting defaults**,
  not fully calibrated constants — Phase 2's manual verification explicitly
  re-measures and may adjust them, consistent with this project's existing
  calibration pattern (`calibration.md` did the same for the 0.10/0.40
  overlap thresholds).
- **Coastal/edge-of-network locations (e.g. Gdynia) may still produce
  `qualityWarning: true` responses more often than inland locations** —
  `round_trip` measured 0-20% in-range hit rate there, and DIY has its own
  structural 404s at the same bearings. This change reduces but does not
  eliminate quality variance at such locations; a full fix would need
  road-network-aware waypoint placement, which is out of scope here.

## Success Criteria (Summary)

- Every `/routes/loop` response reflects one consistent paved → smooth →
  spike-free → distance ordering, regardless of which overlap bucket the
  winning candidate falls in.
- The 0.40 overlap ceiling is a real, response-visible signal
  (`qualityWarning`), not a log-only warning.
- A locality-aware spike metric exists and demonstrably distinguishes a
  smooth loop from one with a severe local out-and-back, closing the
  measurement gap that made symptom #3 previously unverifiable by automated
  means.
- Live re-validation against this session's 3-city baseline shows improved
  overlap/spike numbers with no regression in distance accuracy or paved
  ratio.

## References

- Frame: `context/changes/route-quality-tuning/frame.md`
- Research: `context/changes/route-quality-tuning/research.md`
- Prior v1 decision (superseded in Phase 4): `context/foundation/loop-route-algorithm.md`
- Prior calibration precedent: `context/archive/2026-06-20-loop-algorithm-tuning/calibration.md`
- Code: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`, `SmoothnessCalculator.cs`, `OverlapDetector.cs`, `OpenRouteServiceClient.cs`, `OrsDirectionOptions.cs`, `RouteResult.cs`, `IOpenRouteServiceClient.cs`
- Tests: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`, `LoopRouteIntegrationTests.cs`, `RouteQualityTests.cs`, `OrsLiveSmokeTests.cs`
- Frontend: `src/frontend/src/types/route.ts`, `src/frontend/src/components/RouteInfoPanel.tsx`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: ORS round_trip client support

#### Automated

- [x] 1.1 Backend builds cleanly
- [x] 1.2 New request-shape unit tests pass
- [x] 1.3 Full backend test suite passes

#### Manual

- [x] 1.4 One-off manual live round_trip call via new client method succeeds

### Phase 2: Combined-batch generation + fixed selection + real overlap ceiling

#### Automated

- [ ] 2.1 Backend builds cleanly
- [ ] 2.2 Updated integration/quality tests pass
- [ ] 2.3 New unified-ordering and overlap-ceiling unit tests pass
- [ ] 2.4 Full backend test suite passes

#### Manual

- [ ] 2.5 Live smoke test at 3 cities shows qualityWarning false for large majority; pre-compensation constant tuned if needed
- [ ] 2.6 Manually confirm high-overlap request returns 200 with qualityWarning true instead of failing

### Phase 3: Locality-aware spike metric

#### Automated

- [ ] 3.1 Backend builds cleanly
- [ ] 3.2 New SpikeDetectorTests pass
- [ ] 3.3 Full backend test suite passes

#### Manual

- [ ] 3.4 Live smoke test at 3 cities: record and compare maxConsecutiveSharpTurns vs. pre-Phase-2 baseline

### Phase 4: Surfacing + docs sync

#### Automated

- [ ] 4.1 Frontend type-checks
- [ ] 4.2 Frontend lints cleanly
- [ ] 4.3 Frontend unit tests pass

#### Manual

- [ ] 4.4 Browser verification of quality-warning banner rendering
- [ ] 4.5 Live re-validation at 3 cities/2 ranges vs. research.md baseline
- [ ] 4.6 Confirm docs no longer describe round_trip as rejected/unused or reference stale "3 candidates"/"DIY-only" claims
