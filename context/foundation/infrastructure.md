---
project: VeloRoute
researched_at: 2026-05-23
recommended_platform: Azure (Static Web Apps + App Service)
runner_up: Railway
context_type: mvp
tech_stack:
  language: TypeScript (frontend) / C# (backend)
  framework: Next.js 16 + React 19 (frontend) / ASP.NET Core Minimal API (backend)
  runtime: Node.js (frontend) / .NET 10 LTS (backend)
---

## Recommendation

**Deploy on Azure — Static Web Apps (Standard) for the Next.js 16 frontend and App Service (Standard S1, Linux) for the ASP.NET Core .NET 10 backend.**

Both services are covered by the developer's Visual Studio Enterprise subscription ($150/month in Azure credits), making the effective monthly cost $0. Azure App Service has first-party, GA support for .NET 10 LTS. Azure Static Web Apps provides a built-in global CDN for static assets and automatic PR preview environments. The Azure MCP Server 1.0 (GA) integrates with VS Code, GitHub Copilot, and Cursor for CLI-adjacent agent operations.

The main risks to carry into the project: SWA hybrid Next.js SSR support is marked **in preview** — `swa deploy` does not work for hybrid apps, deployments must go through GitHub Actions, the linked-API-proxy feature is unsupported, and Next.js 16 compatibility is unverified in the preview docs. These constraints are documented, manageable for a solo MVP, and partially mitigated by the VS Enterprise budget enabling the Standard tier (always-ready instances, deployment slots).

---

## Platform Comparison

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP/Integration | Notes |
|---|---|---|---|---|---|---|
| **Azure SWA + App Service** | 🟡 Partial | ✅ Pass | 🟡 Partial | 🟡 Partial | ✅ Pass | **Recommended** — free with VS Enterprise credits |
| **Railway** | ✅ Pass | ✅ Pass | ✅ Pass | 🟡 Partial | ✅ Pass | Runner-up — best full-stack agent-friendly score |
| **Render** | 🟡 Partial | ✅ Pass | ✅ Pass | ✅ Pass | ✅ Pass | Third — strong GA MCP, both services deployable |
| Vercel | ✅ Pass | ✅ Pass | ✅ Pass | ✅ Pass | ✅ Pass | .NET not supported — split architecture required |
| Cloudflare W+P | ✅ Pass | ✅ Pass | ✅ Pass | ✅ Pass | ✅ Pass | .NET not supported — split architecture required |
| Fly.io | ✅ Pass | ✅ Pass | ❌ Fail | 🟡 Partial | ❌ Fail | No llms.txt, no MCP server, no free tier |

**Hard filters applied:** None — route generation is a synchronous HTTP request (~5s); no persistent WebSocket or long-polling requirement confirmed from the interview.

**Soft weights applied:** Free Azure credits (↑ Azure), Azure familiarity (Azure tiebreaker), global reach (Azure CDN covers static assets globally), co-location preference (Azure has the deepest managed services ecosystem of any candidate).

> **Note on Vercel and Cloudflare:** Both score 5/5 on raw criteria but cannot run .NET 10. A hybrid approach — Cloudflare Workers (Next.js 16, free tier up to 100k req/day) + Azure App Service (free with VS Enterprise credits) — is a $0 alternative that maximises edge performance if SWA preview instability becomes blocking.

### Shortlisted Platforms

#### 1. Azure (SWA + App Service) — Recommended

Effective cost $0 with VS Enterprise ($150/month covers App Service S1 ~$57/month + SWA Standard ~$9/month + PostgreSQL Flexible ~$12/month). .NET 10 is fully GA on App Service with dedicated, persistent processes and no request execution timeout. SWA delivers a built-in global CDN for static assets. App Service S1 supports deployment slots for zero-downtime rollback (`az webapp deployment slot swap`). Azure MCP Server 1.0 (GA, `microsoft/mcp`) covers 40+ services and wires into VS Code, Copilot, and Cursor. The key constraint: SWA hybrid Next.js SSR is in preview — `swa deploy` is unsupported, GitHub Actions is the required deployment path, and the linked-API proxy is unsupported.

#### 2. Railway

