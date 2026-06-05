# Backend Test Bootstrap — ORS Mapping and GPX Locale Coverage

## Overview

Bootstrap the xUnit test project for the .NET backend and write the first two units of automated test coverage defending the risks the team has already been burned by: ORS enum mapping drift (Risk #1) and GPX locale/format failure (Risk #3). These are the cheapest tests for the highest-known-probability failures.

## Current State Analysis

No test project exists. No `.sln` file exists. Both target classes (`GpxSerializer`, `OpenRouteServiceClient`) are `internal`. The ORS mapping logic lives as a `static` local function inside `GetDirectionsAsync` — not callable from tests in its current form. The GPX serializer uses the `"G"` format specifier on coordinates, which can produce scientific notation for near-zero values.

The bug history is concrete: `SurfaceType` previously had `Gravel = 3` while ORS defines code 3 = Asphalt. The fix corrected the enum integer assignments but left the underlying fragility — a direct cast with no lookup table and no test.

## Desired End State

`dotnet test VeloRoute.sln` from the repo root exits 0 with all tests passing.

Risk #1 is proven: 28 [Theory] rows (19 surface + 9 road class) each explicitly assert that a specific ORS integer code maps to a specific named `SurfaceType` / `RoadClass` value, with the expected value sourced from the ORS API documentation, not from reading the enum. Three additional tests prove the `Enum.IsDefined` guard and the span-gap default.

Risk #3 is proven: the serializer output uses `.` as the decimal separator under Polish and German locales; the XML structure contains `<trk>/<trkseg>/<trkpt>` and no `<rte>` elements; a known coordinate value survives round-trip intact.

### Key Discoveries

- `MapToRouteResult` is a `static` local function at `OpenRouteServiceClient.cs:101` — not directly callable. Extraction to `OrsMapper` is required before any mapping test can be written (`src/backend/Routing/OpenRouteServiceClient.cs:101`)
- `GpxSerializer` is a pure function with no DI or state — the ideal unit test target (`src/backend/Routing/GpxSerializer.cs:1`)
- Both classes are `internal`; the test project requires `InternalsVisibleTo` set in the main project's `.csproj` (`src/backend/VeloRoute.csproj`)
- The resilience pipeline wraps `HttpClient` in the live app — by extracting `OrsMapper` as a pure static class, Phase 1 unit tests bypass the HTTP layer entirely; no pipeline concern
- `"G"` format specifier at `GpxSerializer.cs:13–14` can produce scientific notation for values with exponent ≤ −5; changing to `"R"` (round-trip) eliminates this latent defect
- No `.sln` file exists; Phase 1 creates one at the repo root

## What We're NOT Doing

- Integration tests (Phase 2 of `test-plan.md` — ORS HTTP mock, constraint validation)
- Testing through the HTTP stack (`WebApplicationFactory` / `TestServer`)
- Testing the resilience pipeline retry / circuit-breaker behaviour
- Frontend tests
- CI gate wiring (Phase 4 of `test-plan.md`)
- Exhaustive snapshot testing of the full GPX XML output

## Implementation Approach

Three sequential phases. Phase 1 is pure infrastructure — nothing compiles or runs as a test until it is done. Phases 2 and 3 are independent of each other once Phase 1 is complete but are ordered: extracting `OrsMapper` (Phase 2) touches production code, so it is verified first before moving to the GPX work.

The key architectural choice: `OrsMapper` is extracted as an `internal static class` with three methods — `MapSurfaceCode`, `MapRoadClassCode`, and `BuildSegments`. This lets each [Theory] row call the mapping function directly with a raw integer and assert a named enum value, with no HTTP, no JSON, and no dependency on `OpenRouteServiceClient`. `OpenRouteServiceClient` delegates to `OrsMapper.BuildSegments` after extraction, keeping its logic identical.

## Critical Implementation Details

**`InternalsVisibleTo` in `.csproj`**: The modern way (no separate `AssemblyInfo.cs` needed) is an `AssemblyAttribute` item in the project file:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>VeloRoute.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

The test project assembly name must be `VeloRoute.Tests` (set via `<AssemblyName>` in the `.csproj`) for this to take effect.

**Oracle constraint — do not derive test expected values from the enum**: Each `[InlineData]` row must name the expected value from the ORS API documentation (https://giscience.github.io/openrouteservice/documentation/extra-info/Extra-Info.html), not by reading the production enum. The comment on each row — `// ORS: 3=Asphalt` — is the independent reference. If the enum is wrong, the test must catch it. If both the test and the enum are wrong in the same way, the test cannot catch it — this is the shipped-bug scenario the test plan explicitly names.

---

## Phase 1: xUnit project bootstrap and solution wiring

### Overview

Creates the test project, solution file, and `InternalsVisibleTo` attribute. No tests are written in this phase — the goal is a green `dotnet test VeloRoute.sln` with zero tests (0 passed, 0 failed).

### Changes Required

#### 1. Create the xUnit test project

**File**: `src/backend.tests/VeloRoute.Tests.csproj` (new directory + file, via `dotnet new xunit`)

**Intent**: Bootstrap a net10.0 xUnit project that references the main backend project. The default `UnitTest1.cs` generated by the template must be deleted — it produces a spurious passing test that would mask a broken bootstrap.

**Contract**: Run from repo root:
```
dotnet new xunit -n VeloRoute.Tests -o src/backend.tests --framework net10.0
del src\backend.tests\UnitTest1.cs
cd src/backend.tests && dotnet add reference ../backend/VeloRoute.csproj
```

The resulting `.csproj` must have `<TargetFramework>net10.0</TargetFramework>` and a `<ProjectReference>` to `../backend/VeloRoute.csproj`. The packages `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, and `coverlet.collector` are added automatically by the template.

#### 2. Add `InternalsVisibleTo` to the main project

**File**: `src/backend/VeloRoute.csproj`

**Intent**: Grant the test project access to `internal` types (`GpxSerializer`, `OpenRouteServiceClient`, and the to-be-created `OrsMapper`) without making them public.

**Contract**: Add the `AssemblyAttribute` `ItemGroup` shown in Critical Implementation Details above.

#### 3. Create the solution file

**File**: `VeloRoute.sln` (repo root, new)

**Intent**: Allow `dotnet build` and `dotnet test` to run from the repo root, covering both projects in one command.

**Contract**: Run from repo root:
```
dotnet new sln -n VeloRoute
dotnet sln VeloRoute.sln add src/backend/VeloRoute.csproj
dotnet sln VeloRoute.sln add src/backend.tests/VeloRoute.Tests.csproj
```

### Success Criteria

#### Automated Verification

- `dotnet build VeloRoute.sln` exits 0 with no errors or warnings
- `dotnet test VeloRoute.sln` exits 0; output reads "Passed! - 0" (no tests yet, no failures)

#### Manual Verification

- Confirm `VeloRoute.sln` exists at repo root and lists both projects
- Confirm `src/backend.tests/` directory contains `VeloRoute.Tests.csproj` and no `UnitTest1.cs`

**Implementation Note**: After both automated checks pass, verify manually that the solution file is well-formed before proceeding to Phase 2.

---

## Phase 2: OrsMapper extraction and Risk #1 unit tests

### Overview

Extracts the segment-mapping logic from `OpenRouteServiceClient` into a testable `internal static class OrsMapper`, then writes unit tests that prove every ORS surface and waytype code maps to the correct domain enum value, using the ORS API documentation as the independent oracle.

### Changes Required

#### 1. Create `OrsMapper`

**File**: `src/backend/Routing/OrsMapper.cs` (new)

**Intent**: Own all logic that converts raw ORS integer codes and span arrays into domain types. Extraction makes the logic unit-testable without HTTP. `OpenRouteServiceClient` becomes a thin caller.

**Contract**: `internal static class OrsMapper` in `namespace VeloRoute.Routing` with three methods:

- `internal static SurfaceType MapSurfaceCode(int code)` — returns `(SurfaceType)code` if `Enum.IsDefined((SurfaceType)code)`, else `SurfaceType.Unknown`
- `internal static RoadClass MapRoadClassCode(int code)` — same pattern for `RoadClass`
- `internal static IReadOnlyList<RouteWaySegment> BuildSegments(IReadOnlyList<int[]> surfaceSpans, IReadOnlyList<int[]> waytypeSpans)` — contains the boundary-merging loop and midpoint span lookup currently at `OpenRouteServiceClient.cs:112–137`; calls `MapSurfaceCode` / `MapRoadClassCode` for the final cast

#### 2. Update `OpenRouteServiceClient` to delegate to `OrsMapper`

**File**: `src/backend/Routing/OpenRouteServiceClient.cs`

**Intent**: Remove the segment-building logic from `MapToRouteResult` and replace with a single call to `OrsMapper.BuildSegments`. The observable behaviour of `GetDirectionsAsync` must be identical.

**Contract**: The `MapToRouteResult` local function (lines 101–140) is simplified: replace the `boundaries` loop and the two `segments.Add(...)` calls with `var segments = OrsMapper.BuildSegments(surfaceSpans, waytypeSpans);`. The `coordinates` and `distanceMeters` extraction stays in place.

#### 3. Create `OrsMapperTests`

**File**: `src/backend.tests/Routing/OrsMapperTests.cs` (new)

**Intent**: Prove that every ORS surface code and waytype code maps to the correct domain enum value, with expected values sourced from ORS API documentation — not from reading the production enum. Also prove that unknown codes and span gaps produce `Unknown` rather than throwing or producing a wrong value.

**Contract**: `public class OrsMapperTests` in `namespace VeloRoute.Tests.Routing`. Four test methods:

**`MapSurfaceCode_KnownCodes_ReturnCorrectSurfaceType`** — `[Theory]` with `[InlineData]` for all 19 ORS surface codes. Each row comment cites the ORS docs:
```
// ORS surface codes — source: ORS API docs (extras/surface)
[InlineData(0,  SurfaceType.Unknown)]         // 0=Unknown
[InlineData(1,  SurfaceType.Paved)]           // 1=Paved
[InlineData(2,  SurfaceType.Unpaved)]         // 2=Unpaved
[InlineData(3,  SurfaceType.Asphalt)]         // 3=Asphalt  ← shipped bug was Gravel here
[InlineData(4,  SurfaceType.Concrete)]        // 4=Concrete
[InlineData(5,  SurfaceType.Cobblestone)]     // 5=Cobblestone
[InlineData(6,  SurfaceType.Metal)]           // 6=Metal
[InlineData(7,  SurfaceType.Wood)]            // 7=Wood
[InlineData(8,  SurfaceType.CompactedGravel)] // 8=Compacted gravel
[InlineData(9,  SurfaceType.FineGravel)]      // 9=Fine gravel
[InlineData(10, SurfaceType.Gravel)]          // 10=Gravel
[InlineData(11, SurfaceType.Dirt)]            // 11=Dirt
[InlineData(12, SurfaceType.Ground)]          // 12=Ground
[InlineData(13, SurfaceType.Ice)]             // 13=Ice
[InlineData(14, SurfaceType.PavingStones)]    // 14=Paving stones
[InlineData(15, SurfaceType.Sand)]            // 15=Sand
[InlineData(16, SurfaceType.Woodchips)]       // 16=Woodchips
[InlineData(17, SurfaceType.Grass)]           // 17=Grass
[InlineData(18, SurfaceType.GrassPaver)]      // 18=Grass paver
```

**`MapSurfaceCode_UnknownCode_ReturnsUnknown`** — `[Theory]` with `[InlineData(99)]`, `[InlineData(-1)]`, `[InlineData(100)]`.

**`MapRoadClassCode_KnownCodes_ReturnCorrectRoadClass`** — `[Theory]` with `[InlineData]` for all 9 ORS waytype codes:
```
// ORS waytype codes — source: ORS API docs (extras/waytypes)
[InlineData(0, RoadClass.Unknown)]   // 0=Unknown
[InlineData(1, RoadClass.StateRoad)] // 1=State road
[InlineData(2, RoadClass.Road)]      // 2=Road
[InlineData(3, RoadClass.Street)]    // 3=Street
[InlineData(4, RoadClass.Path)]      // 4=Path
[InlineData(5, RoadClass.Track)]     // 5=Track
[InlineData(6, RoadClass.Cycleway)]  // 6=Cycleway
[InlineData(7, RoadClass.FootPath)]  // 7=Footpath
[InlineData(8, RoadClass.Steps)]     // 8=Steps
```

**`MapRoadClassCode_UnknownCode_ReturnsUnknown`** — `[Theory]` with `[InlineData(99)]`, `[InlineData(-1)]`.

**`BuildSegments_GapBetweenSurfaceSpans_ProducesUnknownSurface`** — `[Fact]`. Input: `surfaceSpans = [[0,2,3], [4,6,10]]`, `waytypeSpans = [[0,6,6]]`. The segment [2,4] (mid=3) falls in a gap in the surface spans — no surface span covers index 3. Assert that the segment at [2,4] has `Surface == SurfaceType.Unknown` and `RoadClass == RoadClass.Cycleway` (waytype span covers the full range).

**`BuildSegments_EmptySpans_ReturnsEmptyList`** — `[Fact]`. Input: both span lists empty. Assert result is empty, no exception.

### Success Criteria

#### Automated Verification

- `dotnet build VeloRoute.sln` exits 0
- `dotnet test VeloRoute.sln` exits 0; all OrsMapper tests pass (28 known-code rows + 5 unknown/gap rows = 33 test cases)

#### Manual Verification

- Review the `[InlineData]` comments in `OrsMapperTests.cs` — each expected value should be independently readable against the ORS API docs without looking at the production enum

**Implementation Note**: After passing automated checks, manually verify that the `OrsMapper` test file comments name ORS codes independent of how the enum members are defined. If any row reads `[InlineData(3, (SurfaceType)3)]` or derives its expected value by reading the enum source, rewrite it — that is the oracle problem.

---

## Phase 3: GPX format specifier fix and Risk #3 unit tests

### Overview

Fixes the latent `"G"` format defect in `GpxSerializer` by changing to `"R"` (round-trip), then writes unit tests that prove the serializer produces decimal-point coordinates under non-English locales, emits the correct `<trk>/<trkseg>/<trkpt>` structure, and preserves coordinate values.

### Changes Required

#### 1. Fix the `"G"` format specifier in `GpxSerializer`

**File**: `src/backend/Routing/GpxSerializer.cs`

**Intent**: Replace the `"G"` (general) format specifier with `"R"` (round-trip) on both coordinate format calls. `"R"` guarantees that the formatted string, when parsed back, returns the exact same `double` value. For GPS coordinates in [−180, 180], it also guarantees decimal (not scientific) notation.

**Contract**: Lines 13–14 change from `ToString("G", CultureInfo.InvariantCulture)` to `ToString("R", CultureInfo.InvariantCulture)` for both `c.Latitude` and `c.Longitude`. No other changes.

#### 2. Create `GpxSerializerTests`

**File**: `src/backend.tests/Routing/GpxSerializerTests.cs` (new)

**Intent**: Prove that the serializer produces GPX-compliant output regardless of the server locale. The locale-injection tests are the core of Risk #3: they manufacture the production failure mode (non-English server) in a unit test and verify it does not occur.

**Contract**: `public class GpxSerializerTests` in `namespace VeloRoute.Tests.Routing`. Five test methods:

**`Serialize_WithPolishCulture_CoordinatesUseDecimalPoint`** — `[Fact]`. Sets `Thread.CurrentThread.CurrentCulture` and `CurrentUICulture` to `new CultureInfo("pl-PL")` (comma decimal separator), calls `GpxSerializer.Serialize` with a coordinate pair containing fractional digits (e.g., latitude `48.20849`, longitude `16.37208`), then restores the original culture. Asserts the output contains `"48.20849"` (with a dot, not `"48,20849"`).

**`Serialize_WithGermanCulture_CoordinatesUseDecimalPoint`** — `[Fact]`. Same pattern with `"de-DE"`.

**`Serialize_OutputContainsTrkStructure_NotRteStructure`** — `[Fact]`. Calls `Serialize` with two coordinates. Asserts:
- output contains `<trk>`
- output contains `<trkseg>`
- output contains `<trkpt `
- output does NOT contain `<rte>`
- output does NOT contain `<rtept`

**`Serialize_KnownCoordinateValueAppearsLiterally`** — `[Fact]`. Uses coordinate `48.20849` (a value whose decimal string representation is unambiguous). Asserts the serialized string contains the literal text `"48.20849"`. This protects against any format change that alters the representation.

**`Serialize_EmptyCoordinateList_ReturnsValidGpxWithEmptyTrkseg`** — `[Fact]`. Calls `Serialize` with an empty list. Asserts the output contains `<trkseg>` and `</trkseg>` (valid empty track segment), does not throw, and is well-formed XML.

### Success Criteria

#### Automated Verification

- `dotnet build VeloRoute.sln` exits 0
- `dotnet test VeloRoute.sln` exits 0; all GpxSerializer tests pass (5 test cases)
- `dotnet test VeloRoute.sln --verbosity normal` shows all 38 tests passing (33 + 5)

#### Manual Verification

- Confirm `GpxSerializer.cs` lines 13–14 now read `"R"` not `"G"`
- Confirm the locale-injection tests restore the original culture in a `finally` block (or using a scope-pattern) — a test that leaks a mutated `CurrentCulture` would corrupt subsequent tests

**Implementation Note**: Culture mutation in tests is a shared-state hazard. The two locale tests must save the original culture before the call and restore it unconditionally in a `finally` block. If the test framework runs tests in parallel (xUnit does by default for different classes), the mutation is process-wide state — consider wrapping culture changes in a `try/finally` or using a helper fixture.

---

## Testing Strategy

### Unit Tests

- **OrsMapper — mapping correctness** (28 known-code [Theory] rows): each row is an independent assertion that ORS integer code N maps to the correct named domain value per ORS API docs
- **OrsMapper — guard and gap behaviour** (5 cases): unknown codes → Unknown; span gap → Unknown; empty spans → empty list
- **GpxSerializer — locale safety** (2 cases): Polish and German locales; assert decimal point
- **GpxSerializer — structure** (1 case): trk/trkseg/trkpt present; rte/rtept absent
- **GpxSerializer — value integrity** (1 case): known coordinate appears literally
- **GpxSerializer — edge case** (1 case): empty coordinate list produces valid XML

### Manual Testing Steps

1. Run `dotnet test VeloRoute.sln --verbosity normal` — confirm 38 passing tests, 0 failures
2. Inspect `OrsMapperTests.cs` comments — each `[InlineData]` expected value should be independently verifiable against ORS API documentation without reading the production enum
3. Confirm `GpxSerializer.cs` uses `"R"` on both coordinate format calls
4. Confirm locale tests restore `CurrentCulture` in a `finally` block

## References

- Research: `context/changes/testing-backend-bootstrap/research.md`
- Test plan: `context/foundation/test-plan.md` (Phase 1, risks #1 and #3)
- Shipped bug history: `context/changes/loop-route-generation/research.md:402–410`
- ORS mapping source: `src/backend/Routing/OpenRouteServiceClient.cs:101–140`
- GPX serializer: `src/backend/Routing/GpxSerializer.cs`
- ORS API extras docs: https://giscience.github.io/openrouteservice/documentation/extra-info/Extra-Info.html

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: xUnit project bootstrap and solution wiring

#### Automated

- [x] 1.1 `dotnet build VeloRoute.sln` exits 0 with no errors or warnings
- [x] 1.2 `dotnet test VeloRoute.sln` exits 0; output shows 0 tests, 0 failures

#### Manual

- [ ] 1.3 `VeloRoute.sln` exists at repo root and lists both projects
- [ ] 1.4 `src/backend.tests/` contains `VeloRoute.Tests.csproj` and no `UnitTest1.cs`

### Phase 2: OrsMapper extraction and Risk #1 unit tests

#### Automated

- [ ] 2.1 `dotnet build VeloRoute.sln` exits 0
- [ ] 2.2 `dotnet test VeloRoute.sln` exits 0; all 33 OrsMapper test cases pass

#### Manual

- [ ] 2.3 `OrsMapperTests.cs` [InlineData] comments name ORS codes independently of the production enum (no `(SurfaceType)3` style expected values)

### Phase 3: GPX format specifier fix and Risk #3 unit tests

#### Automated

- [ ] 3.1 `dotnet build VeloRoute.sln` exits 0
- [ ] 3.2 `dotnet test VeloRoute.sln` exits 0; all 38 tests pass (33 + 5)

#### Manual

- [ ] 3.3 `GpxSerializer.cs` lines 13–14 use `"R"` not `"G"`
- [ ] 3.4 Locale-injection tests save and restore `CurrentCulture` in a `finally` block
