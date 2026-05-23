# VeloRoute — First Deploy Plan

## Problem & Approach

Deploy the current scaffold (Next.js 16 frontend + .NET 10 backend) to Azure to validate the CI/CD
pipeline end-to-end, before real app functionality is built. Platform: Azure Static Web Apps (Standard)
for the frontend + Azure App Service S1 (Linux) for the backend, as decided in `context/foundation/infrastructure.md`.

The plan has four phases:
1. **Code prep** — make both projects deployable (agent executes)
2. **Azure infrastructure** — create Azure resources (agent runs az CLI; two manual browser gates)
3. **GitHub Actions** — link repo, verify auto-generated workflow (agent + one manual gate)
4. **Smoke test** — confirm both services are alive and reachable (agent executes)

---

## Current state

| Area | Status |
|---|---|
| GitHub remote | ❌ Not configured — must be added before SWA creation |
| Next.js version | ⚠️ 16.2.6 — Azure SWA hybrid requires Node.js 18.x; Next.js 15 is the last confirmed-safe version |
| `next.config.ts` | ❌ Missing `output: 'standalone'` — required by SWA hybrid mode |
| `Program.cs` | ❌ `UseHttpsRedirection()` present — causes issues behind Azure proxy |
| CORS (backend) | ❌ Not configured — needed for browser-side API calls |
| Health endpoint | ❌ Absent — needed for smoke test and App Service health checks |
| `.github/workflows/` | ❌ Absent — auto-created by SWA during resource creation |
| Azure resources | ❌ None created yet |

> **Why downgrade from Next.js 16 → 15?** Azure SWA hybrid mode uses managed Azure Functions
> backed by Node.js 18.x — Node.js 20 is not yet supported (confirmed May 2026). Azure officially
> documents Next.js 13/14 support; 15/16 is "expected" but unverified. Next.js 16 may require
> Node.js 20+, which would break the SWA build. Next.js 15.x is the safe, documented-compatible
> choice. It supports React 19 (as of 15.3.x), so `react@19.2.4` stays unchanged.

---

## Key constants

| Name | Value |
|---|---|
| GitHub repo | `https://github.com/lukasz05/velo-route` |
| Azure region | `westeurope` |
| Resource group | `velo-route-rg` |
| App Service name | `velo-route-api` |
| SWA name | `velo-route-app` |
| App Service URL | `https://velo-route-api.azurewebsites.net` (post-deploy) |
| SWA URL | auto-assigned (e.g. `https://velo-route-app.azurestaticapps.net`) |

---

## Phase 1 — Code prep (agent)

### T0 — Downgrade Next.js 16 → 15 (latest stable 15.x)

Run from `src/frontend/`:

```bash
npm install next@^15 eslint-config-next@^15
```

This updates `next` and the matching ESLint config. React stays at 19.2.4 — Next.js 15.3.x supports
React 19 as a first-class option.

Verify no peer-dependency errors, then commit.

### T1 — `next.config.ts`: add `output: 'standalone'`

File: `src/frontend/next.config.ts`

Add `output: 'standalone'` to the Next.js config. SWA hybrid mode requires standalone output and
enforces a 250 MB app size limit.

```ts
const nextConfig: NextConfig = {
  output: 'standalone',
};
```

### T2 — `Program.cs`: production-ready backend

File: `src/backend/Program.cs`

Three changes:
1. Move `UseHttpsRedirection()` inside the `IsDevelopment()` block — Azure App Service terminates
   TLS at the proxy level; the app serves HTTP internally. Keeping the redirect causes issues.
2. Add CORS policy allowing the SWA origin (`*.azurestaticapps.net`) + `localhost:3000` for local dev.
3. Add a `GET /health` endpoint returning `{ "status": "ok" }` — used for smoke testing.

### T3 — Authenticate with GitHub ⚠️ MANUAL GATE

Two GitHub auth steps are needed before pushing and monitoring deployments:

**A) Git credential / HTTPS push auth**

The repo is private (assumed). HTTPS pushes require a Personal Access Token or GitHub CLI credential
helper. The recommended approach:

```bash
gh auth login
# → choose GitHub.com → HTTPS → browser/token
```

This configures Git's credential helper for `github.com` automatically.

If `gh` is not installed: `winget install GitHub.CLI`

**B) Verify access to the target repo**

```bash
gh repo view lukasz05/velo-route
```

If the repo doesn't exist yet, create it:

```bash
gh repo create lukasz05/velo-route --public --source=. --remote=origin --push
# (skip if repo already exists on GitHub)
```

### T4 — Add GitHub remote and push

```bash
git remote add origin https://github.com/lukasz05/velo-route.git
git push -u origin main
```

Also commit `context/foundation/infrastructure.md` and `context/deployment/deploy-plan.md`
(currently untracked) alongside the code changes.

---

## Phase 2 — Azure infrastructure (manual gates)

### T5 — Login to Azure ⚠️ MANUAL GATE

```bash
az login
```

Opens a browser. Log in with the account that has the Visual Studio Enterprise subscription.
Confirm the correct subscription is active:

```bash
az account show --query "{name:name, id:id}" -o table
az account set --subscription "<subscription-id-or-name>"
```

