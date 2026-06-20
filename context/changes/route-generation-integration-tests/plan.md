# Route Generation Integration Tests — Implementation Plan

## Overview

Add integration tests covering test-plan Phase 2: Risk #2 (distance/overlap constraints) and
Risk #5 (ORS timeout deadline). Tests use `WebApplicationFactory<Program>` with a custom
`IOpenRouteServiceClient` fake — exercising the full in-process pipeline (input validation →
generator → HTTP response mapping) while keeping ORS mocked.

## Current State Analysis

- xUnit 2.9.3 bootstrapped in `src/backend/VeloRoute.Tests/` (F-02 done)
- `InternalsVisibleTo("VeloRoute.Tests")` already set in `VeloRoute.csproj`
- 43 unit tests passing (ORS mapping + GPX serialiser)
- `LoopRouteGenerator` takes `IOpenRouteServiceClient` — mockable at interface level
- 4.5 s deadline is hardcoded in `Program.cs` — must be made configurable for timeout tests
- `Microsoft.AspNetCore.Mvc.Testing` not yet in test project
- `Program` class from top-level statements is `internal sealed` — needs `public partial class Program {}` for `WebApplicationFactory<Program>` to compile

### Key Discoveries

- `LoopRouteGenerator` (`src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:20`) takes `IOpenRouteServiceClient` + `ILogger` — no HTTP dependency; fake at interface level is enough for constraint tests
- Deadline wired in `Program.cs:71` — `new CancellationTokenSource(TimeSpan.FromSeconds(4.5))`; ORS options accessible via `IOptions<OpenRouteServiceOptions>` which is already injected nearby
- `OverlapDetector.ComputeOverlapRatio` (`OverlapDetector.cs:48`) skips segments within 5 index positions (`j <= i + 5`) — synthetic overlap geometry needs ≥ 7 segments apart to trigger the flag
- `RoutingResult<RouteResult>.Failure` + error code `"NO_VALID_RESULT"` maps to HTTP 422 in `Program.cs:85`
- `OperationCanceledException` when `timeoutCts.IsCancellationRequested` maps to HTTP 504 in `Program.cs:93`

## Desired End State

`dotnet test` from `src/backend/` passes with tests for:
- Routes within distance range + ≤ 10% overlap selected successfully (200 OK)
- No route within distance range → 422 NO_VALID_RESULT
- ORS responds after deadline → 504 TIMEOUT returned within 500 ms wall time

Verify: `dotnet test src/backend/` shows ≥ 5 new tests in `VeloRoute.Tests.Routing.LoopRouteIntegrationTests`, all green.

## What We're NOT Doing

- Testing `IOpenRouteServiceClient` HTTP response parsing (covered by F-02 unit tests)
- Calling live ORS (excluded per test-plan §7)
- Testing `WebApplicationFactory` at the HTTP mock boundary (WireMock.Net) — interface-level fake gives cleaner signal for constraint tests and avoids dual-concern failures
- Testing input validation edge cases (MinKm/MaxKm bounds) — that is Program.cs logic, not the generator contract

## Implementation Approach

1. Make the 4.5 s timeout configurable via `ORS:TimeoutSeconds` so tests can set 0.1 s
2. Add `public partial class Program {}` so `WebApplicationFactory<Program>` can reference the entry point
3. Add `Microsoft.AspNetCore.Mvc.Testing` to the test project
4. Write a `FakeOpenRouteServiceClient` (inline in the test file) with configurable per-call responses and optional delay
5. Write a `VeloRouteWebApplicationFactory` that replaces `IOpenRouteServiceClient` with the fake
6. Write tests for Risk #2 and Risk #5 using the factory

## Critical Implementation Details

**`j <= i + 5` adjacency guard in OverlapDetector** — a synthetic out-and-back geometry must have
at least 7 segments between the outbound and return passes, or the overlap segments will be
silently skipped and the ratio will read 0.0. Use a 13-coordinate route (6 outbound + 6 return
segments) to guarantee j ≥ i + 6.

