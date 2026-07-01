# Backend Deployment and CI Gate — Plan Brief

> Full plan: `context/changes/backend-deploy/plan.md`

## What & Why

Wire GitHub Actions CI/CD for the .NET backend: a required `dotnet test` gate on every
PR and push to `main` touching `src/backend/`, and automated deploy to the already-live
Azure App Service on merge. This closes the last gap before v1 is shippable — the backend
runs in production but deploys are manual and there is no CI gate.

## Starting Point

Azure App Service `velo-route-api` (Linux S1) and SWA `velo-route-app` are both live from
a prior manual deploy. The SWA already has a GitHub Actions workflow. The backend has no
workflow. Code prep (CORS, health endpoint, standalone output, HTTPS redirect) is complete.

## Desired End State

Every PR touching `src/backend/` shows a required passing `test` status check. Every merge
to `main` automatically deploys to `velo-route-api.azurewebsites.net` via OIDC (no stored
credentials). The full app — SWA frontend → App Service backend → ORS — works end-to-end
in production.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| Deploy auth | OIDC Workload Identity Federation | No stored secrets; token-per-run; Microsoft-recommended production approach | Plan |
| Workflow shape | Single file, two jobs (`test` → `deploy`) | One file to maintain; deploy is automatically gated on tests | Plan |
| Test gate scope | PRs + push to main | Catches regressions both before and after merge | Plan |
| Path filter | `src/backend/**` + `.github/workflows/backend.yml` | Avoids spurious runs on frontend-only changes; self-tests the CI config | Plan |
| Federation type | Branch-based (`ref:refs/heads/main`) | Simpler than environment-based; no GitHub environment to create | Plan |

## Scope

**In scope:**
- Azure Entra app registration + federated credential for OIDC
- `.github/workflows/backend.yml` (test job + deploy job)
- `ALLOWED_ORIGINS` App Service setting verification/update
- End-to-end smoke test (live pipeline + browser)

**Out of scope:**
- Deployment slots (staging → production swap)
- Application Insights / monitoring
- Azure Key Vault for ORS API key
- Docker containerisation
- Frontend CI changes

## Architecture / Approach

GitHub Actions requests an OIDC token → Azure Entra validates the subject claim
(`repo:lukasz05/velo-route:ref:refs/heads/main`) against the federated credential →
issues a short-lived access token → `azure/login@v2` authenticates → `azure/webapps-deploy@v3`
zip-deploys the `dotnet publish` output. The `test` job runs on all qualifying triggers;
the `deploy` job runs only on push to `main` and only after `test` passes.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. OIDC Identity Setup | App registration + federated credential + role + GitHub vars | Subject claim mismatch = silent 401 in CI |
| 2. GitHub Actions Workflow | `backend.yml` with test gate + deploy | Pushed directly to main (not via PR) to avoid chicken-and-egg |
| 3. CORS Verification | `ALLOWED_ORIGINS` confirmed; `/health` reachable | Wrong SWA URL → browser CORS errors on live app |
| 4. End-to-End Smoke | Full pipeline validated; live app usable | None — observation only |

**Prerequisites:** Azure CLI authenticated (`az login`), `gh` CLI authenticated, Phase 1 complete before Phase 2 commit.
**Estimated effort:** ~1 session; ~30 min manual Azure + GitHub setup (Phase 1), ~15 min workflow authoring (Phase 2), ~5 min verification (Phases 3–4).

## Open Risks & Assumptions

- SWA URL assumed to be `purple-sky-08f4fb710.azurestaticapps.net` from workflow filename — verify before setting `ALLOWED_ORIGINS`
- `ASPNETCORE_ENVIRONMENT=Production` assumed already set from manual deploy — Phase 3 verifies

## Success Criteria (Summary)

- `dotnet test` is a required status check visible on every PR touching `src/backend/`
- `https://velo-route-api.azurewebsites.net/health` returns `{"status":"ok"}` after a CI-triggered deploy
- Route generation works end-to-end on the live SWA URL with no CORS errors
