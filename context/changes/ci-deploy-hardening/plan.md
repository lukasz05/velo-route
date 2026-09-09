# CI/Deploy Hardening Implementation Plan

## Overview

Three misconfigurations shipped through green CI in the same session (2026-09-09): an empty `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` baked into the frontend build, missing `Clerk__*` App Service settings on the backend, and a pending EF migration (`AddShares`) that was never applied to production Postgres. All three were invisible to CI because the pipelines only prove "builds and uploads" — never "the deployed app actually works," and migrations were never applied outside `IsDevelopment()`. This plan closes those three specific gaps: migrations run automatically as part of deploy, missing required config fails the app/build loudly instead of silently, and a post-deploy check proves the live deployment actually works.

## Current State Analysis

- `src/backend/VeloRoute/Program.cs:107-112` — `Database.Migrate()` only runs when `IsDevelopment()`. `.github/workflows/backend.yml`'s `deploy` job (`publish` → `azure/webapps-deploy@v3`) never applies migrations. Only `InitialCreate` (2026-07-10) had ever landed on prod; `AddShares` (2026-07-26) sat pending for 6 weeks until this session's manual fix.
- `Program.cs:74-102` configures `AddJwtBearer` from `Clerk:Authority` / `Clerk:AllowedAzp`, both of which were entirely absent from the `velo-route-api` App Service settings — every authenticated request 401'd with no diagnostic beyond a generic `{"error":"Unauthorized"}`.
- `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`'s `build_and_deploy_job` now passes `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` via `env:` (fixed 2026-09-09, PR #24) — but nothing validates the value is non-empty; an empty secret would silently re-ship the same bug.
- No IaC (no Bicep/Terraform) — both Azure resources are CLI/portal-managed. Neither workflow authenticates to Azure beyond narrow, purpose-built deploy credentials (`AZURE_STATIC_WEB_APPS_API_TOKEN_PURPLE_SKY_08F4FB710`, `AZURE_WEBAPP_PUBLISH_PROFILE`) — no `azure/login`, no service principal, no OIDC federation. The repo owner cannot create Azure AD app registrations, so this plan must not require one.
- Postgres (`velo-route-db`, flexible server) firewall currently allows only `AllowAllAzureServicesAndResourcesWithinAzureIps` and a stale single-IP rule `AllowOperatorMigration` (`88.156.211.82`, unrelated to any current operator). GitHub Actions' hosted-runner IP allowlist (`api.github.com/meta` → `actions`) is 6,980 CIDR ranges — not a viable firewall allowlist.
- `velo-route-api` is `kind: app,linux`, `linuxFxVersion: DOTNETCORE|10.0` — a Linux App Service, meaning it already has a Kudu/SCM site reachable at `velo-route-api.scm.azurewebsites.net`, authenticated with the same credentials already encoded in the `AZURE_WEBAPP_PUBLISH_PROFILE` secret used for deploy.
- `src/backend/dotnet-tools.json` already pins `dotnet-ef 10.0.9` as a local tool — `dotnet tool restore` makes it available, no new install needed.
- The verified production Clerk publishable key is `pk_test_...` (test-mode instance, not `pk_live_`) — out of scope to change, but it means Clerk's documented `+clerk_test` email convention (fixed verification code `424242`, no real inbox) works against this "production" instance, which Phase 3's smoke test relies on.
- `src/frontend/package.json`'s `build` script is plain `"next build"` with no pre-build validation.

## Desired End State