**`IOpenRouteServiceClient` registration is backed by `AddHttpClient`** — in `ConfigureServices`
overrides, first remove the `IOpenRouteServiceClient` descriptor, then add the fake as a
singleton; otherwise the DI container resolves the HttpClient-backed registration first.

**Timeout test wall-time budget** — `ORS:TimeoutSeconds = 0.1` (100 ms); fake delays 500 ms;
assert the HTTP response arrives within 400 ms total. This leaves 300 ms margin and prevents
the test from hanging if cancellation doesn't propagate.

---

## Phase 1: Configurable Deadline

### Overview

Add `TimeoutSeconds` to `OpenRouteServiceOptions`, update `appsettings.json`, wire it into
`Program.cs`, and expose the `Program` type to `WebApplicationFactory`.

### Changes Required

#### 1. OpenRouteServiceOptions — add TimeoutSeconds

**File**: `src/backend/VeloRoute/Routing/OpenRouteServiceOptions.cs`

**Intent**: Add a `TimeoutSeconds` property so the deadline duration can be overridden in test configuration without changing code.

**Contract**: `public double TimeoutSeconds { get; set; } = 4.5;` alongside the existing `BaseUrl` and `ApiKey` properties.

#### 2. appsettings.json — document the default

**File**: `src/backend/VeloRoute/appsettings.json`

**Intent**: Document the default timeout value so ops teams can tune it without source access.

**Contract**: Add `"TimeoutSeconds": 4.5` inside the existing `"ORS"` section.

#### 3. Program.cs — use opts.TimeoutSeconds

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Replace the hardcoded `4.5` with the configured value, and expose `Program` to `WebApplicationFactory`.

**Contract**:
- In the `/routes/loop` endpoint delegate, add `IOptions<OpenRouteServiceOptions> orsOpts` as a parameter alongside the existing `LoopRouteGenerator gen` and `CancellationToken requestCt`
- Change line 71 from `TimeSpan.FromSeconds(4.5)` to `TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds)`
- Add `public partial class Program {}` as the last line of the file (after `app.Run()`)

### Success Criteria

#### Automated Verification

- `dotnet build src/backend/` passes with no errors or warnings
- Existing 43 tests still pass: `dotnet test src/backend/`

#### Manual Verification

- `appsettings.json` has `"TimeoutSeconds": 4.5` inside `"ORS"` block

---

## Phase 2: Test Infrastructure

### Overview

Add `Microsoft.AspNetCore.Mvc.Testing` package and create the shared test infrastructure
(`FakeOpenRouteServiceClient` and `VeloRouteWebApplicationFactory`) inside the integration test file.

### Changes Required

#### 1. VeloRoute.Tests.csproj — add Mvc.Testing

**File**: `src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`

**Intent**: Enable `WebApplicationFactory<Program>` for in-process integration tests.

**Contract**: Add `<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.7" />` inside the existing `<ItemGroup>` with other test packages.

#### 2. LoopRouteIntegrationTests.cs — test infrastructure classes

**File**: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`

**Intent**: Define `FakeOpenRouteServiceClient` and `VeloRouteWebApplicationFactory` as private types within the test file.

**Contract for `FakeOpenRouteServiceClient`**:
- Implements `IOpenRouteServiceClient`
- Exposes a `Queue<RoutingResult<RouteResult>>` property — `GetDirectionsAsync` dequeues one result per call; when the queue is empty it returns `RoutingResult<RouteResult>.Failure(new RoutingError("EMPTY", "no more fake results"))`
- Exposes `TimeSpan Delay { get; set; }` (default `TimeSpan.Zero`) — if set, `GetDirectionsAsync` awaits `Task.Delay(Delay, cancellationToken)` before returning, allowing the CancellationToken to interrupt it

**Contract for `VeloRouteWebApplicationFactory`**:
- Extends `WebApplicationFactory<Program>`
- Constructor accepts optional `string? timeoutSeconds` (default `null` = keep production default)
- In `ConfigureWebHost`:
  1. Calls `builder.ConfigureServices` to remove the `IOpenRouteServiceClient` HttpClient registration and register `FakeClient` as `IOpenRouteServiceClient` singleton
  2. If `timeoutSeconds` is non-null, calls `builder.ConfigureAppConfiguration` to inject `ORS:TimeoutSeconds = timeoutSeconds` via `AddInMemoryCollection`
- Exposes `FakeOpenRouteServiceClient FakeClient { get; } = new()`

### Success Criteria

#### Automated Verification

- `dotnet build src/backend/` passes with no errors

---

## Phase 3: Integration Tests

### Overview

Write tests that exercise the full in-process pipeline for Risk #2 (constraint verification)
and Risk #5 (timeout propagation).

### Changes Required

#### 1. Risk #2 tests — constraint verification

**File**: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`

