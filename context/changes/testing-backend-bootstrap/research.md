---
date: 2026-06-05T17:05:40+02:00
researcher: Copilot
git_commit: 5934d0b
branch: main
repository: lukasz05/velo-route
topic: "Backend test bootstrap — ORS enum mapping drift (Risk #1) and GPX locale/format failure (Risk #3)"
tags: [research, backend, xunit, ors-mapping, gpx-serializer, surface-type, road-class, testing]
status: complete
last_updated: 2026-06-05
last_updated_by: Copilot
---

# Research: Backend test bootstrap — ORS enum mapping and GPX locale coverage (Phase 1)

**Date**: 2026-06-05T17:05:40+02:00
**Researcher**: Copilot
**Git Commit**: [5934d0b](https://github.com/lukasz05/velo-route/blob/5934d0b)
**Branch**: main
**Repository**: lukasz05/velo-route

## Research Question

Phase 1 of `context/foundation/test-plan.md`: bootstrap xUnit; defend Risk #1 (ORS enum mapping drift) and Risk #3 (GPX locale/format failure) at unit level. The team has shipped the enum bug before. Challenges to defeat: "it rendered = parsing was correct" (Risk #1) and "works in dev = works in prod" (Risk #3).

---

## Summary

**Risk #1 (ORS mapping)**: The mapping lives entirely in a single `static` local function `MapToRouteResult` inside `OpenRouteServiceClient.GetDirectionsAsync` (lines 101–140). It uses direct enum cast: `(SurfaceType)surfaceCode` with an `Enum.IsDefined` guard. **This is the same technique that shipped the `Gravel=3` bug** (documented in `loop-route-generation/research.md:409`). The current enum was subsequently corrected (`Asphalt=3` now matches ORS code 3), but the approach has no test. A future enum member insertion or reordering would silently regress.

The critical architectural consequence for tests: `MapToRouteResult` is a file-local function — **not directly callable from outside the file**. Tests must drive it through `GetDirectionsAsync` with a captured-payload `HttpClient` stub or through extraction of the mapping into a testable helper. The **oracle problem is real**: test expected values must come from an independent ORS surface/waytype code table (not from the enum definition itself), or the test will verify nothing.

**Risk #3 (GPX format)**: `GpxSerializer.Serialize` already uses `CultureInfo.InvariantCulture` explicitly on both coordinate values (lines 13–14). It emits `<trk>/<trkseg>/<trkpt>` structure. The risk is currently **not manifested in code**, but the test is still required to lock this in as a regression guard — one refactor that changes the format specifier or removes the culture arg would be invisible without it.

**xUnit bootstrap**: no test project, no `.sln` file. `GpxSerializer` and `OpenRouteServiceClient` are both `internal`. The test project will need an `InternalsVisibleTo` attribute or must test through the public HTTP endpoints. For Phase 1 unit tests, `InternalsVisibleTo` is the right approach for `GpxSerializer`; for ORS mapping, the mapping logic must be either extracted to a testable helper or exercised through a real `HttpClient` backed by a fake handler.

---

## Detailed Findings

### Risk #1 — ORS Enum Mapping

#### Mapping mechanism

**File**: [`src/backend/Routing/OpenRouteServiceClient.cs`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/OpenRouteServiceClient.cs)

```csharp
// Line 129
var surfaceCode = surfaceSpans.FirstOrDefault(s => s[0] <= mid && mid < s[1])?[2] ?? 0;
// Line 130
var waytypeCode = waytypeSpans.FirstOrDefault(s => s[0] <= mid && mid < s[1])?[2] ?? 0;

// Lines 135–136
Enum.IsDefined((SurfaceType)surfaceCode) ? (SurfaceType)surfaceCode : SurfaceType.Unknown,
Enum.IsDefined((RoadClass)waytypeCode) ? (RoadClass)waytypeCode : RoadClass.Unknown
```

This is a **direct integer-to-enum cast** guarded by `Enum.IsDefined`. The mapping is correct if and only if every ORS integer code equals the corresponding enum member's assigned integer. There is no lookup table; no explicit code-comment pairing; no assertion.

Fallback behaviour: any unknown code silently produces `Unknown` (the `?? 0` default also silently produces `Unknown` when a span boundary has no match at mid, e.g., a gap between spans).

#### SurfaceType enum — current values

**File**: [`src/backend/Routing/SurfaceType.cs:1–27`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/SurfaceType.cs)

| Enum value | Int | ORS surface code matches? |
|---|---|---|
| Unknown | 0 | ✓ (0 = Unknown) |
| Paved | 1 | ✓ |
| Unpaved | 2 | ✓ |
| Asphalt | 3 | ✓ (was `Gravel=3` in old plan — **this was the shipped bug**) |
| Concrete | 4 | ✓ |
| Cobblestone | 5 | ✓ |
| Metal | 6 | ✓ |
| Wood | 7 | ✓ |
| CompactedGravel | 8 | ✓ |
| FineGravel | 9 | ✓ |
| Gravel | 10 | ✓ |
| Dirt | 11 | ✓ |
| Ground | 12 | ✓ |
| Ice | 13 | ✓ |
| PavingStones | 14 | ✓ |
| Sand | 15 | ✓ |
| Woodchips | 16 | ✓ |
| Grass | 17 | ✓ |
| GrassPaver | 18 | ✓ |

The old plan (pre-fix) defined `Gravel=3, Ground=4, Dirt=5, Rock=6` (`routing-api-wiring/plan.md:101`). The enum was corrected to match ORS codes after the bug was found.

#### RoadClass enum — current values

**File**: [`src/backend/Routing/RoadClass.cs:1–17`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/RoadClass.cs)

| Enum value | Int | ORS waytype code matches? |
|---|---|---|
| Unknown | 0 | ✓ |
| StateRoad | 1 | ✓ |
| Road | 2 | ✓ |
| Street | 3 | ✓ |
| Path | 4 | ✓ |
| Track | 5 | ✓ |
| Cycleway | 6 | ✓ |
| FootPath | 7 | ✓ |
| Steps | 8 | ✓ |

RoadClass matches the original plan contract and has not been reported as incorrect.

#### Critical testability constraint

`MapToRouteResult` is a `static` local function declared inside `GetDirectionsAsync` at line 101. **It is not a method on the class; it cannot be called in isolation**. Unit tests for the mapping must use one of two approaches:

1. **Captured-payload stub** (recommended for unit tests): create a `DelegatingHandler` or a fake `HttpMessageHandler` that returns a pre-recorded ORS GeoJSON JSON fixture, then call `GetDirectionsAsync` and assert on the returned `RouteResult.Segments`. This exercises the full mapping path in one test.

2. **Extract the mapper**: move the span-to-segment mapping to an internal static method (e.g., `OrsMapper.MapSegments`) for direct unit testing without an HTTP layer. This solves the oracle problem most cleanly.

Approach 1 is lower-friction for Phase 1; approach 2 is structurally sounder. The plan should decide.

#### Oracle problem — what the test expected values must NOT be

The test plan calls this out explicitly: "Copying the expected enum value from the production mapping code (oracle problem — if the mapping is wrong in both, the test passes and the bug ships again)."

The test expected values **must** come from an independent source — for example, a comment block in the test file that reproduces the ORS API surface/waytype code table from the ORS documentation. The test asserts:

```
ORS JSON with surface code 3 in extras → RouteWaySegment.Surface == SurfaceType.Asphalt  // ORS docs: 3=Asphalt
ORS JSON with surface code 10 in extras → RouteWaySegment.Surface == SurfaceType.Gravel  // ORS docs: 10=Gravel
```

Not:
```
ORS JSON with surface code 3 → RouteWaySegment.Surface == (SurfaceType)3   // ← oracle problem
```

#### Request `extra_info` key asymmetry (known historical bug)

ORS request `extra_info` uses `"waytype"` (no trailing s); ORS response key is `"waytypes"` (with s). This was a critical finding during `routing-api-wiring` that would have caused all RoadClass values to be `Unknown`. The current implementation sends `["surface", "waytype"]` at line 47. Tests that use captured payloads should verify via a real ORS response fixture, not a synthetic one constructed without this knowledge.

---

### Risk #3 — GpxSerializer Locale and Structure

#### Implementation

**File**: [`src/backend/Routing/GpxSerializer.cs:1–38`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/GpxSerializer.cs)

```csharp
var lat = c.Latitude.ToString("G", CultureInfo.InvariantCulture);   // line 13
var lon = c.Longitude.ToString("G", CultureInfo.InvariantCulture);  // line 14
return $"""      <trkpt lat="{lat}" lon="{lon}"></trkpt>""";        // line 15
```

`CultureInfo.InvariantCulture` **is** explicitly used. The format specifier is `"G"` (general), which uses decimal point (not comma) for all double values. For GPS coordinates in the range −180 to 180, `"G"` never produces scientific notation.

The XML output structure (lines 18–36):

```
<?xml version="1.0" encoding="UTF-8"?>
<gpx version="1.1" creator="VeloRoute"
     xmlns="http://www.topografix.com/GPX/1/1"
     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
     xsi:schemaLocation="...">
  <metadata>
    <name>VeloRoute Loop</name>
    <time>{DateTime.UtcNow:O}</time>
  </metadata>
  <trk>
    <name>VeloRoute Loop</name>
    <type>cycling</type>
    <trkseg>
      <trkpt lat="..." lon="..."></trkpt>
      ...
    </trkseg>
  </trk>
</gpx>
```

This is GPX 1.1-compliant track format. No `<rte>` or `<rtept>` elements. No elevation (`<ele>`).

#### Current status

The code is **currently correct** for Risk #3. The test is still mandatory as a regression guard. Key scenarios the test must cover:

1. **Locale injection**: set `Thread.CurrentThread.CurrentCulture` to a comma-decimal locale (e.g., `pl-PL` or `de-DE`) before calling `Serialize`, then assert coordinates use `"."` as the decimal separator.
2. **Structure assertion**: assert the output contains `<trk>`, `<trkseg>`, `<trkpt`, and does NOT contain `<rte>` or `<rtept>`.
3. **Round-trip value integrity**: assert that a known coordinate value (e.g., `48.20849`) appears literally in the output.

#### Minor edge case — `"G"` format for near-zero values

`"G"` switches to scientific notation when the exponent is ≤ −5 or ≥ precision (15). For a coordinate value like `0.000001°` (never a real GPS coordinate), the output would be `1E-06` — invalid GPX XML. This is not a realistic scenario for VeloRoute (all coordinates are in the range −180 to 180 with at minimum 2 integer digits), but should be documented as a known limitation. **No fix needed for Phase 1.**

#### Only caller

`GpxSerializer.Serialize` is called exactly once: `Program.cs:110` inside `app.MapPost("/routes/gpx", ...)`.

---

### xUnit Bootstrap — Project Structure

#### Current state

- Main project: `src/backend/VeloRoute.csproj` (`net10.0`, `RootNamespace: VeloRoute`, nullable enabled)
- **No `.sln` file**
- **No test project**
- **No existing test infrastructure anywhere in the repo**

#### Access modifier constraint

Both test targets are `internal`:
- `GpxSerializer` — `internal static class` (`GpxSerializer.cs:5`)
- `OpenRouteServiceClient` — `internal sealed class` (`OpenRouteServiceClient.cs:8`)

To unit-test these directly, the main project must expose internals to the test project via `InternalsVisibleTo`:

```xml
<!-- Add to VeloRoute.csproj or a separate AssemblyInfo.cs -->
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>VeloRoute.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

#### Recommended project layout

```
src/
  backend/                   ← existing (VeloRoute.csproj)
  backend.tests/             ← new (VeloRoute.Tests.csproj)
    VeloRoute.Tests.csproj
    Routing/
      GpxSerializerTests.cs
      OrsEnumMappingTests.cs
```

Bootstrap commands:

```bash
cd src
dotnet new xunit -n VeloRoute.Tests -o backend.tests
cd backend.tests
dotnet add reference ../backend/VeloRoute.csproj
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit
dotnet add package xunit.runner.visualstudio
dotnet add package coverlet.collector
```

Then from repo root: `dotnet test src/backend.tests/`

#### Key packages in main project (relevant for test project decisions)

| Package | Version | Test relevance |
|---|---|---|
| `Microsoft.Extensions.Http.Resilience` | 10.6.0 | HttpClient retry — test stubs must account for retry behaviour |
| `NetTopologySuite` | 2.5.0 | Geometry types — test fixtures may need to reference these |
| `Microsoft.AspNetCore.OpenApi` | 10.0.7 | No direct test relevance |

---

## Code References

- [`src/backend/Routing/OpenRouteServiceClient.cs:101–140`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/OpenRouteServiceClient.cs#L101) — `MapToRouteResult` local function (full span-to-enum mapping)
- [`src/backend/Routing/OpenRouteServiceClient.cs:129–136`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/OpenRouteServiceClient.cs#L129) — direct cast: `(SurfaceType)surfaceCode` / `(RoadClass)waytypeCode`
- [`src/backend/Routing/SurfaceType.cs:1–27`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/SurfaceType.cs) — all 19 SurfaceType enum values with explicit integer assignments
- [`src/backend/Routing/RoadClass.cs:1–17`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/RoadClass.cs) — all 9 RoadClass enum values
- [`src/backend/Routing/GpxSerializer.cs:1–38`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/GpxSerializer.cs) — full serializer (38 lines total)
- [`src/backend/Routing/GpxSerializer.cs:13–14`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Routing/GpxSerializer.cs#L13) — `InvariantCulture` usage on lat/lon
- [`src/backend/Program.cs:99–111`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/Program.cs#L99) — sole caller of `GpxSerializer.Serialize`
- [`src/backend/VeloRoute.csproj`](https://github.com/lukasz05/velo-route/blob/5934d0b/src/backend/VeloRoute.csproj) — main project file (net10.0, no test runner)

---

## Architecture Insights

1. **Direct cast is the fragile core of Risk #1**. There is no lookup table. The correctness of the mapping is purely a function of enum integer assignments staying in sync with ORS API codes. The test plan's concern is well-founded: the team already shipped `Gravel=3` when ORS uses 3=Asphalt. The fix was to correct the enum, not to add a lookup table — the fragility remains.

2. **`MapToRouteResult` is not directly testable** without either a captured-payload stub or an extraction refactor. The plan must decide this before writing tests. Option B (extract to `internal static OrsMapper`) would allow testing the mapping in pure isolation and solve the oracle problem most cleanly.

3. **`GpxSerializer` is a pure function** (no DI, no state) — the ideal target for unit tests. Its only external dependency is `IReadOnlyList<RouteCoordinate>`. Tests are straightforward.

4. **`InternalsVisibleTo` is required** for both test targets. Neither class is public. This is intentional design (ORS codes and serialisation format are implementation details), but it means the test project setup must include this attribute.

5. **No `.sln` file means no `dotnet build` at the solution level**. For Phase 4 CI gate, creating a solution is recommended. Phase 1 can proceed without it using `dotnet test src/backend.tests/`.

6. **Resilience pipeline wraps the `HttpClient`** (retry + circuit breaker). A fake `HttpMessageHandler` for ORS mapping tests must account for this — the test stub will be called through the pipeline. Either disable the resilience for tests or use a `TestServer`/`WebApplicationFactory` approach.

---

## Historical Context (from prior changes)

- [`context/changes/loop-route-generation/research.md:402–410`](https://github.com/lukasz05/velo-route/blob/5934d0b/context/changes/loop-route-generation/research.md#L402) — **Documents the shipped bug**: "ORS code 3 = Asphalt; enum value 3 = Gravel. This is a bug in the existing code — the surface type display will be wrong. Needs a mapping table or corrected enum values." The fix chosen was corrected enum values, not a mapping table.

- [`context/changes/routing-api-wiring/plan.md:99–101`](https://github.com/lukasz05/velo-route/blob/5934d0b/context/changes/routing-api-wiring/plan.md#L99) — **Old SurfaceType contract** (`Gravel=3, Ground=4, Dirt=5, Rock=6`) — the state before the bug was found and fixed. This is the "before" state for the shipped bug.

- [`context/changes/routing-api-wiring/reviews/plan-review.md:35–43`](https://github.com/lukasz05/velo-route/blob/5934d0b/context/changes/routing-api-wiring/reviews/plan-review.md#L35) — **`extra_info` key asymmetry bug** discovered in plan review: `"waytypes"` in request must be `"waytype"`. Would have caused all RoadClass values to be Unknown. Fixed before implementation.

- [`context/changes/routing-api-wiring/plan-brief.md:66`](https://github.com/lukasz05/velo-route/blob/5934d0b/context/changes/routing-api-wiring/plan-brief.md#L66) — "A captured payload in a unit test is the safest guard" — the plan itself recommended this exact test approach during F-01.

- [`context/changes/gpx-export/plan-brief.md:68`](https://github.com/lukasz05/velo-route/blob/5934d0b/context/changes/gpx-export/plan-brief.md#L68) — GPX plan explicitly noted InvariantCulture omission as a risk on non-English servers.

---

## Open Questions

1. **Oracle source for ORS codes**: should the test file include an inline ORS surface/waytype code reference table sourced from ORS API docs, or should a separate `OrsCodeReference.cs` fixture class own it? The former is simpler; the latter is more maintainable if tests grow.

2. **MapToRouteResult testability strategy**: captured-payload stub via `HttpMessageHandler` (lower friction, exercises more code) vs. extract `OrsMapper` as a separate internal class (cleaner unit test, solves oracle problem more clearly). Plan must decide.

3. **Resilience pipeline in tests**: should the test project register the full resilience pipeline or bypass it? Bypassing is simpler for unit tests; keeping it catches retry-related bugs. For Phase 1 unit tests, bypassing is recommended.

4. **`.sln` creation**: Phase 1 can defer this to Phase 4. Confirm with the team whether `dotnet test src/backend.tests/` is acceptable for local runs in Phase 1.

5. **`"G"` format specifier**: should Phase 1 change it to `"F6"` (6 decimal places, always decimal notation, GPX 1.1 typical precision) for greater robustness? Not a Phase 1 blocker, but worth noting in the plan.
