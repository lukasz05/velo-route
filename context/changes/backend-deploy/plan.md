# Backend Deployment and CI Gate — Implementation Plan

## Overview

Wire GitHub Actions CI/CD for the .NET backend: a `dotnet test` gate on every PR and
push to `main` that touches `src/backend/`, and an automated deploy to the already-provisioned
Azure App Service (`velo-route-api`) on merge to `main`. Authentication uses OIDC Workload
Identity Federation — no stored passwords.

## Current State Analysis

The Azure infrastructure is already in place from a prior manual deploy:
- App Service `velo-route-api` (Linux, S1) — deployed via `az webapp up`
- SWA `velo-route-app` — live at `purple-sky-08f4fb710.azurestaticapps.net`; workflow
  `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml` exists and deploys
  on push to `main`

Code prep is complete:
- `output: 'standalone'` in `next.config.ts`
- `UseHttpsRedirection()` inside `IsDevelopment()` block only
- CORS reads `ALLOWED_ORIGINS` env var (`Program.cs:9–18`)
- `GET /health` endpoint (`Program.cs:59–60`)

Missing:
- GitHub Actions workflow for backend CI + deploy
- OIDC identity wired between Azure Entra and GitHub Actions
- `ALLOWED_ORIGINS` App Service setting confirmed/updated with SWA URL

## Desired End State

After this plan:
- Every PR touching `src/backend/**` shows a required `test` status check that must pass before merge
- Every merge to `main` touching `src/backend/**` automatically deploys to `velo-route-api.azurewebsites.net`
- `curl https://velo-route-api.azurewebsites.net/health` returns `{"status":"ok"}`
- Full app is usable end-to-end: SWA frontend → App Service backend → ORS

### Key Discoveries

- Solution file is `src/backend/VeloRoute.slnx` (`.slnx`, not `.sln`) — use project path in CI to be explicit
- Live ORS smoke tests carry `[Fact(Skip = "Live ORS...")]` — `dotnet test` in CI will show 56 pass, 3 skip; no ORS key needed in CI
- `az webapp deploy` (zip deploy) replaces code only; App Service settings (`ALLOWED_ORIGINS`, `ORS__ApiKey`, `ASPNETCORE_ENVIRONMENT`) persist across deploys
- SWA URL from existing workflow name: `purple-sky-08f4fb710.azurestaticapps.net` — must be in `ALLOWED_ORIGINS`

## What We're NOT Doing

- Setting up deployment slots (staging → production swap) — S1 supports it but deferred
- Application Insights / distributed tracing — deferred post-launch
- Azure Key Vault for ORS API key — App Service app settings are sufficient for MVP
- Docker containerisation — `dotnet publish` zip deploy is simpler and adequate
- Frontend CI changes — SWA workflow already handles the frontend
- OIDC Workload Identity Federation — deferred to v2; publish profile is sufficient for MVP

## Implementation Approach

Phase 1 is manual Azure Portal + GitHub setup (publish profile secret). Phase 2 is the
agent writing the workflow file. Phase 3 is a one-command CORS verification. Phase 4 is
the human smoke-testing the live pipeline end-to-end.

Auth approach: publish profile (MVP). OIDC Workload Identity Federation deferred to v2.

## Phase 1: Publish Profile Setup

### Overview

Download the App Service publish profile and store it as a GitHub Actions secret.
No Entra app registration, no role assignment, no federation — one credential file.

### Changes Required

#### 1. Download publish profile

**File**: Azure Portal (manual)

**Intent**: Obtain the credential file the deploy action uses to authenticate.

**Contract**: Azure Portal → App Service `velo-route-api` → Overview →
**Download publish profile** button. Save the downloaded XML file locally.

#### 2. Store as GitHub Actions secret

**File**: GitHub → repo Settings → Secrets and variables → Actions → **Secrets** tab

**Intent**: Make the publish profile available to the deploy job.

**Contract**: Create one repository **secret** (sensitive — use Secrets, not Variables):
- Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
- Value: paste the full XML content of the downloaded file

### Success Criteria

#### Manual Verification

- [ ] `AZURE_WEBAPP_PUBLISH_PROFILE` secret exists in GitHub Actions secrets

**Implementation Note**: All of Phase 1 is manual. No code changes. Proceed to Phase 2 once the secret is set.

---

## Phase 2: GitHub Actions Workflow

### Overview

Write the single backend workflow file. The `test` job runs on every qualifying trigger;
the `deploy` job runs on push to `main` only, after `test` passes.

### Changes Required

#### 1. Backend workflow

**File**: `.github/workflows/backend.yml`

**Intent**: CI gate (test) + automated deploy, scoped to backend changes only.

**Contract**: Trigger on `push` to `main` and `pull_request` targeting `main`, with
`paths` filter `['src/backend/**', '.github/workflows/backend.yml']`.

Permissions block at workflow level:
```yaml
permissions:
  contents: read
```

`test` job (`ubuntu-latest`):
- `actions/checkout@v4`
- `actions/setup-dotnet@v4` with `dotnet-version: '10.x'`
- `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj --configuration Release --verbosity minimal`

`deploy` job (`ubuntu-latest`):
- `needs: test`
- `if: github.ref == 'refs/heads/main' && github.event_name == 'push'`
- `actions/checkout@v4`
- `actions/setup-dotnet@v4` with `dotnet-version: '10.x'`
- `dotnet publish src/backend/VeloRoute/VeloRoute.csproj -c Release -o ./publish`
- `azure/webapps-deploy@v3` with `app-name: velo-route-api`, `publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}`, `package: ./publish`

