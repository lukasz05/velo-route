---
bootstrapped_at: 2026-05-22T18:05:00Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: velo-route-api
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: first-class
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

```yaml
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
```

### Why this stack

.NET ASP.NET Core webapi for VeloRoute's compute-heavy route computation and GPX generation backend. Deploys to Azure App Service alongside the Next.js frontend on Azure Static Web Apps.

---

## Pre-scaffold verification

| Signal      | Value   | Severity | Notes                                                                                                           |
| ----------- | ------- | -------- | --------------------------------------------------------------------------------------------------------------- |
| npm package | not run | —        | Not a JS-family starter; npm check skipped                                                                      |
| GitHub repo | not run | —        | `docs_url` is `https://learn.microsoft.com/aspnet/core` — not a GitHub URL; `gh` CLI also unavailable in env   |

Registry card `last_updated: 2026-04-18` (~5 weeks before scaffold date) observed as informational; freshness appears normal. No recency warning emitted.

---

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n bootstrap-scaffold --no-restore`

**Strategy**: scaffold into a temp directory then move files up (subdir-then-move)
**Exit code**: 0
**Files moved**: 6 (`Properties`, `appsettings.Development.json`, `appsettings.json`, `bootstrap-scaffold.csproj`, `bootstrap-scaffold.http`, `Program.cs`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (no `.gitignore` emitted by `dotnet new webapi`)
**bootstrap-scaffold cleanup**: deleted

**Notable scaffold output**:
- 4 non-fatal workload warnings: `Workload set version 10.0.204.1 has missing manifests — run "dotnet workload repair"`. These are environment-level warnings; the template was created successfully.
- `--no-restore` flag intentional per cmd_template; `dotnet restore` not run during scaffold.

---

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Status**: failed to run
**Reason**: MSBuild SDK resolver failure — workload manifests missing (`Workload set version 10.0.204.1 has missing manifests`). Same root cause as the non-fatal scaffold warnings. Fix: run `dotnet workload repair`, then re-run `dotnet list package --vulnerable --include-transitive` manually.

**Partial output**:
```
error MSB4242: SDK Resolver Failure: "The SDK resolver "Microsoft.DotNet.MSBuildWorkloadSdkResolver" failed..."
Restore failed with 1 error(s) in 0,3s
```

**Recommended action**: After running `dotnet workload repair`, verify the project has no vulnerable packages with:
```
dotnet restore
dotnet list package --vulnerable --include-transitive
```

---

## Hints recorded but not acted on

| Hint                    | Value             |
| ----------------------- | ----------------- |
| bootstrapper_confidence | first-class       |
| quality_override        | false             |
| path_taken              | standard          |
| self_check_answers      | null              |
| team_size               | solo              |
| deployment_target       | azure-app-service |
| ci_provider             | github-actions    |
| ci_default_flow         | auto-deploy-on-merge |
| has_auth                | false             |
| has_payments            | false             |
| has_realtime            | false             |
| has_ai                  | false             |
| has_background_jobs     | false             |

`deployment_target`, `ci_provider`, and `ci_default_flow` were read and logged. No CI/CD scaffolding in v1.

---

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- Run `dotnet workload repair` to fix missing workload manifests, then run `dotnet restore` and `dotnet list package --vulnerable --include-transitive` to complete the security audit.
- **Azure App Service wiring**: add a `.github/workflows/azure-app-service.yml` GitHub Actions workflow to deploy to Azure App Service on merge.
- **Wire frontend ↔ backend**: configure the Next.js app to call this API (environment variable for the API base URL; CORS policy on the .NET side).
- `git init` at the monorepo root (if not already done) and commit both `api/` and the root Next.js files.
