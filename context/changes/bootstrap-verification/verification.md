---
bootstrapped_at: 2026-05-22T17:47:00Z
starter_id: next
starter_name: "Next.js"
project_name: velo-route
language_family: multi
package_manager: npm
cwd_strategy: subdir-then-move
bootstrapper_confidence: first-class
phase_3_status: ok
audit_command: "null"
---

## Hand-off

```yaml
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
```

### Why this stack

VeloRoute is a two-project, multi-language architecture: a **Next.js frontend** (`next`) for the interactive map UI and a **.NET ASP.NET Core webapi backend** (`dotnet`) for route computation and GPX generation. Next.js passes all four agent-friendly gates (TypeScript end-to-end, file-based routing conventions, dominant in JS training data, versioned Vercel docs) and is the `starter_id` bootstrapper scaffolds first. The .NET API is the natural C# home for the compute-heavy graph-traversal logic; the `dotnet new webapi` template scaffolds separately. Both projects deploy to Azure: Next.js to **Azure Static Web Apps** (SSR via Azure Functions, free tier, GitHub Actions integration), .NET to **Azure App Service**. Bootstrapper confidence is `first-class` — the `next` card's deployment defaults don't include Azure, so the GitHub Actions workflow for Azure Static Web Apps requires a brief manual wiring step post-scaffold. VeloRoute v1 has no auth, payments, realtime, AI, or background-job requirements.

---

## Pre-scaffold verification

| Signal      | Value    | Severity | Notes                                                                                                                  |
| ----------- | -------- | -------- | ---------------------------------------------------------------------------------------------------------------------- |
| npm package | not run  | —        | `hints.language_family` is `multi` (not `js`); npm check skipped per routing rules                                    |
| GitHub repo | not run  | —        | `docs_url` is `https://nextjs.org/docs` — not a GitHub URL; no GitHub `pushed_at` signal available                    |

Registry card `last_updated: 2026-04-15` (~5 weeks before scaffold date) observed as informational; card freshness appears normal. No recency warning emitted.

---

## Scaffold log

**Resolved invocation**: `npx create-next-app@latest bootstrap-scaffold --ts --tailwind --eslint --app --src-dir --import-alias "@/*" --use-npm`

> Note: The `.bootstrap-scaffold` temp directory name was adjusted to `bootstrap-scaffold` (no leading dot) because `create-next-app` enforces npm naming restrictions that reject names starting with a period. The subdir-then-move mechanic was otherwise applied identically.

**Strategy**: scaffold into a temp directory then move files up (subdir-then-move)
**Exit code**: 0
**Files moved**: 15 (`.next`, `node_modules`, `public`, `src`, `.gitignore`, `AGENTS.md`, `CLAUDE.md`, `eslint.config.mjs`, `next-env.d.ts`, `next.config.ts`, `package-lock.json`, `package.json`, `postcss.config.mjs`, `README.md`, `tsconfig.json`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: moved silently (absent in cwd prior to scaffold)
**bootstrap-scaffold cleanup**: deleted

**Notable scaffold output**:
- `create-next-app@16.2.6` installed
- Used template: `app-tw` (App Router + Tailwind)
- npm engine warning: `eslint-visitor-keys@5.0.1` requires Node `^20.19.0 || ^22.13.0 || >=24`; current is `v20.11.0`. Non-fatal warning.
- npm install reported: `added 359 packages, audited 360 packages`
- npm install reported: `2 moderate severity vulnerabilities` (see Post-scaffold audit below)
- `next typegen` ran successfully: route types generated

---

## Post-scaffold audit

**Tool**: skipped — no built-in audit tool for `multi`

**Recommended external tool**: No single audit tool covers this multi-language stack. Consider running the per-language tools separately:
- For the Next.js/JS layer: `npm audit` (already surfaced 2 moderate findings during install — run `npm audit` from cwd for full details)
- For the .NET backend (once scaffolded): `dotnet list package --vulnerable --include-transitive`

**Informal note from scaffold install output**: `npm install` reported 2 moderate severity vulnerabilities during scaffolding. To see full details run `npm audit` from the project root. No CRITICAL or HIGH findings were indicated by the install output.

---

## Hints recorded but not acted on

| Hint                    | Value                   |
| ----------------------- | ----------------------- |
| bootstrapper_confidence | first-class             |
| quality_override        | false                   |
| path_taken              | standard                |
| self_check_answers      | null                    |
| team_size               | solo                    |
| deployment_target       | azure-static-web-apps   |
| ci_provider             | github-actions          |
| ci_default_flow         | auto-deploy-on-merge    |
| has_auth                | false                   |
| has_payments            | false                   |
| has_realtime            | false                   |
| has_ai                  | false                   |
| has_background_jobs     | false                   |

`bootstrapper_confidence` is `first-class` — the `next` card's `deployment_defaults` do not include Azure Static Web Apps. A manual wiring step is required post-scaffold (GitHub Actions workflow + Azure Static Web Apps resource). No automated compensation in v1; this is noted here for the future M1L4 skill.

`deployment_target`, `ci_provider`, and `ci_default_flow` were read and logged. No CI/CD scaffolding in v1.

All `has_*` flags are `false`; no feature scaffold was expected.

---

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- Run `npm audit` from the project root to review the 2 moderate vulnerabilities flagged during install.
- **Azure Static Web Apps wiring**: the `next` card's deployment defaults don't include Azure. You'll need to manually create a `.github/workflows/azure-static-web-apps.yml` workflow and link it to an Azure Static Web Apps resource (the Azure portal / CLI can generate the workflow file).
- **Scaffold the .NET backend**: re-run `/10x-tech-stack-selector` with `starter_id: dotnet` (or update `context/foundation/tech-stack.md` to point at `dotnet`), choose a subdirectory (e.g., `api/`) as the scaffold target, then re-invoke `/10x-bootstrapper`.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep (none were created in this run).
- `git init` (if you have not already) to start your own repo history, then commit the scaffolded files.
