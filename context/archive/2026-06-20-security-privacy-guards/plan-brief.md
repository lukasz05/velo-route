# Security and Privacy Guards — Plan Brief

> Full plan: `context/changes/security-privacy-guards/plan.md`

## What & Why

Add two integration tests verifying the PRD privacy NFR: no input coordinates appear in
backend logs after a route-generation request, and no API key string appears in error
responses forwarded to the caller. These are regression guards — current code likely passes
both — but they lock the floor against future log-level changes or error-path refactors.

## Starting Point

`VeloRoute.Tests` has an xUnit project with 43 unit tests and Phase 2 integration tests.
The integration test factory (`VeloRouteWebApplicationFactory`) and fake (`FakeOpenRouteServiceClient`)
are `file sealed` inside `LoopRouteIntegrationTests.cs` — inaccessible to a new test file.
No log-capture infrastructure exists in the project.

## Desired End State

`dotnet test` passes with two new tests in `SecurityPrivacyIntegrationTests.cs`:
`PostRoutesLoop_WhenRequestCompletes_LogsContainNoCoordinates` and
`PostRoutesLoop_WhenOrsErrorContainsApiKey_ResponseBodyDoesNotExposeKey`.
`§6.3` in `test-plan.md` is filled with the log-capture and key-leakage cookbook patterns.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Logger capture mechanism | `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogCollector`) | Purpose-built, zero boilerplate vs. custom `ILoggerProvider` | Plan |
| API key injection | Factory constructor `apiKey` param + `AddInMemoryCollection` | Mirrors existing `timeoutSeconds` pattern exactly | Plan |
| Test file placement | New `SecurityPrivacyIntegrationTests.cs` | Matches §6.3 cookbook slot; keeps security concerns isolated | Plan |
| Mock layer for Risk #6 | `FakeOpenRouteServiceClient` with sentinel in error message | Tests that `Program.cs` error-mapping strips ORS message; follows established Phase 2 pattern | Plan |
| Infrastructure refactor | Move factory + fake to shared `TestInfrastructure.cs` | `file sealed` prevents cross-file access; Phase 1 is a pure structural refactor | Plan |

## Scope

**In scope:** NuGet package addition; factory/fake extraction to shared file; `VeloRouteWebApplicationFactory` extension (`apiKey`, `useFakeLogging` params); two integration tests (Risk #4, Risk #6); §6.3 cookbook fill + Phase 3 status update in `test-plan.md`.

**Out of scope:** `POST /routes/gpx` logging tests (no ORS call; coordinates intentionally in output); `HttpMessageHandler`-level HTTP mock; any production code changes.

## Architecture / Approach

Phase 2 factory pattern extended: `VeloRouteWebApplicationFactory` gains two new optional
constructor parameters. `useFakeLogging: true` registers `FakeLogCollector` via
`AddFakeLogging()` in `ConfigureLogging`. `apiKey: "sentinel"` injects the value via
`AddInMemoryCollection`. Both security tests use the shared factory from `TestInfrastructure.cs`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Extract infrastructure | Shared factory + fake in `TestInfrastructure.cs`; factory extended with `apiKey` + `useFakeLogging`; existing tests still green | Removing `file` modifier may surface namespace collisions |
| 2. Security tests | Two new tests; §6.3 filled; Phase 3 marked shipped | Tests could trivially pass without real detection power — manual regression check is mandatory |

**Prerequisites:** F-03 done (already satisfied — Phase 2 integration tests shipped).
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- If either test fails against the current backend, it indicates a real coordinate/key leak in
  production code — requires investigation before the plan is considered complete.
- `Microsoft.Extensions.Diagnostics.Testing` version compatibility with .NET 10 — verify
  during `dotnet add package`.

## Success Criteria (Summary)

- `dotnet test` passes with both new test method names in output.
- Manual regression check confirms Risk #4 test fails when coordinate logging is introduced.
- `§6.3` in `test-plan.md` replaced with concrete cookbook patterns.
