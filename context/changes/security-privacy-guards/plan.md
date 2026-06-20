# Security and Privacy Guards — Implementation Plan

## Overview

Add two integration tests covering the PRD privacy NFR: (a) no input coordinate values appear
in backend logs after a route-generation request completes (Risk #4), and (b) no API key string
appears in the error response body when ORS returns an error (Risk #6). These are regression
guards — the current code likely passes both — but they lock the floor against future log-level
changes or error-path refactors.

## Current State Analysis

- `FakeOpenRouteServiceClient` and `VeloRouteWebApplicationFactory` are `file sealed` in
  `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs:10-67`. The `file`
  modifier makes them inaccessible to a second test file.
- No log-capture infrastructure exists in the test project.
- `appsettings.json` and `appsettings.Development.json` both set `"Default": "Information"`;
  the .NET HTTP client does not emit request bodies at this level — coordinate leakage is
  unlikely but not tested.
- API key flows as `Authorization` header on `HttpClient` (`Program.cs:28-33`). Error mapping
  in `Program.cs:73-96` returns only internal error codes (`"PROVIDER_ERROR"`, `"NO_ROUTE"`,
  etc.) — the ORS error *message* is dropped before it reaches the caller. Key leakage via the
  normal error path is unlikely but not tested.
- `§6.3` in `context/foundation/test-plan.md` is marked "TBD — see §3 Phase 3".

## Desired End State

`dotnet test` from `src/backend/` runs two new tests in
`src/backend/VeloRoute.Tests/Routing/SecurityPrivacyIntegrationTests.cs`:

1. `PostRoutesLoop_WhenRequestCompletes_LogsContainNoCoordinates` — passes a known start point,
   captures all `ILogger` output, asserts neither `startLon` nor `startLat` values appear in
   any log entry.
2. `PostRoutesLoop_WhenOrsErrorContainsApiKey_ResponseBodyDoesNotExposeKey` — configures a
   sentinel API key, enqueues a failure whose error message contains that key, asserts the
   response body string does not contain the sentinel.

`§6.3` in `test-plan.md` is filled with the log-capture and key-leakage patterns.

### Key Discoveries

- `FakeOpenRouteServiceClient:10-33` and `VeloRouteWebApplicationFactory:35-67` in
  `LoopRouteIntegrationTests.cs` are `file sealed` — must move to a shared file.
- `Microsoft.Extensions.Diagnostics.Testing` provides `FakeLogCollector`; registered via
  `builder.ConfigureLogging(l => l.AddFakeLogging())` in `ConfigureWebHost`; retrieved via
  `factory.Services.GetRequiredService<FakeLogCollector>()`.
- The factory already accepts `timeoutSeconds` as a constructor parameter and injects it via
  `AddInMemoryCollection`; `apiKey` follows the same pattern.
- Risk #6 test strategy: enqueue `RoutingResult.Failure(new RoutingError("PROVIDER_ERROR",
  "message containing sentinel"))` — simulates an ORS error body that echoes the key back.
  The assertion is that `Program.cs:73-96` strips the ORS message and the sentinel never
  reaches the HTTP response.

## What We're NOT Doing

- Testing `POST /routes/gpx` for coordinate logging — that endpoint calls no external service;
  coordinate presence in GPX output is expected and intentional.
- Testing at the real HTTP client level (no `HttpMessageHandler` mock) — the `FakeClient`
  approach tests the error-mapping path where key leakage is most plausible, and follows the
  established pattern from Phase 2.
- Testing that log level configuration is set correctly — the anti-pattern per `test-plan.md`
  §2 Risk #4 guidance; we assert coordinates do not appear in captured output, not that a level
  is configured.
- Modifying production code — both tests are expected to pass against the current backend; if
  either fails, that indicates a real leak requiring a separate fix.

## Implementation Approach

Move shared test infrastructure to a new file (dropping `file` scoping), extend the factory
with the two new parameters (`apiKey`, `useFakeLogging`), then write the two security tests in
their own file. Phase 1 is pure refactor with no behaviour change — existing tests must still
pass. Phase 2 adds the new tests and fills the cookbook.

## Phase 1: Extract Shared Test Infrastructure

### Overview

Move `FakeOpenRouteServiceClient` and `VeloRouteWebApplicationFactory` out of
`LoopRouteIntegrationTests.cs` (where they are `file`-scoped) into a new shared file. Extend
the factory with `apiKey` and `useFakeLogging` constructor parameters. Existing tests must
remain green.

### Changes Required

#### 1. Add NuGet package

**File**: `src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`

**Intent**: Add `Microsoft.Extensions.Diagnostics.Testing` so the factory can register
`FakeLogCollector`.

**Contract**: Add an `<ItemGroup>` `<PackageReference>` entry for
`Microsoft.Extensions.Diagnostics.Testing`. Use the latest stable version compatible with
.NET 10 (check `dotnet add package` for the current version).

#### 2. Create shared infrastructure file

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: Provide `FakeOpenRouteServiceClient` and `VeloRouteWebApplicationFactory` as
`internal sealed` types accessible to all test files in the project.

**Contract**: Copy the two types verbatim from `LoopRouteIntegrationTests.cs:10-67`, remove
the `file` modifier, change visibility to `internal sealed`. Extend
`VeloRouteWebApplicationFactory` constructor to accept `string? apiKey = null` and
`bool useFakeLogging = false` in addition to the existing `string? timeoutSeconds = null`.
In `ConfigureWebHost`:

- If `apiKey is not null`: inject `["ORS:ApiKey"] = apiKey` via `AddInMemoryCollection`
  alongside the existing `timeoutSeconds` injection.
- If `useFakeLogging`: call `builder.ConfigureLogging(l => l.AddFakeLogging())`.

Add `using Microsoft.Extensions.Diagnostics.Testing;` and
`using Microsoft.Extensions.Logging;`.

#### 3. Remove moved types from LoopRouteIntegrationTests.cs

**File**: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`

