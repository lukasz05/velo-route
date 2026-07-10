# Backend Test Bootstrap — Plan Brief

> Full plan: `context/changes/testing-backend-bootstrap/plan.md`
> Research: `context/changes/testing-backend-bootstrap/research.md`

## What & Why

Bootstrap the xUnit test project for the .NET backend and write the first two units of automated coverage defending the risks the team has already shipped: ORS enum mapping drift (Risk #1) and GPX locale/format failure (Risk #3). Both tests are unit-level — the cheapest layer that catches the bugs already known to have shipped.

## Starting Point

No test project, no solution file. The ORS mapping lives as a non-callable local function inside `GetDirectionsAsync`. Both target classes (`GpxSerializer`, `OpenRouteServiceClient`) are `internal`. The GPX serializer uses the `"G"` format specifier, which can produce scientific notation for near-zero coordinates.

## Desired End State

`dotnet test VeloRoute.sln` from repo root exits 0 with 38 passing tests. Risk #1 is proven by 33 [Theory]-driven test cases asserting that every known ORS surface and waytype integer code maps to the correct named domain value, with an ORS API docs reference as the oracle. Risk #3 is proven by 5 tests verifying decimal-point coordinates under Polish/German locales, correct `<trk>/<trkseg>/<trkpt>` structure, and value round-trip fidelity.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| ORS mapping testability | Extract `OrsMapper` static class | Makes each code→enum mapping directly callable in isolation — no HTTP, no JSON, no `HttpClient` stub needed | Plan |
| ORS code coverage | All 19 surface + 9 road class codes | The shipped bug was a single off-by-one; covering all codes prevents the same bug in a different slot | Plan |
| Test oracle | Inline `[InlineData]` comment citing ORS API docs | The oracle problem: expected values must be independent of the production enum or the test proves nothing | Research / Plan |
| Unknown-code tests | Include (99, −1 → Unknown; span gap → Unknown) | The `?? 0` and `Enum.IsDefined` guards are the only protection against future ORS code additions | Plan |
| GPX format specifier | Fix `"G"` → `"R"` in Phase 1 | `"R"` eliminates scientific notation for near-zero values and guarantees round-trip fidelity | Plan |
| Solution file | Create `VeloRoute.sln` at repo root now | Enables `dotnet build` / `dotnet test` from root; foundation for Phase 4 CI gate | Plan |

## Scope

**In scope:**
- xUnit project at `src/backend.tests/`, net10.0, project reference to `src/backend/`
- `VeloRoute.sln` at repo root covering both projects
- `InternalsVisibleTo("VeloRoute.Tests")` in `VeloRoute.csproj`
- `OrsMapper` extraction from `OpenRouteServiceClient` (production refactor required for testability)
- 33 ORS mapping test cases + 5 GPX serializer test cases

**Out of scope:**
- Integration tests (`WebApplicationFactory`, ORS HTTP mock)
- Resilience pipeline testing
- Frontend tests
- CI gate wiring (Phase 4 of test-plan.md)

## Architecture / Approach

`OrsMapper` is a new `internal static class` in `VeloRoute.Routing` owning three methods: `MapSurfaceCode(int)`, `MapRoadClassCode(int)`, and `BuildSegments(surfaceSpans, waytypeSpans)`. The loop and midpoint lookup currently in `OpenRouteServiceClient.MapToRouteResult` move to `BuildSegments`; `MapToRouteResult` becomes a thin caller. This makes the entire mapping logic unit-testable without any HTTP machinery. `GpxSerializer` is already a pure function — tests call it directly after injecting a non-English `CurrentCulture`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. xUnit bootstrap + solution | Green `dotnet test VeloRoute.sln` (0 tests); solution file at repo root | Template `UnitTest1.cs` left in place produces a false-positive on `dotnet test` |
| 2. OrsMapper + Risk #1 tests | `OrsMapper` extracted; 33 mapping tests passing; oracle verified by inspection | Oracle problem: expected value copied from enum definition instead of ORS docs |
| 3. GPX fix + Risk #3 tests | `"G"` → `"R"` fix shipped; 5 locale/structure/integrity tests passing | Locale tests leaking mutated `CurrentCulture` corrupting subsequent tests |

**Prerequisites:** .NET 10 SDK installed; `dotnet new xunit` available  
**Estimated effort:** ~1 session across 3 phases

## Open Risks & Assumptions

- ORS API surface and waytype codes are treated as stable per the ORS documentation; if ORS adds codes beyond 18 (surface) or 8 (road class) in future, the `Enum.IsDefined` guard silently maps them to `Unknown` — acceptable per current test plan scope
- xUnit runs test classes in parallel by default; the locale-injection tests mutate `Thread.CurrentThread.CurrentCulture` — `finally` restore is mandatory

## Success Criteria (Summary)

- `dotnet test VeloRoute.sln` exits 0 from repo root with 38 passing tests
- Each `[InlineData]` comment in `OrsMapperTests.cs` is independently readable against ORS API docs without consulting the production enum
- `GpxSerializer.cs` uses `"R"` format; locale tests restore `CurrentCulture` in a `finally` block
