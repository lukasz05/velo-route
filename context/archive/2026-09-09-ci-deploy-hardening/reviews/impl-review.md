<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: CI/Deploy Hardening Implementation Plan

- **Plan**: context/changes/ci-deploy-hardening/plan.md
- **Scope**: Full plan (Phases 1-4 of 4)
- **Date**: 2026-09-10
- **Verdict**: REJECTED (pre-fix) → resolved during triage (both findings fixed)
- **Findings**: 1 critical, 1 warning, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | FAIL (pre-fix) → fixed |
| Architecture | PASS |
| Pattern Consistency | WARNING (pre-fix) → fixed |
| Success Criteria | PASS |

## Findings

### F1 — Kudu response body dumped to CI log unredacted, can leak prod connection string

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/backend.yml:85 (pre-fix)
- **Detail**: `cat /tmp/kudu-cmd.json` ran unconditionally before the exit-code check. Kudu's `/api/command` response `Output`/`Error` fields carry the efbundle shell's stdout/stderr, which on an Npgsql connection failure commonly embeds the full connection string. That string never routes through `secrets.*`, so GitHub's log masking can't redact it — first real `efbundle` failure would print prod Postgres credentials in cleartext to the job log.
- **Fix A ⭐ Recommended**: Redact before printing; only surface scrubbed detail on failure.
  - Strength: Keeps most debugging value while removing the specific string shapes most likely to carry the secret; matches the file's existing "surface curl errors, don't dump raw" pattern (commit 8e88c3a).
  - Tradeoff: Regex scrubbing is inherently imperfect.
  - Confidence: MED.
  - Blind spot: Haven't inventoried real Npgsql auth-failure exception text inside Kudu's Output/Error fields.
- **Fix B**: Never print raw response in job log; push to a workflow artifact instead.
  - Strength: Removes leak surface entirely, strongest guarantee.
  - Tradeoff: Slower debugging (pull artifact vs read log), needs artifact wiring.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED (Fix A) — added a `scrub()` helper (sed-based redaction of `Password=`/`Pwd=` and `postgres(ql)://user:pass@` patterns) and moved the `kudu-cmd.json` print to only run on the failure paths, scrubbed. Commit: pending (uncommitted at review time).

### F2 — Publish-profile secret spliced inline instead of via env:

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: .github/workflows/backend.yml:52 (pre-fix)
- **Detail**: `PROFILE='${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}'` spliced the secret into script text via template expansion, unlike the smoke-test step later in the same file which passes secrets through `env:`.
- **Fix**: Pass `AZURE_WEBAPP_PUBLISH_PROFILE` via `env:` like the smoke-test step does.
- **Decision**: FIXED — added `env: AZURE_WEBAPP_PUBLISH_PROFILE: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}` to the step and switched `USER`/`PASS` extraction to read `$AZURE_WEBAPP_PUBLISH_PROFILE` instead of the inline-templated `$PROFILE`. Commit: pending (uncommitted at review time).

## Verification performed during review

- `dotnet ef migrations has-pending-model-changes` — exit 0
- `DOCKER_API_VERSION=1.41 dotnet test` (src/backend) — 142 passed, 3 skipped (live ORS smoke), 0 failed
- `dotnet ef migrations bundle` — builds successfully
- `az postgres flexible-server firewall-rule list` — confirms `AllowOperatorMigration` removed
- `curl https://velo-route-api.azurewebsites.net/health` — HTTP 200
- `curl https://purple-sky-08f4fb710.7.azurestaticapps.net/` — HTTP 200, non-empty `publishableKey` present
- `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY= npm run build` (src/frontend) — fails fast as expected
- `npm run lint`, `npm test` (src/frontend) — pass (47 tests)
- Plan-drift sub-agent: all 10 planned changes across all 4 phases verdict MATCH, no unplanned files
