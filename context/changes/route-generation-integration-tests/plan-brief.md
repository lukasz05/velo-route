# Route Generation Integration Tests — Plan Brief

> Full plan: `context/changes/route-generation-integration-tests/plan.md`

## What & Why

Add integration tests covering test-plan Phase 2: Risk #2 (LoopRouteGenerator must produce
routes within [min_km, max_km] and ≤ 10% overlap) and Risk #5 (the 4.5 s ORS deadline must
fire and return a 504 rather than hanging). These are the last outstanding backend risks
before S-03 quality tuning can begin with a reliable regression gate.

## Starting Point

F-02 delivered 43 passing xUnit unit tests covering ORS enum mapping and GPX serialisation.
`IOpenRouteServiceClient` is an injectable interface; `InternalsVisibleTo` already grants test
access. The 4.5 s deadline is hardcoded in `Program.cs`; no HTTP testing package exists yet.

## Desired End State

`dotnet test src/backend/` passes with ≥ 5 new integration tests verifying the distance/overlap
constraints and the timeout response — giving S-03 a reliable regression gate so any waypoint
geometry change can be verified automatically against PRD business-logic constraints.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Mock layer | Custom `IOpenRouteServiceClient` fake | Avoids dual-concern failures; parsing already covered by F-02 unit tests | Plan |
| Test scope | `WebApplicationFactory<Program>` | Tests full pipeline (validation → generator → HTTP code) — the "end-to-end" scope within test-plan §7 limits | Plan |
| Timeout configurability | Add `ORS:TimeoutSeconds` to `OpenRouteServiceOptions` | Timeout tests must run in < 400 ms, not 4.5 s | Plan |
| Overlap geometry | Synthetic 13-coord out-and-back | Deterministic; avoids `OverlapDetector` adjacency guard (`j <= i + 5`) | Plan |

## Scope

**In scope:**
- `ORS:TimeoutSeconds` config property + `Program.cs` wiring
- `public partial class Program {}` for WebApplicationFactory entry point
- `Microsoft.AspNetCore.Mvc.Testing` package
- `FakeOpenRouteServiceClient` + `VeloRouteWebApplicationFactory` test helpers
- 5 integration tests (4× Risk #2, 1× Risk #5)
- test-plan §6.2 cookbook + Phase 2 status update

**Out of scope:**
- ORS HTTP response parsing tests (covered by F-02)
- Live ORS calls (test-plan §7 exclusion)
- Input validation edge cases (`minKm`/`maxKm` bounds) — Program.cs concern, not generator contract
- Frontend tests

## Architecture / Approach

`WebApplicationFactory<Program>` spins up the ASP.NET Core app in-process. A `FakeOpenRouteServiceClient` (queue-based, configurable delay) is substituted for the real `IOpenRouteServiceClient` via `ConfigureServices`. Constraint tests queue deterministic `RouteResult` values; the timeout test sets `ORS:TimeoutSeconds = 0.1` and delays 500 ms in the fake. All tests POST to `/routes/loop` and assert HTTP status codes and response body fields.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Configurable deadline | `ORS:TimeoutSeconds` in config; `public partial class Program {}` | `IOptions<>` injection at endpoint level slightly changes Program.cs signature |
| 2. Test infrastructure | `Microsoft.AspNetCore.Mvc.Testing` + factory + fake | DI override order — must remove HttpClient-backed registration before adding singleton |
| 3. Integration tests | 5 passing tests covering Risk #2 + Risk #5 | Overlap geometry must have ≥ 6 index gap between antiparallel segments |
| 4. Docs | §6.2 cookbook + Phase 2 status | None |

**Prerequisites:** F-02 shipped (done), xUnit running (`dotnet test src/backend/` green)
**Estimated effort:** ~1 session across 4 phases

## Open Risks & Assumptions

- `Microsoft.AspNetCore.Mvc.Testing` version must match `TargetFramework net10.0` — use `10.0.7` to match existing `Microsoft.AspNetCore.OpenApi` version
- `WebApplicationFactory` requires the test project to reference `Microsoft.NET.Sdk.Web` or that the entry-point assembly is a web assembly — verify build after adding the package

## Success Criteria (Summary)

- `dotnet test src/backend/` passes with ≥ 48 tests (43 existing + 5 new), all green
- Timeout test completes in < 400 ms
- test-plan §6.2 no longer reads `TBD`
