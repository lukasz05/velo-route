<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Account Deletion (S-06)

- **Plan**: context/changes/account-deletion/plan.md
- **Scope**: Phase 1 of 4 (full plan)
- **Date**: 2026-07-26
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 1 observation

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

### F1 — Escape key bypasses in-flight-delete guard

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability/UX)
- **Location**: src/frontend/src/components/ConfirmModal.tsx:24
- **Detail**: `handleKeyDown` calls `onCancel()` unconditionally on Escape, while the Cancel button is `disabled={isConfirming}`. Pre-existing gap (not introduced this phase), but now guards the delete-account flow specifically: pressing Escape mid-delete unmounts the modal while the in-flight `DELETE /account` fetch in `account/page.tsx` keeps running — on success it still calls `signOut()` and redirects with the deleted banner, even though the user believed they'd cancelled.
- **Fix**: Gate the Escape handler behind `!isConfirming`, matching the Cancel button's disabled condition.
- **Decision**: FIXED

### F2 — Missing `finally` reset in account/page.tsx

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/app/account/page.tsx:38
- **Detail**: `my-routes/page.tsx` resets its deleting-flag in a `finally` block; `account/page.tsx` resets `isDeleting`/`showConfirm` only in `catch`. Functionally equivalent here (success path navigates away before state would matter), but diverges from the established sibling pattern.
- **Fix**: Move `isDeleting` reset into a `finally` block for consistency.
- **Decision**: FIXED

### F3 — FakeClerkClient.DeleteResult=false path untested

- **Severity**: 👁 OBSERVATION
- **Dimension**: Success Criteria / Test Coverage
- **Location**: src/backend/VeloRoute.Tests/Routing/AccountDeletionTests.cs
- **Detail**: Only `ThrowOnDelete` is exercised; the graceful-false return path (Clerk answers non-2xx/non-404 without throwing) has no test. Low-value since the endpoint discards the bool either way, but it's an untested branch of `ClerkClient`'s contract.
- **Fix**: Added `Delete_ClerkReturnsFalse_StillReturns204AndPostgresDeleteCommits` test case.
- **Decision**: FIXED

## Confirmed clean

- Endpoint scoping: `DELETE /account` derives target solely from `user.GetSub()`, no id param, no cross-account risk.
- `Clerk:SecretKey` never logged; frontend proxy never sees it (only relays user's own bearer token).
- Postgres-delete-before-Clerk-call ordering matches design; outer `catch` swallows any Clerk exception, `204` always returned once Postgres commits.
- FK cascade (`Route→Users`, `Share→Routes`, both `Cascade`) verified against real Postgres via `Delete_ValidAccount_Returns204AndCascadesRoutesAndShares`.
- Typed-confirmation gating correct; no stale-`typedValue` risk since modal fully unmounts/remounts on each open.
- `ClerkClient` matches `OpenRouteServiceClient`'s `HttpClient`+`ILogger<T>` DI shape.
- `account/page.tsx` matches `my-routes/page.tsx`'s auth-gate-redirect and inline-error conventions.
- No scope creep: no soft-delete, no admin/bulk deletion, no account-settings framework beyond email+delete, no background job, no new toast system.
- All automated checks pass: backend `dotnet build` (0 warnings) + `dotnet test` (98 passed, 3 skipped live-ORS), frontend `npm run build` + `npm run lint` (clean) + `npm test` (38 passed).
