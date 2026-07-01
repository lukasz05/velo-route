---
id: backend-deploy
title: Backend deployment and CI gate
status: implementing
created: 2026-06-30
updated: 2026-06-30
roadmap_ref: F-05
---

Deploy the .NET backend to Azure App Service (already provisioned manually) via GitHub
Actions CI/CD; add a `dotnet test` gate that runs on every PR and push to `main` that
touches `src/backend/`. Uses OIDC Workload Identity Federation for zero-secret deploy auth.
