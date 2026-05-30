<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: routing-api-wiring

- **Plan**: context/changes/routing-api-wiring/plan.md
- **Scope**: All Phases (1–3 of 3)
- **Date**: 2026-05-30
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical  4 warnings  2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Raw exception message surfaced in HTTP response

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/Routing/OpenRouteServiceClient.cs:76–79 / Program.cs:55–57
- **Detail**: `ex.Message` flowed into RoutingError then into `Results.Problem(502)`, leaking internal hostnames/ORS URLs. Pattern would persist into production routes.
- **Fix A ⭐ Applied**: Log ex at Error level (`_logger.LogError(ex, ...)`), return fixed string `"Routing provider unavailable"`.
  - Strength: Eliminates leak class; matches all other fixed-string error paths.
  - Tradeoff: Need logs to debug — full exception is there.
  - Confidence: HIGH
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — commit 2931511

### F2 — 429 responses retried, burning ORS quota

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/Program.cs:33–41
- **Detail**: `AddStandardResilienceHandler` didn't exclude 429 by default. With MaxRetryAttempts=2, a rate-limited call fired 3 ORS requests total. Plan required excluding 401, 403, 429.
- **Fix A ⭐ Applied**: Override `options.Retry.ShouldHandle` to only retry on `HttpStatusCode.RequestTimeout` or `>= HttpStatusCode.InternalServerError`.
  - Strength: Satisfies plan contract; prevents quota burn.
  - Confidence: HIGH
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — commit 2931511

### F3 — OperationCanceledException swallowed, callers get 502

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/Routing/OpenRouteServiceClient.cs:71–75
- **Detail**: ALL OperationCanceledException caught and returned as RoutingError. Client disconnects swallowed rather than propagated.
- **Fix**: Added `if (cancellationToken.IsCancellationRequested) throw;` before returning Failure.
- **Decision**: FIXED — commit 2931511

### F4 — Unplanned frontend config/tooling changes

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/frontend/src/app/layout.tsx, eslint.config.mjs, package.json, tsconfig.json
- **Detail**: Four files changed outside the plan — all forced adaptations (corporate SSL, ESLint 9 compat).
- **Fix**: Added "Forced Adaptations" addendum section to plan.md.
- **Decision**: FIXED — commit 2931511

### F5 — VELO_API_URL undefined produces silent broken URL

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/frontend/src/lib/routingApi.ts:4–5
- **Detail**: Missing env var produced "undefined/routes/preview" URL with confusing error message.
- **Fix**: Added explicit `if (!process.env.VELO_API_URL) throw new Error('VELO_API_URL is not set')` guard.
- **Decision**: FIXED — commit 2931511

### F6 — Dev page not environment-gated

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/app/dev/page.tsx
- **Detail**: Backend /routes/preview gated by IsDevelopment(); frontend /dev page had no matching guard. Also echoed raw err.message.
- **Fix**: Added `if (process.env.NODE_ENV !== 'development') notFound();`, replaced err.message with fixed string, changed `catch (err)` to `catch`.
- **Decision**: FIXED — commit 2931511