- A push to `main` that changes `src/backend/**` runs the pending EF migration against production Postgres automatically, before the new binary deploys — via Kudu, using only credentials the pipeline already has. A destructive-looking failure in that step fails the deploy job outright and leaves the old binary running.
- `dotnet ef migrations has-pending-model-changes` runs in the `test` job — a model change without a matching migration fails CI at PR time, not silently at deploy time.
- The backend refuses to start in any non-Development environment if `Clerk:Authority`, `Clerk:AllowedAzp`, or `Clerk:SecretKey` are unset — the App Service's health probe fails immediately and loudly instead of 401ing every real request indefinitely.
- The frontend build fails if `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is empty — the exact bug from PR #24 cannot silently reoccur.
- Both deploy workflows verify the live, deployed artifact after shipping: backend checks `/health` plus one real authenticated round-trip; frontend checks a 200 response with a non-empty `publishableKey` in the served bundle.
- The stale `AllowOperatorMigration` firewall rule is removed — the Kudu approach needs zero standing Postgres firewall rules beyond what already exists.
- `src/backend/VeloRoute/README.md` and the repo's root docs describe the new migration/config-check behavior; no doc claims a state that no longer exists.

### Key Discoveries:

- `Microsoft.EntityFrameworkCore.EF.IsDesignTime` (static bool) is true during any `dotnet ef` invocation (`migrations add`, `has-pending-model-changes`, `migrations bundle`) because EF's tooling executes the app's top-level statements up through `builder.Build()` to introspect the model. A fail-fast check placed before `Build()` must guard on `!EF.IsDesignTime` as well as `!builder.Environment.IsDevelopment()`, or every `dotnet ef` invocation — in CI and for any local developer — breaks the moment `Clerk:*` secrets aren't present in that shell.
- `dotnet ef migrations bundle` produces a self-contained executable that runs migrations via its own Npgsql connection — it does not go through `Program.cs`'s HTTP pipeline or `app.Run()`, so it needs no Clerk config at all to execute (only to be *built*, where the same `EF.IsDesignTime` guard applies).
- The App Service's existing `ConnectionStrings__Default` app setting is exposed as an environment variable inside the Kudu/SCM container the same way it's exposed to the running app — the migration bundle can read it directly with no new secret.

## What We're NOT Doing

- Not setting up OIDC or a service principal for Azure AD — explicitly ruled out by the repo owner's permission constraints.
- Not building auto-rollback for a failed post-deploy smoke test — failure alerts (fails the workflow), no automatic revert.
- Not adding a manual-approval gate before migrations run — stays fully automatic, consistent with the rest of this pipeline.
- Not building automatic additive-vs-destructive migration classification — out of scope; the model-drift guard and migrate-before-deploy ordering are the safety net instead.
- Not fixing the underlying `pk_test_` vs `pk_live_` Clerk instance question — noted as a discovery, not addressed here.
- Not touching `context/foundation/roadmap.md` unless a matching item is found during archive (none is expected; this is infra hardening, not a roadmap slice).

## Implementation Approach

Four phases, ordered by the priority the repo owner set (migration-in-CI and fail-fast config checks are must-have; smoke tests should-have; doc sync nice-to-have) and by dependency (Phase 3's backend smoke test wants Phase 2's fail-fast checks already in place so a real config regression surfaces at startup, not just at smoke-test time). Phases 1 and 2 are independent of each other and could ship in either order; 1 is listed first because it directly reverses today's live incident.

## Critical Implementation Details

**EF design-time guard.** Every `dotnet ef` command used anywhere in this plan (the CI model-drift check, migration bundle creation) executes `Program.cs` up through `builder.Build()`. Phase 2's fail-fast check MUST be written as `if (!builder.Environment.IsDevelopment() && !EF.IsDesignTime)` — omitting the `EF.IsDesignTime` guard breaks Phase 1's CI steps and any developer's local `dotnet ef migrations add`.

**Kudu execution environment.** The Kudu command API (`POST https://velo-route-api.scm.azurewebsites.net/api/command`) authenticates with Basic Auth using the `userName`/`userPWD` attributes of the `MSDeploy` `<publishProfile>` node inside the `AZURE_WEBAPP_PUBLISH_PROFILE` secret's XML — extract them in a workflow step, never echo them. The command executes inside the site's Linux container, which already has the app's App Settings (including `ConnectionStrings__Default`) injected as environment variables.

## Phase 1: Migration-in-CI via Kudu

### Overview

Close the gap that let `AddShares` sit unapplied for 6 weeks: CI now applies pending EF migrations to production automatically, via Kudu, before the new binary deploys.

### Changes Required:

#### 1. Model-drift guard in the test job

**File**: `.github/workflows/backend.yml`

**Intent**: Fail CI at PR time if the EF model has diverged from the latest migration — the authoring-time version of today's bug (a schema change shipped without its migration ever being written).

**Contract**: Add a `dotnet tool restore` step (consumes `src/backend/dotnet-tools.json`, already pins `dotnet-ef 10.0.9`) followed by `dotnet ef migrations has-pending-model-changes --project VeloRoute/VeloRoute.csproj` run from `src/backend/`, with `ASPNETCORE_ENVIRONMENT` left unset (the `EF.IsDesignTime` guard from Phase 2 makes this safe without any Clerk secrets present). Non-zero exit fails the job.

#### 2. Migrate-then-deploy step in the deploy job

**File**: `.github/workflows/backend.yml`

**Intent**: Apply any pending migration to production Postgres before the new binary ships, so deployed code never runs against a schema it doesn't expect.