Railway scored the highest among full-stack single-vendor platforms on agent-friendly criteria (4 Pass, 1 Partial). Both services deploy on one platform: Next.js 16 auto-detected by Railpack; .NET 10 deploys via a one-time Dockerfile. Private WireGuard networking connects both services with zero configuration. First-party PostgreSQL, Redis, and S3 buckets provision in one command. The MCP server (local + remote at `mcp.railway.com`) and agent skills are GA. `railway up` deploys from the terminal in one command; `railway logs` streams live. Cost: ~$15–20/month Hobby — meaningful out-of-pocket vs. $0 on Azure, which is why Azure edges it out for this user. If SWA preview instability becomes blocking, Railway is the immediate fallback requiring minimal re-architecture.

#### 3. Render

Render has the strongest MCP story of any platform in the candidate pool: official `mcp.render.com` GA server, CLI-installable agent skills, and Jules (Google Labs) integration for auto-fixing PR build failures. Both services deploy via GA paths: Next.js 16 via Node runtime, .NET 10 via Docker. llms.txt and llms-full.txt are confirmed GA. Paid services never spin down (persistent processes, no cold starts). Co-located Postgres (v13–18) and Valkey (Redis-compatible) are included on the same private network. Primary gaps: CLI is secondary to deploy hooks as the CI pattern; horizontal autoscaling requires Pro plan; services are single-region per project. Cost: ~$21/month.

---

## Anti-Bias Cross-Check: Azure (SWA + App Service)

### Devil's Advocate — Weaknesses

1. **`swa deploy` is unsupported for hybrid Next.js**: The SWA CLI explicitly cannot deploy hybrid (SSR) apps. All frontend deployments must go through GitHub Actions (`Azure/static-web-apps-deploy@v1`). An agent operating from the terminal cannot trigger a frontend deploy — it must commit, push, and wait for Actions to complete (~3–4 minutes). This breaks the CLI-first agent ops loop for half the stack.

2. **No `az staticwebapp rollback` command exists**: Reverting the frontend requires re-triggering a previous GitHub Actions run, which programmatically needs a GitHub PAT and call to the GitHub Actions API — not a one-command terminal operation. Contrast with `railway redeploy` or `fly deploy --image <sha>`.

3. **Linked API routing unsupported in SWA hybrid preview**: SWA's built-in `/api` proxy path to an external App Service is **explicitly unsupported** in hybrid mode. The Next.js frontend and .NET API are on separate HTTPS origins. The API must either be called directly via CORS from the browser, or via direct URL from Next.js Server Components — adding configuration that Railway/Render handle automatically via private networking.

4. **SWA hybrid Next.js 16 compatibility is unverified**: SWA's preview documentation and examples reference Next.js 14/v15. This project uses Next.js 16 (with App Router breaking changes). Bugs specific to Next.js 16 on SWA may have no existing workaround in the preview issue tracker.

5. **SWA hybrid can lag Next.js patch releases**: Next.js minor/patch upgrades can break the SWA preview integration until Microsoft ships a compatible update. During a 3-week MVP sprint, getting pinned to an older Next.js version is a real risk.

### Pre-Mortem — How This Could Fail

The team deployed VeloRoute on Azure SWA + App Service S1 using VS Enterprise credits. The backend landed cleanly in the first hour: `az webapp up` had .NET running in minutes. The frontend was slower: the agent committed, pushed, waited 3–4 minutes per deploy. Tolerable at first.

Week three: a Next.js 16 minor update introduced an App Router change that the SWA preview adapter hadn't caught up to. Build succeeded in Actions; the live site returned 500 errors on SSR routes. Rolling back required reverting the commit, pushing, and waiting for Actions again — 45 minutes of iteration, none of it from a terminal command. The Azure Functions cold-start issue surfaced the same week: the first user request after a quiet evening added 5–7 seconds of latency on top of route generation, exceeding the PRD's 5-second target. Configuring always-ready instances required discovering the right combination of SWA Standard settings — documentation for this interaction specifically in hybrid preview mode was sparse and community threads were months old.

By month two, the developer discovered that calling the .NET API from Next.js Server Components required hardcoding the App Service URL as an environment variable and adding a CORS policy on the .NET side. When the App Service URL changed after a region migration, the Next.js app silently returned empty route results until the env var was updated. None of this is insurmountable, but each friction point arrived unannounced.

*(~180 words)*

### Unknown Unknowns

- **SWA uses Azure Functions internally for SSR**: The Next.js SSR layer on SWA runs on Azure Functions under the hood. On the Free SWA plan, these run on a consumption plan with cold-start latency of 2–8 seconds after idle — directly threatening the PRD's 5-second load target. Mitigated on Standard plan with always-ready instances, but requires explicit configuration that is not on by default.