### Success Criteria

#### Automated Verification

- [ ] `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj -c Release` passes locally (56 pass, 3 skip)
- [ ] Workflow file passes YAML lint (`yamllint .github/workflows/backend.yml` or GitHub's editor validation)

#### Manual Verification

- [ ] Workflow file committed and pushed to `main`
- [ ] GitHub Actions tab shows `Backend CI/CD` workflow listed

**Implementation Note**: Do not merge via PR for this phase — push directly to `main`. A PR would trigger the workflow before the OIDC variables are confirmed, and the deploy job would fail. The test job will still run and provide early signal.

---

## Phase 3: CORS Verification

### Overview

Confirm the App Service `ALLOWED_ORIGINS` setting includes the SWA URL so browser-originated
requests from the frontend reach the backend without CORS errors.

### Changes Required

#### 1. Verify and update ALLOWED_ORIGINS

**File**: Azure App Service app settings (via `az` CLI)

**Intent**: The CORS policy in `Program.cs` reads `ALLOWED_ORIGINS` from config; if this
setting is absent or wrong, API calls from the SWA frontend will be blocked.

**Contract**: Run:
```bash
az webapp config appsettings list -g velo-route-rg -n velo-route-api --query "[?name=='ALLOWED_ORIGINS']"
```

Expected value: `https://purple-sky-08f4fb710.azurestaticapps.net http://localhost:3000`

If missing or incorrect:
```bash
az webapp config appsettings set \
  -g velo-route-rg -n velo-route-api \
  --settings ALLOWED_ORIGINS="https://purple-sky-08f4fb710.azurestaticapps.net http://localhost:3000"
```

Also verify `ASPNETCORE_ENVIRONMENT=Production` is set (controls whether Swagger/OpenAPI
endpoints are exposed).

### Success Criteria

#### Automated Verification

- [ ] `az webapp config appsettings list` shows correct `ALLOWED_ORIGINS` and `ASPNETCORE_ENVIRONMENT`
- [ ] `curl https://velo-route-api.azurewebsites.net/health` returns `{"status":"ok"}`

#### Manual Verification

- [ ] No CORS errors in browser DevTools when the SWA frontend calls the backend

---

## Phase 4: End-to-End Smoke Test

### Overview

Validate the full pipeline: test gate blocks merge on failure, deploy runs on merge,
live app works end-to-end.

### Changes Required

No code changes — this phase is observation only.

### Success Criteria

#### Automated Verification

- [ ] First real CI run (triggered by workflow push in Phase 2): `test` job shows 56 pass, 3 skip in Actions log
- [ ] Deploy job log shows `az webapp deploy` success and App Service URL

#### Manual Verification

- [ ] Open a test PR with a trivial `src/backend/` change (e.g., add/remove a blank line in a `.cs` file) — `test` status check appears and passes
- [ ] Merge the test PR — deploy job runs in Actions and completes green
- [ ] `curl https://velo-route-api.azurewebsites.net/health` → `{"status":"ok"}`
- [ ] Open SWA URL in browser, generate a route — no CORS errors, route displays on map
- [ ] GPX export works from the live app

---

## Testing Strategy

### Automated (CI)

- `dotnet test` runs the full suite: 56 unit + integration tests, 3 live ORS smoke tests skipped
- No ORS API key needed in CI — live tests carry `[Fact(Skip = ...)]`

### Manual

- Test PR verifies the gate blocks merges without a green test run
- Post-merge smoke confirms the deployed binary is the one just built

## References

- Deploy plan (prior work): `context/deployment/deploy-plan.md`
- Infrastructure research: `context/foundation/infrastructure.md`
- Program.cs CORS + health: `src/backend/VeloRoute/Program.cs:9–60`
- Existing SWA workflow: `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`
- GitHub Actions OIDC docs: https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-azure

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands.

### Phase 1: Publish Profile Setup

#### Manual

- [x] 1.1 Publish profile downloaded from App Service `velo-route-api` — c06a3c4
- [x] 1.2 `AZURE_WEBAPP_PUBLISH_PROFILE` secret set in GitHub Actions secrets — c06a3c4

### Phase 2: GitHub Actions Workflow

#### Automated

- [x] 2.1 `dotnet test` passes locally (56 pass, 3 skip) — 6cf9e29
- [x] 2.2 Workflow YAML is valid — 6cf9e29

#### Manual

- [x] 2.3 Workflow file pushed to `main` and visible in GitHub Actions tab

### Phase 3: CORS Verification

#### Automated

- [x] 3.1 `ALLOWED_ORIGINS` App Service setting contains SWA URL
- [x] 3.2 `/health` returns `{"status":"ok"}`

#### Manual

- [x] 3.3 No CORS errors in browser DevTools on SWA → backend calls

### Phase 4: End-to-End Smoke Test

#### Automated

- [ ] 4.1 First CI run shows 56 pass, 3 skip in Actions log
- [ ] 4.2 Deploy job completes green

#### Manual

- [ ] 4.3 Test PR triggers required `test` status check
- [ ] 4.4 Post-merge deploy completes; `/health` returns 200
- [ ] 4.5 Live app: route generation works, no CORS errors
- [ ] 4.6 GPX export works from the live app
