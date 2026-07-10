<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Route Generation Integration Tests

- **Plan**: context/changes/route-generation-integration-tests/plan.md
- **Scope**: All Phases (1–4)
- **Date**: 2026-06-20
- **Verdict**: NEEDS ATTENTION (resolved via triage)
- **Findings**: 0 critical  2 warnings  2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Timeout test timing assertion may flake on slow CI

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs:187
- **Detail**: `sw.ElapsedMilliseconds < 400` includes WebApplicationFactory startup (cold init can be 200–400 ms on loaded CI). Functional correctness already proven by 504 status + "TIMEOUT" body assertions.
- **Fix A ⭐ Applied**: Loosened to `< 2000 ms` with hang-guard comment. Zero behavior change to correctness; eliminates flake risk.
- **Decision**: FIXED via Fix A

### F2 — AttemptTimeout (5 s Polly) decoupled from configurable ORS deadline

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:44
- **Detail**: `AttemptTimeout.Timeout = 5 s` is wider than default `TimeoutSeconds = 4.5 s`, making it dead code at default. If an operator raises `TimeoutSeconds > 5`, Polly retries activate unexpectedly. Discussed: `AttemptTimeout` doesn't make sense alongside the outer CTS pattern used here.
- **Fix Applied**: Removed `options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5)` entirely. Outer CTS is the sole timeout authority.
- **Decision**: FIXED (removed AttemptTimeout)

### F3 — Queue<T> in FakeOpenRouteServiceClient under concurrent Task.WhenAll

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs:12
- **Detail**: `Queue<T>` is not thread-safe; 3 concurrent `GetDirectionsAsync` calls from `Task.WhenAll` each dequeue. Safe in practice (synchronous at Delay=0) but a theoretical race.
- **Fix Applied**: Replaced `Queue<T>` with `ConcurrentQueue<T>`; swapped `using System.Collections.Generic` → `using System.Collections.Concurrent`.
- **Decision**: FIXED

### F4 — Explicit `using System.Collections.Generic` vs implicit usings

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs:1
- **Detail**: Sibling `OrsMapperTests.cs` has no explicit `using System.*` imports. Minor convention drift.
- **Decision**: FIXED (resolved automatically by F3 fix — explicit generic using replaced by concurrent using)