- **Enterprise Azure Policy may block resource creation**: Work Azure subscriptions frequently enforce Policy rules that block specific resource types, regions, or require mandatory tags. First deploys may fail with a cryptic policy error requiring an IT ticket — entirely invisible until you try. Run `az policy assignment list --scope /subscriptions/<id>` before the first deploy.

- **No global CDN for App Service by default**: SWA's CDN covers static assets globally. API requests to the App Service (single region) bypass any CDN — users in Asia or South America see the full round-trip latency on every API call (~300–600ms). Azure Front Door resolves this (~$35/month, within VS Enterprise budget) but requires explicit configuration.

- **`WEBSITE_PROACTIVE_AUTOHEAL_ENABLED` is on by default on App Service**: The worker process auto-recycles if 80% of requests exceed 200 seconds in a 2-minute window. Unlikely to trigger for VeloRoute's normal load, but if the upstream routing API (OpenRouteService) hangs, cascading slow responses could cause unexpected process recycling and 502s.

- **SWA hybrid Next.js 16 compatibility is unverified**: The SWA preview docs are written against Next.js 14/15 examples. Next.js 16 introduces breaking changes from training data. Bugs specific to Next.js 16 App Router behaviour on SWA may be unresolved in the preview issue tracker with no official timeline.

---

## Operational Story

- **Preview deploys**: SWA automatically creates PR preview environments at unique URLs (e.g., `lively-rock-123456-preview.westeurope.3.azurestaticapps.net`) when configured in GitHub Actions via `Azure/static-web-apps-deploy@v1`. Requires SWA Standard plan. Preview URLs are accessible without additional auth by default — add SWA password protection or Azure Static Web Apps authentication rules for sensitive previews.

- **Secrets**: App Service env vars: `az webapp config appsettings set -g velo-route-rg -n velo-route-api --settings KEY=value`. SWA env vars: `az staticwebapp appsettings set -n velo-route-app --setting-names KEY=value`. GitHub Actions deployment token: retrieved via `az staticwebapp secrets list -n velo-route-app` → stored as GitHub Secret `AZURE_STATIC_WEB_APPS_API_TOKEN`. Sensitive values (routing API keys, DB connection strings) go into Azure Key Vault and are referenced from App Service via Key Vault references (`@Microsoft.KeyVault(SecretUri=...)`). Secrets never in source code, never in `az` command history (use `--settings @file.json` for sensitive values).

- **Rollback**: App Service S1 (deployment slots): `az webapp deployment slot swap -g velo-route-rg -n velo-route-api --slot staging` — instant, zero-downtime, traffic cuts over in seconds. SWA frontend: `gh run rerun <run-id> --repo <owner>/10xdevs` to re-run a previous GitHub Actions deployment, or `git revert <sha> && git push` to trigger a new deploy from the previous state. Typical time-to-revert: App Service ~30 seconds; SWA ~4 minutes. Database migrations do not roll back automatically — write inverse migrations before running forward ones.

- **Approval**: Agent may perform unattended: `az webapp up`, `az webapp config appsettings set`, `az webapp restart`, `az webapp deployment slot create`, trigger GitHub Actions via `gh workflow run`. Human required for: `az group delete`, `az webapp delete`, rotating primary API token (`az staticwebapp secrets reset-api-key`), dropping a PostgreSQL database, slot-swapping production after a schema-changing deployment.

- **Logs**: App Service runtime logs: `az webapp log tail -g velo-route-rg -n velo-route-api`. SWA build logs: `gh run view <run-id> --log --repo <owner>/10xdevs`. Application Insights (if enabled): `az monitor app-insights query -g velo-route-rg --app velo-route-insights --analytics-query "requests | order by timestamp desc | take 50"`. Azure MCP Server read-only log tools cover most diagnostic queries without CLI.

