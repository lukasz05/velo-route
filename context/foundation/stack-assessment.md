---
project: VeloRoute
assessed_at: 2026-07-04T00:00:00Z
agent_readiness: ready
context_type: brownfield
stack_components:
  frontend_language: TypeScript 5
  frontend_framework: Next.js 15 (App Router) + React 19
  frontend_build_tool: Next.js built-in / npm
  frontend_test_runner: null
  backend_language: C# / .NET 10
  backend_framework: ASP.NET Core minimal API
  backend_test_runner: xUnit 2.9.3
  package_manager: npm
  css_framework: Tailwind v4
  ci_provider: GitHub Actions
  deployment_frontend: Azure Static Web Apps
  deployment_backend: Azure Web Apps (velo-route-api)
gates_passed: 10
gates_failed: 0
---

## Stack Components

**Frontend — TypeScript 5 + Next.js 15 (App Router) + React 19.** The frontend is a Next.js 15 App Router application with React 19 and TypeScript 5. Strict mode is enabled (`tsconfig.json`). Styling uses Tailwind v4 (config surface moved to `postcss.config.mjs` — no `tailwind.config.js`). The `@/*` import alias maps to `src/frontend/src/*`. Dependencies are managed with npm (`package-lock.json`). No frontend test runner is configured.

**Backend — C# / .NET 10 + ASP.NET Core minimal API.** The backend is an ASP.NET Core Minimal API project targeting .NET 10. Nullable reference types are enabled (`<Nullable>enable</Nullable>`). Endpoints are registered directly in `Program.cs` — no controllers folder. The `Routing/` directory holds the ORS HTTP client, loop-route generator, and data models. Integration tests run via xUnit 2.9.3 in `VeloRoute.Tests/`, with `Microsoft.AspNetCore.Mvc.Testing` for in-process test hosting.

**CI/CD + Deployment.** Two GitHub Actions workflows: `backend.yml` runs xUnit tests and deploys to Azure Web Apps (`velo-route-api`) on push to main; the SWA workflow deploys the frontend to Azure Static Web Apps. Both workflows are path-scoped to their respective source trees.

**Instruction files.** The project has layered instruction coverage: root `CLAUDE.md` (via `@.github/copilot-instructions.md`) documents conventions, dev commands, and architecture; `src/frontend/AGENTS.md` warns agents that Next.js 15 + React 19 has breaking changes from training data and directs them to `node_modules/next/dist/docs/`; `src/backend/VeloRoute/README.md` documents running, configuration, and project structure.

## Quality Gate Assessment

| Component | Typed | Convention | Training Data | Documented | Verdict |
|-----------|-------|------------|---------------|------------|---------|
| TypeScript 5 | ✓ | — | — | — | pass |
| Next.js 15 + React 19 | — | ✓ | ✓ | ✓ | pass |
| npm + Next.js build | — | ✓ | ✓ | ✓ | pass |
| xUnit 2.9.3 | — | — | ✓ | ✓ | pass |
| C# / .NET 10 | ✓ | — | — | — | pass |
| ASP.NET Core minimal API | — | ✓ | ✓ | ✓ | pass |

Legend: ✓ = pass, — = not applicable

### Gate Details

**Type safety**

TypeScript: `src/frontend/tsconfig.json` has `"strict": true`, enabling the full strict flag set (noImplicitAny, strictNullChecks, etc.). The `@types/react` and `@types/react-dom` dev dependencies are present.

C#: `VeloRoute.csproj` has `<Nullable>enable</Nullable>`, making null safety a compiler-enforced invariant across the codebase.

**Convention-based layout**

Next.js App Router: file-based routing is enforced by framework convention — `src/frontend/src/app/layout.tsx` is the root layout, `page.tsx` files define routes, `api/` directories define route handlers. Agents navigating the codebase can predict file locations without reading every file.

ASP.NET Core: strong conventions for the DI container, middleware pipeline registration order, configuration system (appsettings.json + environment variables + user secrets), and HTTP client factory. Minimal API replaces controller conventions with direct endpoint registration in `Program.cs`; the backend README documents this layout explicitly.

**Training data representation**

Next.js + React: the dominant pairing in the JS/TS training corpus. Minor friction: Next.js 15 + React 19 introduced breaking changes (server actions, `use client` semantics, async components) that diverge from older training data. The project compensates with `src/frontend/AGENTS.md`, which directs agents to read `node_modules/next/dist/docs/` before writing code.

ASP.NET Core: mainstream within the C# ecosystem. Minimal API style (introduced in .NET 6, stabilized by .NET 8+) is well-represented in training data. xUnit is the de-facto .NET test framework.

**Documentation quality**

Next.js: versioned docs at nextjs.org/docs; per-version migration guides; examples per API.

ASP.NET Core: versioned reference at learn.microsoft.com/aspnet/core; minimal API guide is a first-class doc section. xUnit: xunit.net, stable and current.

## Gaps & Compensation

No gates failed. The stack is agent-friendly out of the box.

### Observed gaps (not gate failures)

**1. No frontend test runner**

There is no jest, vitest, playwright, or cypress in `src/frontend/package.json`. This is not a quality gate criterion, but it is a real gap for agent-driven TDD on the frontend. Agents that are asked to write frontend tests will lack a runner to execute them against.

Recommended addition — when a test runner is adopted, add to `src/frontend/AGENTS.md`:

```
## Testing
Frontend tests use [vitest / playwright — choose one]. Run with `npm test`.
Unit tests live alongside source files as `*.test.ts(x)`. E2E tests live in `tests/`.
```

**2. Next.js 15 / React 19 version freshness**

Already compensated by `src/frontend/AGENTS.md`. No additional action required. Monitor as Next.js 15 docs mature and update the AGENTS.md pointer if the `node_modules/next/dist/docs/` pattern stops working after a Next.js upgrade.

**3. Minimal API convention gap**

ASP.NET Core minimal API removes the controller folder convention. This is compensated by the backend README, but agents working on the backend need to know that endpoint registration happens in `Program.cs`, not in a controllers directory. This is currently documented in `copilot-instructions.md` ("endpoints are registered directly in `Program.cs`, not in controller classes"). No additional action required.

### Recommended Instruction File Additions

All material gaps are already covered. If a frontend test runner is added in the future, add this block to `src/frontend/AGENTS.md`:

```markdown
## Testing
- Test runner: [vitest | playwright]
- Run: `npm test`
- Unit tests: co-located with source as `*.test.ts(x)`
- E2E tests: `tests/` directory
- Coverage: `npm run coverage`
```

## Summary

VeloRoute's stack is **agent-ready out of the box**. Both the TypeScript/Next.js frontend and the C# / ASP.NET Core backend pass all four quality gates, and the existing instruction file layer (root CLAUDE.md, copilot-instructions.md, frontend AGENTS.md, backend README) already compensates for the one non-obvious friction point (Next.js 15 breaking changes from training data).

Key strengths:
- Full type safety enforced at the compiler level in both stacks
- Framework conventions (App Router file-based routing, ASP.NET Core DI/middleware) give agents a predictable navigation model
- Strong training data representation for all components
- Instruction files already in place for the main version-freshness risk

Single gap to watch:
- No frontend test runner — budget time to add vitest or playwright and document it in AGENTS.md before agents are asked to write frontend tests

Recommended next step: `/10x-health-check`
