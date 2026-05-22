---
starter_id: next
package_manager: npm
project_name: velo-route
hints:
  language_family: multi
  team_size: solo
  deployment_target: azure-static-web-apps
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

VeloRoute is a two-project, multi-language architecture: a **Next.js frontend** (`next`) for the interactive map UI and a **.NET ASP.NET Core webapi backend** (`dotnet`) for route computation and GPX generation. Next.js passes all four agent-friendly gates (TypeScript end-to-end, file-based routing conventions, dominant in JS training data, versioned Vercel docs) and is the `starter_id` bootstrapper scaffolds first. The .NET API is the natural C# home for the compute-heavy graph-traversal logic; the `dotnet new webapi` template scaffolds separately. Both projects deploy to Azure: Next.js to **Azure Static Web Apps** (SSR via Azure Functions, free tier, GitHub Actions integration), .NET to **Azure App Service**. Bootstrapper confidence is `first-class` — the `next` card's deployment defaults don't include Azure, so the GitHub Actions workflow for Azure Static Web Apps requires a brief manual wiring step post-scaffold. VeloRoute v1 has no auth, payments, realtime, AI, or background-job requirements.