**Intent**: Verify that `LoopRouteGenerator` enforces distance range and ≤10% overlap constraints through the full HTTP pipeline.

**Contract — test methods to add**:

`PostRoutesLoop_WhenAllCallsReturnValidRoute_Returns200`
- Queue 3 `RouteResult` values, each with `DistanceMeters = 20_000` (within MinKm=15/MaxKm=25) and non-overlapping geometry (simple A→B→C→D→A polygon, no segment within 5 index positions of another parallel segment)
- POST `{"startLon":16.37,"startLat":48.20,"minKm":15,"maxKm":25,"seed":null}`
- Assert HTTP 200 and response body contains `"distanceMeters"`

`PostRoutesLoop_WhenAllCallsReturnOutOfRangeDistance_Returns422`
- Queue 3 `RouteResult` values each with `DistanceMeters = 5_000` (below minKm=15)
- POST same request
- Assert HTTP 422 and response body `code` is `"NO_VALID_RESULT"`

`PostRoutesLoop_WhenAllCallsFailWithProviderError_Returns502`
- Queue 3 `RoutingResult<RouteResult>.Failure(new RoutingError("500", "ORS down"))` results
- POST same request
- Assert HTTP 502

`PostRoutesLoop_WhenSomeCallsReturnHighOverlapInRange_FallsBackTo200`
- Queue 3 `RouteResult` values each with `DistanceMeters = 20_000` and degenerate 13-coordinate out-and-back geometry (overlap ratio ≈ 50%)
- POST same request
- Assert HTTP 200 (fallback path: distance constraint met, overlap relaxed to 40%)

**Out-and-back geometry helper**: construct 13 `RouteCoordinate` values — 7 going east from (16.37, 48.20) in steps of +0.01° longitude, then 6 going back west — yielding 12 segments with segments 6–11 antiparallel to segments 0–5, all pairs ≥ 6 index positions apart.

#### 2. Risk #5 test — timeout propagation