### T6 — Create resource group

```bash
az group create --name velo-route-rg --location westeurope
```

### T7 — Deploy .NET 10 backend to App Service S1

Run from `src/backend/`:

```bash
az webapp up \
  --sku S1 \
  --name velo-route-api \
  --resource-group velo-route-rg \
  --runtime "DOTNETCORE:10.0" \
  --os-type Linux \
  --location westeurope
```

`az webapp up` creates the App Service Plan + Web App + deploys the project in one command.
It detects the `.csproj` automatically when run from the project directory.

### T8 — Configure App Service settings

```bash
az webapp config appsettings set \
  -g velo-route-rg -n velo-route-api \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    ALLOWED_ORIGINS="https://velo-route-app.azurestaticapps.net http://localhost:3000"
```

> ⚠️ The exact SWA URL isn't known until T9. Update `ALLOWED_ORIGINS` again after T9 if the
> auto-assigned SWA URL differs from the expected name.

### T9 — Create SWA and link GitHub repo ⚠️ MANUAL GATE (browser OAuth)

```bash
az staticwebapp create \
  --name velo-route-app \
  --resource-group velo-route-rg \
  --sku Standard \
  --source https://github.com/lukasz05/velo-route \
  --branch main \
  --app-location "src/frontend" \
  --output-location "" \
  --login-with-github
```

This opens two browser windows:
1. Azure authentication (if not already logged in)
2. GitHub OAuth to authorize Azure Static Web Apps

After completion, Azure:
- Creates the SWA resource
- Commits a `.github/workflows/azure-static-web-apps-*.yml` file to the `main` branch
- Triggers the first GitHub Actions run automatically

### T10 — Configure SWA environment variable

```bash
az staticwebapp appsettings set \
  -n velo-route-app \
  --setting-names \
    VELO_API_URL=https://velo-route-api.azurewebsites.net
```

---

## Phase 3 — GitHub Actions verification

### T11 — Pull the auto-generated workflow and verify

After T9, Azure commits a workflow file (e.g., `.github/workflows/azure-static-web-apps-<hash>.yml`)
to the repo. Pull it locally and check:

```bash
git pull origin main
```

Verify the workflow contains:
- `app_location: "src/frontend"` ✅ (passed via `--app-location`)
- `output_location: ""`
- `api_location: ""`

If `app_location` is wrong (e.g., `/` instead of `src/frontend`), edit the file, commit, and push.

### T12 — Monitor the first deployment

```bash
gh run list --repo lukasz05/velo-route --limit 3
gh run watch <run-id> --repo lukasz05/velo-route
```

Also requires `gh auth login` to work (uses the credential helper configured in T3).

Expected: the workflow completes successfully and the SWA URL returns the Next.js default page.

---

## Phase 4 — Smoke test

### T13 — Verify backend health

```bash
curl https://velo-route-api.azurewebsites.net/health
# Expected: {"status":"ok"}

curl https://velo-route-api.azurewebsites.net/weatherforecast
# Expected: JSON array of 5 weather forecasts
```

### T14 — Verify frontend

```bash
curl -I https://velo-route-app.azurestaticapps.net/
# Expected: HTTP/2 200
```

Or open in the browser to confirm the Next.js page renders.

### T15 — Confirm SWA environment variable is visible (optional)

Add a temporary `console.log(process.env.VELO_API_URL)` check in `src/frontend/src/app/page.tsx`
(server component), deploy, check SWA build logs to confirm the env var is injected.
Remove after confirming. (Optional — can skip for scaffold validation.)

---

## Manual gates summary

| Gate | When | Action |
|---|---|---|
| `gh auth login` | T3 | Browser — authenticate GitHub CLI (enables git push + gh commands) |
| `az login` | T5 | Browser — log in to Azure with VS Enterprise account |
| `--login-with-github` | T9 | Browser — OAuth GitHub authorization for Azure SWA |

---

## Out of scope for this deploy

- Custom domain / SSL configuration
- App Service deployment slots (staging → production swap) — infrastructure exists (S1), configure when needed
- Application Insights / monitoring setup
- Azure Key Vault for secrets
- Actual VeloRoute app functionality (routes, map, GPX export)

---

## Risks to watch during this deploy

1. **Next.js 15 + React 19 peer dep warnings** — `npm install next@^15` may show peer dependency
   warnings for React 19 on some minor versions. If they appear, pin to `next@15.3.x` which has
   official React 19 support and resolve with `--legacy-peer-deps` only as last resort.

2. **Azure Policy blocking resource creation** — if `az webapp up` or `az staticwebapp create` fails
   with a policy error, run `az policy assignment list --scope /subscriptions/<id>` to identify
   the blocking policy. May require an exemption request or using a personal subscription.

3. **App Service URL collision** — `velo-route-api` must be globally unique in `azurewebsites.net`.
   If it's taken, use `velo-route-api-<suffix>` and update the CORS origin accordingly.

4. **`output_location` in the auto-generated workflow** — Azure SWA may set `output_location: "build"`
   or another default. For hybrid Next.js (standalone output), this must be `""`. Edit the workflow
   file if Azure's default is wrong.