**Intent**: Delete the `file sealed` definitions of `FakeOpenRouteServiceClient` and
`VeloRouteWebApplicationFactory` from lines 10-67 now that they live in
`TestInfrastructure.cs`. No test logic changes.

### Success Criteria

#### Automated Verification

- Build succeeds: `dotnet build src/backend/VeloRoute.sln`
- All existing tests still pass: `dotnet test src/backend/VeloRoute.sln`

#### Manual Verification

- No new warnings about duplicate type definitions.

**Implementation Note**: After Phase 1 automated verification passes, proceed directly to
Phase 2 — no manual gate required here since this is a pure structural refactor.

---

## Phase 2: Security and Privacy Integration Tests

### Overview

Create `SecurityPrivacyIntegrationTests.cs` with two tests covering Risk #4 (coordinate
logging) and Risk #6 (API key leakage). Fill §6.3 in `test-plan.md`.

### Changes Required

#### 1. Create security test file

**File**: `src/backend/VeloRoute.Tests/Routing/SecurityPrivacyIntegrationTests.cs`

**Intent**: Implement the two security integration tests using the shared factory from Phase 1.

**Contract**:

*Risk #4 — coordinate logging test*

```csharp
[Fact]
public async Task PostRoutesLoop_WhenRequestCompletes_LogsContainNoCoordinates()
{
    await using var factory = new VeloRouteWebApplicationFactory(useFakeLogging: true);
    // Enqueue a valid result so the request completes successfully
    var coords = new RouteCoordinate[]
    {
        new(16.37, 48.20), new(16.38, 48.21),
        new(16.39, 48.20), new(16.37, 48.20),
    };
    for (int i = 0; i < 3; i++)
        factory.FakeClient.Results.Enqueue(
            RoutingResult<RouteResult>.Success(
                new RouteResult(new RouteGeometry(coords), 20_000, [])));

    var client = factory.CreateClient();
    var response = await client.PostAsync(
        "/routes/loop",
        new StringContent(
            """{"startLon":16.37,"startLat":48.20,"minKm":15,"maxKm":25,"seed":null}""",
            System.Text.Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var collector = factory.Services.GetRequiredService<FakeLogCollector>();
    var logText = string.Join("\n", collector.GetSnapshot().Select(e => e.Message));
    Assert.DoesNotContain("16.37", logText);
    Assert.DoesNotContain("48.20", logText);
}
```

*Risk #6 — API key leakage test*

