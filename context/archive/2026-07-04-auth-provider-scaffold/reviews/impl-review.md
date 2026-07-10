<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Auth Provider Scaffold — Clerk + .NET JWT middleware

- **Plan**: context/changes/auth-provider-scaffold/plan.md
- **Scope**: Phase 1-3 of 3 (full plan)
- **Date**: 2026-07-07
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 1 observation

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

### F1 — azp check fails open if AllowedAzp config key is ever absent (not just empty)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:66
- **Detail**: `if (azp != allowed)` — if `Clerk:AllowedAzp` were ever entirely absent from config (not just `""`), `builder.Configuration[...]` returns null. A token with no azp claim also reads null. `null != null` is false in C#, so `context.Fail` never fires — the check silently passes. Not exploitable today: `appsettings.json` always defines `"AllowedAzp": ""` as a non-null sentinel, and confirmed live that a bogus token + empty config still 401s. But it fails closed by accident, not by construction.
- **Fix**: Change to `if (string.IsNullOrEmpty(allowed) || azp != allowed)` so a missing/misconfigured AllowedAzp fails closed structurally.
- **Decision**: FIXED

### F2 — /auth/probe 401 response shape diverges from existing error convention

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute/Program.cs (auth/probe endpoint)
- **Detail**: `/routes/loop` and `/routes/gpx` return `{ error, code }` JSON on failure. `/auth/probe`'s 401 (verified live) returns an empty body with just a `WWW-Authenticate` header — the ASP.NET Core default challenge response, not this codebase's error shape. Low severity since it's an explicitly dev-only smoke endpoint, not real API surface.
- **Fix**: Leave as-is for now (dev-only, no client depends on the shape); align to `{error, code}` only if `/auth/probe` or similar auth-gated endpoints become real API surface later.
- **Decision**: FIXED — user opted to align now via `OnChallenge` returning `{ error: "Unauthorized", code: "UNAUTHORIZED" }` instead of deferring.