**File**: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`

**Intent**: Verify that a slow ORS client causes the `/routes/loop` endpoint to return 504 within the configured deadline.

**Contract — test method to add**:

`PostRoutesLoop_WhenOrsSlowAndDeadlineFires_Returns504WithinBudget`
- Create factory with `timeoutSeconds = "0.1"` (100 ms)
- Set `FakeClient.Delay = TimeSpan.FromMilliseconds(500)`
- Queue 3 failure results (they won't be reached — cancellation fires first)
- Start a `Stopwatch`; POST request; stop watch after response
- Assert HTTP 504, response body `code` is `"TIMEOUT"`, elapsed < 400 ms

### Success Criteria

#### Automated Verification

- `dotnet test src/backend/` passes; output shows ≥ 5 new tests in `VeloRoute.Tests.Routing.LoopRouteIntegrationTests`, all passing
- All 5 new tests complete in < 3 s total (no hanging — timeout test should take ≈ 100 ms)
- All existing 43 tests still pass

#### Manual Verification

- Run `dotnet test src/backend/ --logger "console;verbosity=normal"` and confirm each test name is listed with `Passed`
- Confirm the timeout test duration in the output is < 400 ms

**Implementation Note**: After completing this phase and verifying all tests pass, pause for manual confirmation before proceeding to docs.

---

## Phase 4: Docs

### Overview

Fill test-plan §6.2 with the cookbook pattern established by Phase 2 and Phase 3, and
update test-plan Phase 2 status.

### Changes Required

#### 1. test-plan.md — fill §6.2 cookbook

**File**: `context/foundation/test-plan.md`

**Intent**: Document the `WebApplicationFactory` + `FakeOpenRouteServiceClient` pattern so future integration tests follow the same shape.

**Contract**: Replace the `TBD — see §3 Phase 2` placeholder in §6.2 with a short description and the key code patterns:
- How to create a `VeloRouteWebApplicationFactory` instance
- How to configure the fake (queue results, set delay)
- How to POST a request and assert the response
- How to configure a short timeout for deadline tests

#### 2. test-plan.md — update Phase 2 status

**File**: `context/foundation/test-plan.md`

**Intent**: Keep the test-plan accurate by marking Phase 2 as shipped.

**Contract**: In §3 Phase rollout table, change Phase 2 `Status` from `not started` to `shipped` and set `Change folder` to `context/changes/route-generation-integration-tests`.

### Success Criteria

#### Automated Verification

- No automated check needed — doc-only change

#### Manual Verification

- §6.2 in `context/foundation/test-plan.md` contains a working example (not a `TBD` placeholder)
- Phase 2 row in §3 table shows `shipped`

---

## Testing Strategy

### Integration Tests

- `PostRoutesLoop_WhenAllCallsReturnValidRoute_Returns200` — primary path: in-range + low overlap
- `PostRoutesLoop_WhenAllCallsReturnOutOfRangeDistance_Returns422` — no candidate in range
- `PostRoutesLoop_WhenAllCallsFailWithProviderError_Returns502` — all ORS calls fail
- `PostRoutesLoop_WhenSomeCallsReturnHighOverlapInRange_FallsBackTo200` — fallback path
- `PostRoutesLoop_WhenOrsSlowAndDeadlineFires_Returns504WithinBudget` — timeout path

### What We Don't Test Here

Per test-plan §7 and §2 anti-patterns:
- ORS response parsing (unit-tested in F-02)
- Specific waypoint coordinate values passed to ORS (implementation mirror)
- Live ORS API calls

## References

- Test-plan Phase 2: `context/foundation/test-plan.md` §3
- Risk #2 + #5 guidance: `context/foundation/test-plan.md` §2 Risk Response Guidance
- Roadmap F-03: `context/foundation/roadmap.md`
- `LoopRouteGenerator`: `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs`
- `OverlapDetector`: `src/backend/VeloRoute/Routing/OverlapDetector.cs`
- Prior test patterns: `src/backend/VeloRoute.Tests/Routing/OrsMapperTests.cs`

---

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Configurable Deadline

#### Automated

- [x] 1.1 `dotnet build src/backend/` passes with no errors or warnings — b97d4c2
- [x] 1.2 Existing 43 tests still pass: `dotnet test src/backend/` — b97d4c2

#### Manual

- [x] 1.3 `appsettings.json` has `"TimeoutSeconds": 4.5` inside `"ORS"` block — b97d4c2

### Phase 2: Test Infrastructure

#### Automated

- [x] 2.1 `dotnet build src/backend/` passes with no errors

### Phase 3: Integration Tests

#### Automated

- [ ] 3.1 `dotnet test src/backend/` passes; ≥ 5 new tests in `LoopRouteIntegrationTests`, all passing
- [ ] 3.2 All 5 new tests complete in < 3 s total
- [ ] 3.3 All existing 43 tests still pass

#### Manual

- [ ] 3.4 Each test listed as `Passed` in `dotnet test --logger "console;verbosity=normal"` output
- [ ] 3.5 Timeout test duration in output is < 400 ms

### Phase 4: Docs

#### Manual

- [ ] 4.1 §6.2 in `test-plan.md` contains working example (not `TBD`)
- [ ] 4.2 Phase 2 row in §3 table shows `shipped`