```csharp
private const string TestApiKeySentinel = "TEST-SENTINEL-KEY-F04-99999";

[Fact]
public async Task PostRoutesLoop_WhenOrsErrorContainsApiKey_ResponseBodyDoesNotExposeKey()
{
    await using var factory = new VeloRouteWebApplicationFactory(apiKey: TestApiKeySentinel);
    factory.FakeClient.Results.Enqueue(
        RoutingResult<RouteResult>.Failure(
            new RoutingError("PROVIDER_ERROR",
                $"ORS rejected request. Key: {TestApiKeySentinel}")));

    var client = factory.CreateClient();
    var response = await client.PostAsync(
        "/routes/loop",
        new StringContent(
            """{"startLon":16.37,"startLat":48.20,"minKm":15,"maxKm":25,"seed":null}""",
            System.Text.Encoding.UTF8, "application/json"));

    var body = await response.Content.ReadAsStringAsync();
    Assert.DoesNotContain(TestApiKeySentinel, body);
}
```

#### 2. Fill §6.3 in test-plan.md

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the "TBD" placeholder in §6.3 with the concrete patterns established in
this phase.

**Contract**: Update the `### 6.3 Adding a security / privacy integration test` section body
to document:

- Log-capture pattern: `useFakeLogging: true` in factory constructor; retrieve
  `FakeLogCollector` via `factory.Services.GetRequiredService<FakeLogCollector>()`; call
  `.GetSnapshot()` after the request; join all `.Message` strings; use `Assert.DoesNotContain`.
- Key-leakage pattern: `apiKey: "sentinel"` in factory constructor; enqueue a `RoutingResult.Failure`
  whose error message contains the sentinel; assert `DoesNotContain(sentinel, responseBody)`.
- Note the anti-pattern to avoid: asserting log level is set vs. asserting coordinate values
  don't appear.

Also update the `# Test Plan` header timestamp (`Last updated: 2026-06-20`) and mark Phase 3
status as `shipped` in §3 Phased Rollout table.

### Success Criteria

#### Automated Verification

- All tests pass including the two new ones: `dotnet test src/backend/VeloRoute.sln`
- Build clean, no warnings: `dotnet build src/backend/VeloRoute.sln`

#### Manual Verification

- Run `dotnet test src/backend/VeloRoute.sln --verbosity normal` and confirm both new test
  method names appear in output as `passed`.
- Confirm the two tests would detect a regression: temporarily add
  `logger.LogInformation("Processing {Lon},{Lat}", request.StartLon, request.StartLat)` to
  the route endpoint in `Program.cs`, re-run — Risk #4 test must fail. Revert the change.

**Implementation Note**: After automated verification passes, perform the manual regression
check before marking Phase 2 complete. The regression-detection verification is the key signal
that the test has real teeth.

---

## Testing Strategy

### Integration Tests

- Risk #4: `POST /routes/loop` completes successfully; `FakeLogCollector` snapshot asserts
  coordinate values absent from all log entries.
- Risk #6: `POST /routes/loop` returns error; response body string asserts sentinel key absent.

### Manual Testing Steps

1. Run `dotnet test src/backend/VeloRoute.sln --verbosity normal` — both new tests pass.
2. Temporarily add coordinate logging to `Program.cs` route handler; re-run tests — Risk #4
   test fails. Revert.
3. Verify no duplicate type definition warnings in build output after Phase 1.

## References

- Roadmap: `context/foundation/roadmap.md` (F-04)
- Test plan risks: `context/foundation/test-plan.md` §2 Risk #4 and Risk #6
- Phase 2 factory pattern: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`
- `Microsoft.Extensions.Diagnostics.Testing`: register via `AddFakeLogging()`, access via
  `GetRequiredService<FakeLogCollector>()`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Extract Shared Test Infrastructure

#### Automated

- [x] 1.1 Build succeeds: `dotnet build src/backend/VeloRoute.sln` — 8cfe51c
- [x] 1.2 All existing tests still pass: `dotnet test src/backend/VeloRoute.sln` — 8cfe51c

#### Manual

- [x] 1.3 No duplicate type definition warnings in build output — 8cfe51c

### Phase 2: Security and Privacy Integration Tests

#### Automated

- [x] 2.1 All tests pass including new ones: `dotnet test src/backend/VeloRoute.sln`
- [x] 2.2 Build clean, no warnings: `dotnet build src/backend/VeloRoute.sln`

#### Manual

- [x] 2.3 Both new test method names appear as `passed` in `--verbosity normal` output
- [x] 2.4 Temporary coordinate log line causes Risk #4 test to fail; reverting restores green
