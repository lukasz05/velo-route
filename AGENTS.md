# Repository Guidelines

VeloRoute is a free road-cycling loop-route planner: Next.js 15 / React 19 / TypeScript / Tailwind v4 frontend (Azure Static Web Apps) + ASP.NET Core .NET 10 minimal API backend (Azure App Service). See `@context/foundation/prd-v2.md` for full current scope (`prd.md` is the frozen v1 doc).

## Hard Rules

- **Branch per change**: `git checkout -b <change-id>` before any implementation. Never commit directly to `main`.
- **Docs stay current**: update `context/` and `CLAUDE.md` in the same commit as any code that makes them stale.
- **Never add `#nullable disable`** in .NET code.
- **Never auto-modify `context/`** — it is the human/agent knowledge base, modified by hand only.
- **Next.js 15 / React 19 breaking changes**: check `node_modules/next/dist/docs/` before writing Next.js code — training data may reflect older APIs.

## Project Structure

`src/frontend/` — Next.js 15, React 19, TypeScript, Tailwind v4, App Router  
`src/backend/` — ASP.NET Core .NET 10, minimal API (`Program.cs`, no controllers folder), root namespace `VeloRoute`; `Data/` (EF Core entities + `AppDbContext`), `Migrations/`, `Auth/` (shared auth helpers), `Routing/` (ORS client + loop-route generator)  
`context/` — knowledge base (PRD, tech-stack docs, per-change logs); never auto-modified  

Each project manages its own dependencies independently. See `@.github/copilot-instructions.md` for full conventions.

## Commands

**Frontend** (run from `src/frontend/`):

- `npm run dev` — dev server at http://localhost:3000
- `npm run lint` — ESLint via `eslint.config.mjs` (`next/core-web-vitals` + `next/typescript`)
- `npm test` — Vitest single-run; `npm run coverage` for coverage report

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
- **Backend**: xUnit 2.9.3; test files named `*Tests.cs` under `src/backend/VeloRoute.Tests/Routing/`.
- Run focused test: `npm test -- <pattern>` (frontend) or `dotnet test --filter <name>` (backend).

## Commits & CI

Conventional Commits: `<type>(<scope>): <subject>` — types `feat|fix|docs|style|refactor|test|chore|perf`, subject ≤50 chars, imperative mood, no period. One logical change per commit.

CI: backend `dotnet test` must pass before Azure App Service deploy triggers. Frontend builds and deploys to Azure Static Web Apps on push to `main`; PRs get a preview environment.
