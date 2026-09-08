<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Edit Saved Route

- **Plan**: `context/changes/edit-route/plan.md`
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-09-08
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 6 observations

All 12 planned items verified MATCH; all six "not doing" guardrails respected; no
unplanned code changes. Both high-risk points came back correct: the `Optional<T>`
converter's null polarity (`HandleNull => true`; explicit `null` → `HasValue == true`)
and `ExecuteUpdateAsync` with a statement lambda (EF Core 10's
`Action<UpdateSettersBuilder<T>>` overload — legal, and proven by tests that would fail
if setters were silently dropped).

Automated criteria: backend 130 passed / 3 skipped / 0 failed; frontend `tsc` and
`eslint` clean, 45 tests pass.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Process note: Progress rows 2.4/2.5 were marked verified while the running backend
still served 405 for PATCH (stale process). Re-confirmed by the user after a restart.

## Findings

### F1 — PATCH proxy forwards an unparsed body, unlike its POST sibling

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/app/api/routes/[id]/route.ts:32
- **Detail**: `await request.text()` forwards raw bytes. The sibling body-carrying proxy (`api/routes/route.ts:20-25`) parses first and returns `{ error: 'Invalid request body', code: 'INVALID_REQUEST' }` on failure. With malformed JSON the backend answers ASP.NET problem-details, which `apiProxy.ts:29-31` cannot read, so it synthesizes `"Backend returned 400"` — and `page.tsx:137` renders that literal string as the validation error. Reachable only by non-browser callers; the edit form always sends `JSON.stringify` output, which is why manual check 3.7 passed.
- **Fix**: Parse-and-reserialize as POST does, returning `INVALID_REQUEST` on a parse failure.
- **Decision**: FIXED

### F2 — Validation measures trimmed length but persists untrimmed

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/RouteMetadataValidation.cs:22,36 vs src/backend/VeloRoute/Program.cs:256
- **Detail**: `name.Trim().Length > MaxNameLength` gates on a trimmed string, but neither the save nor the PATCH path trims before writing. 120 chars wrapped in 200 spaces passes and lands in the unconstrained `text` column at 320 chars — the enforced contract is not the stated one. Matches the plan's "<= MaxNameLength after trimming" wording and mirrors pre-existing POST behaviour, so this is inherited, not new.
- **Fix A ⭐ Recommended**: Have `Validate` return the normalized (trimmed) values and write those at both call sites.
  - Strength: Makes the stored value match the advertised cap; one place to change, both paths benefit.
  - Tradeoff: Signature change ripples to both callers and their tests.
  - Confidence: HIGH — both call sites are in Program.cs and visible.
  - Blind spot: Haven't checked whether any existing row relies on leading/trailing whitespace.
- **Fix B**: Leave as-is, document the trim-for-check/store-raw semantics.
  - Strength: Zero risk; preserves exact POST behaviour shipped in S-02.
  - Tradeoff: The 120-char cap stays nominal rather than enforced.
  - Confidence: MEDIUM — fine until someone adds a DB length constraint.
  - Blind spot: None significant.
- **Decision**: FIXED

### F3 — Optional&lt;T&gt;.Write collapses "absent" into "explicit null"

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Optional.cs:56
- **Detail**: The branch tests `value.Value is null`, not `HasValue`. Serializing `default(Optional<string[]?>)` (absent) emits `"tags": null`, which on the way back in means "clear the tags" — precisely the absent-vs-null collapse this type exists to prevent. No live impact: `Optional` appears only on the inbound request DTO and is never serialized out today. `OptionalTests.cs:56-63` locks the current behaviour in rather than flagging it.
- **Fix**: Branch on `HasValue` and throw when writing an absent `Optional`, so a future round-trip fails loudly instead of clearing data.
- **Decision**: FIXED

### F4 — ExecuteUpdateAsync row count discarded (narrow TOCTOU)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:254
- **Detail**: If the route is deleted between the ownership read (:236) and the update (:254), zero rows change and the handler still returns 204, telling the caller the write succeeded.
- **Fix**: Capture the affected-row count and return 404 when it is 0.
- **Decision**: FIXED

### F5 — Stale-tab 404 shows a retry message instead of redirecting

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/frontend/src/app/my-routes/[id]/page.tsx:146
- **Detail**: `handleSaveEdit` treats every non-400 failure alike. `handleConfirmDelete` redirects to `/my-routes` on 404; `handleStopSharing` tolerates 404 explicitly. The plan's manual step 6 ("404 handled without a crash") is satisfied, but the user retries forever.
- **Fix**: Mirror the delete path — redirect to `/my-routes` on 404.
- **Decision**: FIXED

### F6 — Full entity read (incl. jsonb geometry) for a metadata-only update

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute/Program.cs:236
- **Detail**: The ownership check materializes the whole `Route` including the `jsonb` Geometry column — potentially thousands of coordinate pairs — while the handler uses only `Name` and `Tags`. DELETE needs the tracked entity to `Remove()` it; PATCH does not, since it writes via `ExecuteUpdateAsync`.
- **Fix**: `.Select(r => new { r.Name, r.Tags })` on the existence check.
- **Decision**: FIXED

### F7 — Test naming and NULL-vs-[] semantics undocumented

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: src/backend/VeloRoute.Tests/Routing/EditRouteTests.cs:163-175
- **Detail**: `Patch_TagsEmptyArray_ClearsTags` asserts `Assert.Empty(updated.Tags!)` — `[]` is persisted, not cleared to NULL. The `Tags` column now holds three states (NULL, `{}`, populated) with no normalization, and `parseTags` always yields `[]`, so the UI can never produce NULL. Also missing relative to `SaveRouteTests`: no PATCH-path coverage for a single tag exceeding `MaxTagLength`.
- **Fix**: Rename to `..._PersistsEmptyArray` and add the missing over-long-tag case.
- **Decision**: FIXED

### F8 — Three new CS8604 nullability warnings in the test project

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/backend/VeloRoute.Tests/OptionalTests.cs:43, EditRouteTests.cs:143,272
- **Detail**: `Assert.Equal(expected, updated.Tags!)` passes a possibly-null array. The main project builds 0-warning; these three are new to this change, in a repo with `<Nullable>enable</Nullable>`.
- **Fix**: Assert non-null first, then compare.
- **Decision**: FIXED

### F9 — New files sit at project root, outside folder-per-namespace convention

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/backend/VeloRoute/Optional.cs, src/backend/VeloRoute/RouteMetadataValidation.cs
- **Detail**: Every other backend type lives in a folder matching its namespace (`Auth/`, `Data/`, `Routing/`). Both new files are bare `namespace VeloRoute` at the project root, forcing `using VeloRoute;` in Program.cs. The plan specified those exact paths, so this is plan-sanctioned, not drift.
- **Fix**: Move `RouteMetadataValidation` to `Routing/` and `Optional` to a `Json/` folder.
- **Decision**: FIXED
