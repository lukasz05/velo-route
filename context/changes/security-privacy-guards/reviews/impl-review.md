<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Security and Privacy Guards

- **Plan**: context/changes/security-privacy-guards/plan.md
- **Scope**: Phase 1–2 of 2
- **Date**: 2026-06-20
- **Verdict**: APPROVED (post-triage)
- **Findings**: 0 critical  5 warnings  1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — §6.3 cookbook documents API that doesn't exist in the project

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Pattern Consistency
- **Location**: context/foundation/test-plan.md:225–232
- **Detail**: Plan specified FakeLogCollector/AddFakeLogging(). Implementation used hand-rolled TestLogSink instead. §6.3 still documented FakeLogCollector.GetSnapshot() — an API that didn't exist in the project. Future implementors following §6.3 would reference a non-existent API. Root cause consistent across TestInfrastructure.cs (wired TestLogSink not AddFakeLogging), SecurityPrivacyIntegrationTests.cs (read factory.LogSink!.Messages not FakeLogCollector.GetSnapshot()), and test-plan.md §6.3 (documented FakeLogCollector API that was never used).
- **Decision**: FIXED via Fix B — reverted to FakeLogCollector. Removed TestLogSink; wired AddFakeLogging() in factory; updated test to use GetRequiredService<FakeLogCollector>().GetSnapshot(). §6.3 cookbook now matches implementation. Namespace: Microsoft.Extensions.Logging.Testing (from Microsoft.Extensions.Diagnostics.Testing package).

### F2 — TestLogSink._messages is not thread-safe

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:13
- **Detail**: TestLogSink._messages was a plain List<string>. ASP.NET Core logging pipeline can invoke ILogger.Log from multiple threads; concurrent Add() on List<T> is undefined behaviour.
- **Decision**: FIXED (resolved by F1 Fix B — TestLogSink was removed entirely; FakeLogCollector is thread-safe).

### F3 — Unplanned production changes in Program.cs

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Scope Discipline
- **Location**: src/backend/VeloRoute/Program.cs:41,87
- **Detail**: Two changes committed under this branch that the plan explicitly prohibited ("Modifying production code — explicitly NOT in scope"). Change 1 (line 41): CircuitBreaker.SamplingDuration 10s → 30s. Change 2 (line 87): error response body hardened from result.Error.Message to "Route generation failed" — this IS the Risk #6 security fix; without it the key-leakage test would have failed. Plan's claim "current code likely passes both" was incorrect. Change 2 is a breaking API contract change: callers parsing error.message now always get a generic string. Frontend audit confirmed: all three usages (routingApi.ts:38, RouteApp.tsx:44, gpx/route.ts:27) read error for display only — no branching on its value.
- **Decision**: FIXED via Fix A — accepted both changes. Frontend audit confirmed no branching on error field.

### F4 — Microsoft.Extensions.Diagnostics.Testing package seemingly unused

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj:13
- **Detail**: Package appeared unused after pivot to TestLogSink; AddFakeLogging() and FakeLogCollector live in Microsoft.Extensions.Logging.Testing namespace inside Microsoft.Extensions.Diagnostics.Testing.dll — same package, different namespace.
- **Decision**: DISMISSED — package is correct and necessary. Provides AddFakeLogging()/FakeLogCollector under Microsoft.Extensions.Logging.Testing namespace.

### F5 — Log-capture test can pass vacuously (no sink trip-wire)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/Routing/SecurityPrivacyIntegrationTests.cs:35
- **Detail**: DoesNotContain assertions on an empty FakeLogCollector snapshot would pass trivially if the collector failed to wire up. Test would give false green.
- **Fix**: Add Assert.NotEmpty(snapshot) before DoesNotContain assertions.
- **Decision**: FIXED — added Assert.NotEmpty(snapshot) before DoesNotContain assertions.

### F6 — API-key test doesn't assert status code before body check

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/Routing/SecurityPrivacyIntegrationTests.cs:41
- **Detail**: Test read the body and asserted DoesNotContain(sentinel) without first asserting the expected HTTP status code. A 200 with empty body would pass trivially.
- **Fix**: Add Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode) before body read.
- **Decision**: FIXED — added status code assertion (502 BadGateway for PROVIDER_ERROR).
