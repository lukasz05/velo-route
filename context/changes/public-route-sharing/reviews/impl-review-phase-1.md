<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Public Route Sharing (S-05)

- **Plan**: context/changes/public-route-sharing/plan.md
- **Scope**: Phase 1 of 2
- **Date**: 2026-07-26
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 1 warning, 3 observations

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

### F1 — Unique-violation catch can't tell which index fired

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:231-235
- **Detail**: `Shares` has two unique indexes (`RouteId`, `Token`). The catch clause assumes any `23505` violation is the planned RouteId race and falls back to `db.Shares.SingleAsync(s => s.RouteId == id, ct)`. Correct for the plan's race, but on a (71-bit, astronomically unlikely) Token collision with a different route, no row exists yet for `id`, so `SingleAsync` throws `InvalidOperationException` → unhandled 500 instead of retrying with a new token.
- **Fix A ⭐ Recommended**: Swap `SingleAsync` for `SingleOrDefaultAsync`; if null, the violation was actually a Token collision — regenerate the token and retry the insert once.
  - Strength: Handles both unique constraints correctly without needing to inspect Npgsql constraint names.
  - Tradeoff: A few more lines; a retry loop even though it'll essentially never execute.
  - Confidence: HIGH — straightforward, matches the plan's existing re-query-on-catch pattern.
  - Blind spot: None significant.
- **Fix B**: Branch on `ex.InnerException.As<PostgresException>().ConstraintName` (`IX_Shares_RouteId` vs `IX_Shares_Token`).
  - Strength: Most precise — no ambiguity about which invariant was violated.
  - Tradeoff: Couples the code to Npgsql's constraint-naming convention; more code for a case this rare.
  - Confidence: MED — correct today, but a naming-convention change in a future migration could silently break the check.
  - Blind spot: Haven't verified EF's default constraint-naming is guaranteed stable across EF Core versions.
- **Decision**: FIXED (Fix A applied)

### F2 — No "share/unshare owned by different user" test

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute.Tests/Routing/ShareRouteTests.cs:28
- **Detail**: `DeleteRouteTests.cs` and `RouteLibraryTests.cs` both have a dedicated "owned by a different real user" 404 case guarding the `r.UserId == sub` predicate. `ShareRouteTests.cs` only tests a nonexistent route ID for share/unshare, not a route owned by someone else.
- **Fix**: Add a case seeding a route owned by a second user and asserting share/unshare both 404, mirroring `Delete_OwnedByDifferentUser_Returns404AndLeavesRowUntouched`.
- **Decision**: FIXED (`ShareAndUnshare_OwnedByDifferentUser_Returns404` added)

### F3 — GET /shares/{token} does two round trips

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:263-269
- **Detail**: Sequential `Shares` lookup then `Routes` lookup instead of one joined query. Not N+1, negligible at this scale.
- **Fix**: Optional — fold into a single query with a join/select if it ever shows up in profiling. Not worth doing now.
- **Decision**: SKIPPED

### F4 — Failed insert leaves entity tracked

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:225-235
- **Detail**: On unique-violation, the failed `share` stays tracked as `Added` on `db`'s change tracker. Harmless today — no further `SaveChangesAsync` happens on that context in this request scope.
- **Fix**: Not needed now; if this code path grows, add `db.Entry(share).State = EntityState.Detached;` in the catch.
- **Decision**: NO_CHANGE_NEEDED (already resolved as a side effect of the F1 fix)
