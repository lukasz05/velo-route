---
starter_id: dotnet
package_manager: dotnet
project_name: velo-route-api
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: first-class
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: false
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
---

## Why this stack

.NET ASP.NET Core webapi for VeloRoute's compute-heavy route computation and GPX generation backend. Deploys to Azure App Service alongside the Next.js frontend on Azure Static Web Apps.
