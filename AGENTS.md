# Repository Guidelines

VeloRoute is a free road-cycling loop-route planner: Next.js 15 / React 19 / TypeScript / Tailwind v4 frontend (Azure Static Web Apps) + ASP.NET Core .NET 10 minimal API backend (Azure App Service). See `@context/foundation/prd-v2.md` for full current scope (`prd.md` is the frozen v1 doc).

## Hard Rules

- **Branch per change**: `git checkout -b <change-id>` before any implementation. Never commit directly to `main`.
- **Docs stay current**: update `context/` and `CLAUDE.md` in the same commit as any code that makes them stale.
- **Never add `#nullable disable`** in .NET code.
- **Never auto-modify `context/`** — it is the human/agent knowledge base, modified by hand only.
- **Next.js 15 / React 19 breaking changes**: training data may reflect older APIs. `next` ships no markdown docs inside `node_modules`, so verify against the installed package's types and source under `node_modules/next/`, or the official Next.js 15 docs.

## Project Structure

`src/frontend/` — Next.js 15, React 19, TypeScript, Tailwind v4, App Router  
`src/backend/` — ASP.NET Core .NET 10, minimal API (`Program.cs`, no controllers folder), root namespace `VeloRoute`; `Data/` (EF Core entities + `AppDbContext`), `Migrations/`, `Auth/` (shared auth helpers), `Routing/` (ORS client + loop-route generator + route metadata validation), `Json/` (shared JSON converters)  
`context/` — knowledge base (PRD, tech-stack docs, per-change logs); never auto-modified  

Each project manages its own dependencies independently. See `@.github/copilot-instructions.md` for full conventions.

## Commands

**Frontend** (run from `src/frontend/`):

- `npm run dev` — dev server at http://localhost:3000
- `npm run lint` — ESLint via `eslint.config.mjs` (`next/core-web-vitals` + `next/typescript`)
- `npm test` — Vitest single-run; **must pass before deploy runs in CI**; `npm run coverage` for coverage report
- `npm run e2e` — Playwright (chromium); **must pass before deploy runs in CI**; starts the .NET backend and a production build itself. One-time: `npx playwright install chromium`

**Backend** (run from `src/backend/`):

- `dotnet run` — API at http://localhost:5098; Swagger UI at `/swagger`
- `dotnet test` — xUnit suite; **must pass before deploy runs in CI**

## Coding Conventions

- Frontend component filenames: PascalCase (`RouteForm.tsx`, `ErrorMessage.tsx`).
- Import alias `@/*` maps to `src/frontend/src/*`.
- Tailwind v4 config in `postcss.config.mjs` — no `tailwind.config.js` exists.
- TypeScript strict mode on (`tsconfig.json`); nullable reference types enabled in .NET.
- Backend endpoints registered in `Program.cs` (minimal API pattern).

## Testing

- **Frontend**: Vitest 4 + React Testing Library; tests co-located as `*.test.tsx`; global setup in `src/frontend/src/test-setup.ts`.
- **Frontend e2e**: Playwright 1.63.0; specs in `src/frontend/e2e/` as `*.spec.ts` (excluded from Vitest); conventions in `context/foundation/test-plan.md` §6.5.
- **Backend**: xUnit 2.9.3; test files named `*Tests.cs` under `src/backend/VeloRoute.Tests/Routing/`.
- Run focused test: `npm test -- <pattern>` (frontend) or `dotnet test --filter <name>` (backend).

## Commits & CI

Conventional Commits: `<type>(<scope>): <subject>` — types `feat|fix|docs|style|refactor|test|chore|perf`, subject ≤50 chars, imperative mood, no period. One logical change per commit.

CI: backend `dotnet test` and an EF model-drift check (`dotnet ef migrations has-pending-model-changes`) must pass before Azure App Service deploy triggers. The `deploy` job then applies any pending EF migration to production Postgres via Kudu before the new binary ships, and runs a post-deploy smoke test (`/health` plus an authenticated round-trip) against the live App Service. Outside Development, the backend also refuses to start if required `Clerk:*` config is missing.

The Azure Static Web Apps deploy is gated on **two** jobs — `build_and_deploy_job` carries `needs: [test, e2e]`, so both `npm test` (Vitest) and `npx playwright test` must pass; the frontend then builds and deploys on push to `main`, and PRs get a preview environment (a failing job means no preview). That workflow now triggers on `src/backend/**` as well as `src/frontend/**`, because the e2e drives the GPX hop against a real backend — a backend-only change therefore also re-runs the deploy. `npm run build` fails if `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is unset, and a post-deploy step verifies the live bundle actually has a non-empty key baked in.