---

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| `swa deploy` unsupported for hybrid Next.js — agent cannot deploy frontend from CLI | Devil's advocate | **High** (confirmed in docs) | Medium | Accept: all frontend deploys via GitHub Actions (`Azure/static-web-apps-deploy@v1`). Document in AGENTS.md: agent commits + pushes; does not call a deploy command for the frontend. |
| No `az staticwebapp rollback` CLI command | Devil's advocate | **High** (confirmed) | Medium | Use `gh run rerun <id>` to re-trigger a previous Actions run. Store last-known-good commit SHA in deployment notes after each release. |
| Linked API proxy unsupported in SWA hybrid preview | Devil's advocate | **High** (confirmed) | Low | Call .NET API directly from Next.js Server Components via `fetch(process.env.VELO_API_URL)`. Add explicit CORS policy on the .NET minimal API for browser-originated requests. |
| SWA hybrid does not validate against Next.js 16 | Devil's advocate + Unknown unknowns | **Medium** | High | Pin `next` to the last minor version tested on SWA before the MVP sprint. Monitor `github.com/Azure/static-web-apps` issues before each upgrade. Fallback: Railway supports Next.js 16 via Railpack with no preview caveats. |
| SWA hybrid lags Next.js patch releases | Devil's advocate | **Medium** | Medium | Keep `next` pinned during the 3-week MVP sprint. Upgrade only after confirming SWA compatibility on a staging environment. |
| Azure Functions cold starts on SWA SSR layer (~2–8s after idle) | Unknown unknowns | **High** (inherent in SWA architecture on Free plan) | High | Use SWA Standard plan (within VS Enterprise budget) and configure always-ready instances. Verify time-to-first-byte meets the 5s PRD target in staging before launch. |
| Enterprise Azure Policy blocking resource creation | Unknown unknowns | **Medium** | High | Run `az policy assignment list --scope /subscriptions/<id>` before first deploy. If blocked, request a policy exemption or create resources via the Azure Portal GUI where policies may be more permissive. |
| No global CDN for App Service — high latency for non-EU API calls | Unknown unknowns | **Medium** | Medium | Accept for MVP (VeloRoute v1 has no explicit latency SLA on the API; the 5s target is page load). Add Azure Front Door (~$35/month, within VS Enterprise credits) post-launch if global latency becomes a complaint. |
| App Service autoheal recycles .NET worker on slow upstream responses | Unknown unknowns | **Low** | Medium | Set a <10s timeout on all outbound OpenRouteService API calls. Add a circuit breaker to fail fast rather than hang. |
| SWA Database Connections feature retiring Nov 30, 2025 | Research finding | **Certain** | Low | Do not use SWA's built-in database connections feature. Connect to Azure Database for PostgreSQL Flexible directly from the App Service backend only. |

---

## Getting Started

1. **Install tooling**:
   ```powershell
   winget install Microsoft.AzureCLI
   npm install -g @azure/static-web-apps-cli   # local dev emulation only
   npx -y @azure/mcp@latest server start        # Azure MCP Server (VS Code / Copilot)
   ```

2. **Login and create a resource group**:
   ```bash
   az login
   az group create --name velo-route-rg --location westeurope
   ```

3. **Deploy the .NET 10 backend to App Service S1** (run from `src/backend/`):
   ```bash
   az webapp up \
     --sku S1 \
     --name velo-route-api \
     --runtime "DOTNETCORE:10.0" \
     --resource-group velo-route-rg \
     --os-type Linux
   az webapp config appsettings set \
     -g velo-route-rg -n velo-route-api \
     --settings ASPNETCORE_ENVIRONMENT=Production
   ```

4. **Create the SWA resource and link the GitHub repo** — the Next.js frontend deploys via GitHub Actions; `swa deploy` is not supported for hybrid apps:
   ```bash
   az staticwebapp create \
     --name velo-route-app \
     --resource-group velo-route-rg \
     --sku Standard \
     --source https://github.com/<owner>/10xdevs \
     --branch main \
     --app-location "src/frontend" \
     --output-location ".next/standalone" \
     --login-with-github
   ```

5. **Wire the backend URL into the SWA environment** so Next.js Server Components can reach the API:
   ```bash
   az staticwebapp appsettings set \
     -n velo-route-app \
     --setting-names VELO_API_URL=https://velo-route-api.azurewebsites.net
   ```

> ⚠️ Before deploying, add `output: 'standalone'` to `src/frontend/next.config.ts`. SWA hybrid mode requires the standalone output and enforces a 250 MB app size limit.

> ⚠️ Verify Next.js 16 compatibility with SWA hybrid preview on a staging branch before deploying to `main`. If incompatible, consider the Railway fallback (both services deploy without preview caveats).

---

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup beyond the GitHub Actions deployment action
- Production-scale architecture (multi-region, HA, DR)