**Contract**: New step between `dotnet publish` and `azure/webapps-deploy@v3`, gated behind the same `needs: test` / main-push condition as the rest of the `deploy` job:
1. `dotnet tool restore` (if not already run in this job).
2. `dotnet ef migrations bundle --configuration Release --self-contained -r linux-x64 --project VeloRoute/VeloRoute.csproj -o efbundle-output/efbundle`.
3. Extract Kudu Basic Auth credentials from `secrets.AZURE_WEBAPP_PUBLISH_PROFILE` (parse the `MSDeploy` `<publishProfile>` node's `userName`/`userPWD`).
4. `PUT` the bundle to Kudu VFS: `https://velo-route-api.scm.azurewebsites.net/api/vfs/site/deployments/tools/efbundle`.
5. `POST` to `https://velo-route-api.scm.azurewebsites.net/api/command` with body `{"command": "chmod +x efbundle && ./efbundle --connection \"$ConnectionStrings__Default\"", "dir": "site/deployments/tools"}`.
6. Fail the step (and therefore the job, blocking the subsequent deploy step) on a non-zero exit code or HTTP error from the Kudu response.

No new GitHub secret — reuses `AZURE_WEBAPP_PUBLISH_PROFILE`, already present.

#### 3. Remove the stale firewall rule

**File**: N/A (Azure resource, `velo-route-db` flexible server)

**Intent**: The Kudu approach needs zero standing Postgres firewall exposure beyond what already exists (`AllowAllAzureServicesAndResourcesWithinAzureIps`) — the leftover `AllowOperatorMigration` single-IP rule (pointing to an unrelated stale IP) is now unnecessary.

**Contract**: `az postgres flexible-server firewall-rule delete --resource-group velo-route-rg --server-name velo-route-db --name AllowOperatorMigration --yes`.

### Success Criteria:

#### Automated Verification:

- Model-drift check passes on current `main`: `dotnet ef migrations has-pending-model-changes --project VeloRoute/VeloRoute.csproj` from `src/backend/` exits 0
- Backend test suite still green: `dotnet test` from `src/backend/`
- Migration bundle builds: `dotnet ef migrations bundle --configuration Release --self-contained -r linux-x64 --project VeloRoute/VeloRoute.csproj -o /tmp/efbundle-test/efbundle` from `src/backend/`

#### Manual Verification:

- On a real push to `main` touching `src/backend/**`, the deploy job's migration step completes successfully and the Kudu command returns a success exit code
- After that deploy, `dotnet ef migrations list --project VeloRoute/VeloRoute.csproj --connection "<prod-connection-string>"` (run locally, one-time, to confirm) shows all migrations applied — or equivalently, a route-detail request against prod succeeds
- A deliberate no-op re-run (pushing again with no pending migration) completes the Kudu step quickly without error, proving idempotency
- `az postgres flexible-server firewall-rule list` no longer shows `AllowOperatorMigration`

**Implementation Note**: The Kudu execution path is the one piece of this plan with genuine residual technical uncertainty (exact Linux-Kudu `/api/command` behavior hasn't been exercised in this repo before). If it doesn't work as designed, the documented fallback is the manual runbook used this session: temporarily allowlist the operator's IP on the Postgres firewall, run `dotnet ef database update --connection "<prod-connection-string>"` locally, then remove the rule. Do not silently fall back without surfacing it — pause here for manual confirmation that the Kudu path actually applied a real pending migration end-to-end before considering this phase done.

---

## Phase 2: Fail-Fast Config Checks

### Overview

Convert "required Clerk config missing" from a silent, indefinite 401/500 into an immediate, loud startup or build failure.

### Changes Required:

#### 1. Backend startup guard

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Refuse to start outside `Development` if any required Clerk setting is unset, so a misconfigured deploy fails its health probe immediately instead of 401ing every request indefinitely.

**Contract**: Insert after `builder.Services.AddAuthorization();` (currently line 103) and before `var app = builder.Build();`:

```csharp
if (!builder.Environment.IsDevelopment() && !EF.IsDesignTime)
{
    var required = new[] { "Clerk:Authority", "Clerk:AllowedAzp", "Clerk:SecretKey" };
    var missing = required.Where(k => string.IsNullOrEmpty(builder.Configuration[k])).ToList();
    if (missing.Count > 0)
        throw new InvalidOperationException(
            $"Missing required configuration: {string.Join(", ", missing)}");
}
```

`EF.IsDesignTime` requires `Microsoft.EntityFrameworkCore` (already imported at `Program.cs:6`).

#### 2. Frontend build-time guard

**File**: `src/frontend/scripts/check-required-env.mjs` (new), `src/frontend/package.json`

**Intent**: Fail `npm run build` if `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is empty — the exact PR #24 bug class.

**Contract**: New script checks `process.env.NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is non-empty, `process.exit(1)` with a clear message if not. Wire into `package.json`: `"build": "node scripts/check-required-env.mjs && next build"`.

### Success Criteria:

#### Automated Verification:

- Backend builds and unit-tests pass with `Clerk:*` present (normal dev flow): `dotnet build` and `dotnet test` from `src/backend/`
- Backend fails fast when `Clerk:*` is absent and environment isn't Development: a scratch run with `ASPNETCORE_ENVIRONMENT=Production dotnet run --project VeloRoute` (no user-secrets) exits with the `InvalidOperationException` message, not a silent 401 later
- `dotnet ef migrations has-pending-model-changes` (Phase 1's CI step) still passes — proves the `EF.IsDesignTime` guard works
- Frontend build fails when the env var is unset: `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY= npm run build` from `src/frontend/` exits non-zero
- Frontend build succeeds when set: `npm run build` with the real key present
- Frontend lint/tests unaffected: `npm run lint`, `npm test`

#### Manual Verification:

- Local dev flow (`dotnet run` with `dotnet user-secrets` configured, `npm run dev` with `.env.local`) is unaffected — sign-in still works end to end

---

## Phase 3: Post-Deploy Smoke Tests

### Overview

Prove the actually-deployed artifact works, not just that it built — would have caught all three of this session's bugs.

### Changes Required:

#### 1. Backend post-deploy smoke test

**File**: `.github/workflows/backend.yml`

**Intent**: After deploy, verify the live App Service both boots and correctly validates a real Clerk session — catching both the config-missing case (Phase 2 should already have prevented it, but this is the outer safety net) and any other live-only failure.

**Contract**: New step after the `azure/webapps-deploy@v3` step:
1. `curl -sf https://velo-route-api.azurewebsites.net/health` — fail the step on non-200.
2. Obtain a session JWT for a dedicated `smoke+clerk_test@<domain>` Clerk test user via the Clerk Frontend API's sign-in flow (`create sign-in` → `prepare_first_factor` with strategy `email_code` → `attempt_first_factor` with Clerk's fixed test code `424242`, valid because the instance is `pk_test_`) — verify exact endpoint paths against current Clerk API docs at implementation time; this is Clerk's documented `+clerk_test` testing convention, the same mechanism `@clerk/testing` uses under the hood for Playwright/Cypress.
3. `curl -sf -H "Authorization: Bearer $JWT" https://velo-route-api.azurewebsites.net/routes` — fail the step on non-200.

New secrets needed: a dedicated Clerk test user (email + Clerk instance's `Clerk:SecretKey`, already available server-side for creating/managing the test user via Backend API if it doesn't already exist).

#### 2. Frontend post-deploy smoke test

**File**: `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`

**Intent**: Prove the deployed SWA bundle actually has a non-empty Clerk key baked in — directly re-verifies PR #24 didn't regress.

**Contract**: New step after `build_and_deploy_job`'s deploy step: `curl -sf https://purple-sky-08f4fb710.7.azurestaticapps.net/` must return 200 (not 500), and the response body must contain a non-empty `publishableKey` field (grep/regex check, same pattern used manually this session to verify the fix).

### Success Criteria:

#### Automated Verification:

- Backend smoke test step passes against the current live deployment (manually triggered re-run once implemented)
- Frontend smoke test step passes against the current live deployment
- Existing Playwright e2e suite (`npx playwright test`) unaffected — these are separate, post-deploy-only checks

#### Manual Verification:

- Deliberately breaking one required backend env var in a scratch/staging test confirms the smoke test step fails the workflow (not just a warning)
- Deliberately shipping an empty frontend Clerk key (scratch test) confirms the frontend smoke step fails the workflow

---

## Phase 4: Doc Sync + Cleanup

### Overview

Per this repo's own "Keep docs accurate" workflow convention: no doc should describe a CI/deploy pipeline that no longer exists after this change lands.

### Changes Required:

#### 1. Backend README

**File**: `src/backend/VeloRoute/README.md`

**Intent**: Document the new migration-in-CI behavior (no new secret, Kudu-based, automatic on every backend deploy) and the fail-fast config check, next to the existing `Clerk:*` user-secrets documentation.

**Contract**: New subsection near the existing "Clerk auth" section describing: migrations apply automatically on deploy via Kudu (no manual `dotnet ef database update` needed anymore except as a documented fallback), and the app now refuses to start outside Development if `Clerk:*` config is incomplete.

#### 2. Root docs staleness sweep

**File**: `README.md`, `AGENTS.md`, `.github/copilot-instructions.md`, `src/frontend/README.md`

**Intent**: Per the repo's standing workflow rule, check each for now-stale claims about CI/deploy status, migration behavior, or "not yet implemented" scope statements touched by this change.

**Contract**: Read each file; update only sentences that describe migration/deploy behavior this change alters. No other content changes.

### Success Criteria:

#### Automated Verification:

- N/A (documentation-only phase)

#### Manual Verification:

- A fresh read of `src/backend/VeloRoute/README.md`'s deploy/migration section accurately describes the Phase 1 behavior
- `grep -rn "Database.Migrate\|manually apply\|dotnet ef database update"` across root docs shows no leftover claim that migrations are manual-only

---

## Testing Strategy

### Unit Tests:

- Phase 2's backend guard: a targeted test asserting the app throws when required Clerk config is missing and environment is not Development (and does NOT throw when `EF.IsDesignTime` is true, if testable in isolation — otherwise covered by the CI automated check instead)

### Integration Tests:

- None beyond existing `dotnet test` / `npm test` suites — this change is CI/infra behavior, not application logic, and is primarily verified by the automated/manual checks per phase above

### Manual Testing Steps:

1. Push a trivial backend change to a PR branch touching `src/backend/**`; confirm the model-drift check runs in CI
2. Merge to `main`; confirm the migration-via-Kudu step runs and the app deploys successfully
3. Confirm both post-deploy smoke tests pass on that same run
4. Temporarily unset a required Clerk app setting on a non-prod scratch App Service (if available) or via local scratch run to confirm the fail-fast guard actually fires

## Performance Considerations

None — this is CI/deploy-time tooling; it adds a few minutes to the `deploy` job (migration bundle build + Kudu round-trip) but does not affect runtime request latency.

## Migration Notes

This plan's own Phase 1 IS the migration-handling mechanism going forward — no separate migration is needed for this change itself (no data model changes).

## References

- Related session context: this session's live-debugging of the Clerk build-env, Clerk backend-config, and missing-`Shares`-table incidents (2026-09-09)
- Existing Clerk config docs: `src/backend/VeloRoute/README.md:35-44`
- `Database.Migrate()` gate: `src/backend/VeloRoute/Program.cs:107-112`
- `AddJwtBearer` config: `src/backend/VeloRoute/Program.cs:74-102`
- Deploy workflows: `.github/workflows/backend.yml`, `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Migration-in-CI via Kudu

#### Automated

- [x] 1.1 Model-drift check passes on current main — d001b28
- [x] 1.2 Backend test suite still green — d001b28
- [x] 1.3 Migration bundle builds — d001b28

#### Manual

- [x] 1.4 Deploy job's migration step completes successfully on a real push — bb9cd5e
- [x] 1.5 Migrations confirmed applied against prod after that deploy — bb9cd5e
- [x] 1.6 No-op re-run completes idempotently — bb9cd5e
- [x] 1.7 Stale firewall rule confirmed removed

### Phase 2: Fail-Fast Config Checks

#### Automated

- [x] 2.1 Backend builds/tests pass with Clerk config present
- [x] 2.2 Backend fails fast without Clerk config outside Development
- [x] 2.3 Model-drift check still passes (EF.IsDesignTime guard works)
- [x] 2.4 Frontend build fails when env var unset
- [x] 2.5 Frontend build succeeds when env var set
- [x] 2.6 Frontend lint/tests unaffected

#### Manual

- [x] 2.7 Local dev flow unaffected

### Phase 3: Post-Deploy Smoke Tests

#### Automated

- [ ] 3.1 Backend smoke test passes against live deployment
- [ ] 3.2 Frontend smoke test passes against live deployment
- [ ] 3.3 Existing Playwright e2e suite unaffected

#### Manual

- [ ] 3.4 Deliberate backend env-var break confirms smoke test fails the workflow
- [ ] 3.5 Deliberate frontend empty-key break confirms smoke test fails the workflow

### Phase 4: Doc Sync + Cleanup

#### Manual

- [ ] 4.1 Backend README accurately describes new migration/config behavior
- [ ] 4.2 No root doc still claims migrations are manual-only
