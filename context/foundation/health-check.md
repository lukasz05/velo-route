---
project: velo-route
checked_at: 2026-07-04T00:00:00Z
health_status: needs-attention
context_type: brownfield
language_family: multi
stack_assessment_available: true
checks_run:
  - lockfile
  - dependency_audit
  - outdated_deps
  - test_runner
  - ci_cd
  - configuration
audit_findings:
  critical: 0
  high: 1
  moderate: 3
  low: 0
test_runner_detected: true
ci_provider: GitHub Actions
recommended_fixes: 6
---

## Dependency Health

### Lockfile

**JS/TS (`src/frontend/`)**
```
Status: present (package-lock.json)
Package manager: npm
```

**. NET (`src/backend/`)**
```
Status: missing — no packages.lock.json
Package manager: dotnet (NuGet)
```

NuGet packages are not version-pinned. Builds on different machines or CI runners may restore different patch versions, making it harder for the agent to reason about exact dependency state.

Fix: enable RestoreLockedMode and generate a lockfile:
```xml
<!-- VeloRoute.csproj and VeloRoute.Tests.csproj PropertyGroup -->
<RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
```
Then run `dotnet restore --lock-file-path packages.lock.json` from `src/backend/`.

---

### Security Audit

**JS/TS** (`npm audit`)
```
Tool: npm audit --json
Summary: 0 CRITICAL, 0 HIGH, 3 MODERATE, 0 LOW
Direct vs transitive: all 3 findings are transitive
```

MODERATE findings (informational — no direct action needed):

- **postcss** <8.5.10 — [GHSA-qx2v-qp2m-jg93](https://github.com/advisories/GHSA-qx2v-qp2m-jg93): XSS via unescaped `</style>` in CSS Stringify output (CVSS 6.1). Bundled inside `next/node_modules/postcss` — not the project's own postcss. Fix: upgrade `next` to a version that bundles postcss ≥8.5.10 (see Outdated Dependencies below).
- **js-yaml** 4.0.0–4.1.1 — [GHSA-h67p-54hq-rp68](https://github.com/advisories/GHSA-h67p-54hq-rp68): Quadratic-complexity DoS via merge key repeated aliases (CVSS 5.3). Transitive tooling dependency.
- **next** 9.3.4-canary.0–16.3.0-canary.5 — cascade from the postcss finding above. Resolves when `next` is upgraded.

**. NET** (`dotnet list package --vulnerable --include-transitive`)
```
Tool: dotnet list package --vulnerable --include-transitive
Summary: 0 CRITICAL, 1 HIGH, 0 MODERATE, 0 LOW
Direct vs transitive: 1 transitive
```

#### HIGH findings

- **Microsoft.OpenApi** 2.0.0 — [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc): present as a transitive dependency in both `VeloRoute` and `VeloRoute.Tests`. Pulled in via `Microsoft.AspNetCore.OpenApi` (10.0.7) and/or `Swashbuckle.AspNetCore.SwaggerUI` (7.3.0). Fix: check the advisory for the minimum patched version, then add a direct `<PackageReference Include="Microsoft.OpenApi" Version="[patched]" />` to `VeloRoute.csproj` to pin the transitive to a safe version, or upgrade the parent packages if they bundle a patched version.

---

### Outdated Dependencies

```
Packages with major version gaps: 4
```

- **@types/node**: 20.19.41 → 26.1.0 (6 major versions behind) — direct devDependency
- **next**: 15.5.18 → 16.2.10 (1 major version behind) — direct dependency; upgrade also resolves the postcss MODERATE finding
- **typescript**: 5.9.3 → 6.0.3 (1 major version behind) — direct devDependency
- **eslint**: 9.39.4 → 10.6.0 (1 major version behind) — direct devDependency (tied to eslint-config-next version)

Note: `next` v16 is a major release; review the migration guide before upgrading. `typescript` v6 also has breaking changes.

---

## Test Suite

**. NET**
```
Test runner: xUnit 2.9.3
Tests found: 59 total
Test execution: passing (56 passed, 3 skipped)
Configuration: VeloRoute.Tests/VeloRoute.Tests.csproj
Framework: xUnit 2.9.3 + Microsoft.AspNetCore.Mvc.Testing
```

The 3 skipped tests are live ORS smoke tests (intentionally skipped when `ORS_API_KEY` is not set).

---

**JS/TS**
```
Test runner: not detected
Tests found: not applicable
Test execution: not attempted
```

No test script in `src/frontend/package.json`. No jest, vitest, or playwright in devDependencies.

⚠ No frontend test runner. The agent cannot verify its own changes to components, hooks, or route handlers.

Recommended: install Vitest (unit/component tests) and optionally Playwright (E2E).
```bash
cd src/frontend
npm install -D vitest @vitejs/plugin-react jsdom @testing-library/react @testing-library/user-event
```
Then add to `package.json` scripts: `"test": "vitest run"` and `"test:watch": "vitest"`.

After installing, add this block to `src/frontend/AGENTS.md`:
```markdown
## Testing
- Test runner: Vitest
- Run: `npm test` (single run) or `npm run test:watch` (watch mode)
- Unit tests: co-located with source as `*.test.ts(x)`
- Coverage: `npm run coverage`
```

---

## CI/CD

```
Provider: GitHub Actions
Configuration: .github/workflows/ (2 workflows)
```

**Frontend — `azure-static-web-apps-purple-sky-08f4fb710.yml`**

| Stage      | Status | Notes                                               |
|------------|--------|-----------------------------------------------------|
| Lint       | ✗      | No `npm run lint` step                              |
| Test       | ✗      | No test runner configured yet                       |
| Build      | ✓      | SWA deploy action builds the Next.js app            |
| Type check | ✗      | No `tsc --noEmit` step                              |
| Security   | ✗      | No `npm audit` step                                 |

**Backend — `backend.yml`**

| Stage      | Status | Notes                                               |
|------------|--------|-----------------------------------------------------|
| Lint       | ✗      | No `dotnet format --verify-no-changes` step         |
| Test       | ✓      | `dotnet test` on every push and PR                  |
| Build      | ✓      | `dotnet publish` on main pushes                     |
| Type check | ✓      | Implicit — dotnet compile enforces Nullable+strict  |
| Security   | ✗      | No `dotnet list package --vulnerable` step          |

Both workflows are path-scoped (frontend: `src/frontend/**`, backend: `src/backend/**`) — solid practice.

---

## Configuration

### Medium severity

- **Prettier / code formatter (JS/TS)** — no `.prettierrc*` found and no formatter in devDependencies. Without a formatter, the agent's output style will be inconsistent across files. Fix: `npm install -D prettier` in `src/frontend/`, add `.prettierrc.json` at repo root or `src/frontend/`, and add `"format": "prettier --write src/"` to `package.json` scripts.

### Low severity

- **`.editorconfig`** — absent from repo root. Without it, different editors (VS Code, Rider, vim) may use different line endings and indent sizes. Fix: add `.editorconfig` at repo root with `indent_style = space`, `indent_size = 2` for JS/TS and `4` for C#, `end_of_line = lf`, `charset = utf-8`.

- **`.env.example`** — no `.env.example` at root or in `src/backend/`. The project uses user secrets (`UserSecretsId` in `VeloRoute.csproj`) and likely requires an ORS API key, but required variables are not documented for new contributors. Fix: add `.env.example` listing all required environment variables (e.g., `ORS_API_KEY=`, `ORS_BASE_URL=`) so new contributors and CI setup know what to configure.

---

## Stack Assessment Cross-Reference

```
Stack assessment: context/foundation/stack-assessment.md
Agent readiness (from stack-assess): ready
```

| Quality Gate Gap                     | Health-Check Finding                                               | Status    |
|--------------------------------------|--------------------------------------------------------------------|-----------|
| No frontend test runner (observed)   | Confirmed: no test runner in package.json; CI has no test step     | Reinforced |
| Next.js 15/React 19 version freshness| AGENTS.md compensation in place at src/frontend/AGENTS.md          | Mitigated  |
| Minimal API convention gap           | Confirmed: copilot-instructions.md documents Program.cs convention | Mitigated  |

Stack-assess gave a clean `ready` verdict with one observed gap (no frontend test runner). Health-check reinforces that gap and adds the HIGH .NET advisory and missing NuGet lockfile as new findings not visible from stack analysis alone.

---

## Recommended Fixes

### Fix before agent work (Category A)

---

### 1. Resolve HIGH vulnerability — Microsoft.OpenApi 2.0.0

**Impact**: A HIGH-severity transitive vulnerability in the OpenAPI/Swagger tooling chain. Swagger UI is development-only, but the affected library is compiled into the app binary. Until patched, any agent-assisted PR touches code with a known HIGH advisory.
**Severity**: high
**Effort**: quick (< 5 min)
**Fix**:

1. Check the [advisory](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc) for the minimum safe version.
2. Add a direct reference in `src/backend/VeloRoute/VeloRoute.csproj` to pin the transitive:
```xml
<PackageReference Include="Microsoft.OpenApi" Version="[minimum safe version]" />
```
3. Run `dotnet restore` and recheck: `dotnet list package --vulnerable --include-transitive`.

---

### 2. Add a frontend test runner

**Impact**: Without tests, the agent has no way to verify that frontend changes are correct. Every frontend PR is unverifiable by automated tooling — the agent can write code but cannot confirm it works.
**Severity**: high
**Effort**: moderate (15–30 min)
**Fix**:

```bash
cd src/frontend
npm install -D vitest @vitejs/plugin-react jsdom @testing-library/react @testing-library/user-event
```

Add to `src/frontend/package.json` scripts:
```json
"test": "vitest run",
"test:watch": "vitest",
"coverage": "vitest run --coverage"
```

Add `vitest.config.ts` at `src/frontend/`:
```ts
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
export default defineConfig({
  plugins: [react()],
  test: { environment: 'jsdom' },
})
```

Update `src/frontend/AGENTS.md` with the testing block shown in the Test Suite section above.

---

### 3. Pin NuGet lockfile for .NET

**Impact**: Without a lockfile, NuGet restores may silently resolve different transitive versions on CI vs local. The agent cannot reason about the exact dependency graph, and a surprise patch upgrade could break tests or introduce new vulnerabilities.
**Severity**: medium
**Effort**: quick (< 5 min)
**Fix**:

In both `VeloRoute.csproj` and `VeloRoute.Tests.csproj`, add to `<PropertyGroup>`:
```xml
<RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
```

Generate the lockfile locally:
```bash
cd src/backend
dotnet restore --lock-file-path packages.lock.json
```

Commit the generated `packages.lock.json`. Add `packages.lock.json` to the backend CI workflow's restore step.

---

### 4. Add a Prettier formatter (JS/TS)

**Impact**: Without a formatter, the agent's output style varies across files. Code review noise increases, and the agent may reformat existing lines as a side-effect of edits.
**Severity**: medium
**Effort**: quick (< 5 min)
**Fix**:

```bash
cd src/frontend
npm install -D prettier
```

Add `src/frontend/.prettierrc.json`:
```json
{
  "semi": false,
  "singleQuote": true,
  "trailingComma": "es5",
  "printWidth": 100
}
```

Add to `package.json` scripts: `"format": "prettier --write src/"`.

---

### 5. Update `@types/node` to v26

**Impact**: The project pins `@types/node: ^20` while Node types are now at v26 — 6 major versions behind. This can cause missing type definitions for newer Node APIs and reduces type-checking accuracy for any server-side code in Next.js route handlers.
**Severity**: low
**Effort**: quick (< 5 min)
**Fix**:

```bash
cd src/frontend
npm install -D @types/node@^26
```

Verify `npm run build` and `npm run lint` still pass.

---

### 6. Add `.env.example`

**Impact**: Required environment variables (ORS API key, base URL) are not documented anywhere outside user secrets. New contributors and CI setup have no template for what must be configured, slowing onboarding and making agent-assisted environment setup impossible.
**Severity**: low
**Effort**: quick (< 5 min)
**Fix**:

Create `src/backend/.env.example`:
```env
# OpenRouteService API key — get one at openrouteservice.org
ORS_API_KEY=

# ORS base URL (optional — defaults to https://api.openrouteservice.org)
# ORS_BASE_URL=https://api.openrouteservice.org
```

---

### Addressed in upcoming lessons (Category B)

---

### Frontend CI — missing lint, type-check, test stages

The frontend workflow (`azure-static-web-apps-purple-sky-08f4fb710.yml`) only builds and deploys — it has no lint, type-check, or test steps. This is a real gap, but adding a test step requires a test runner to exist first (Category A fix #2 above). CI hardening is covered in the infrastructure and CI/CD lesson.

---

### Backend CI — missing lint and security scan stages

The backend workflow runs tests and deploys but does not check `dotnet format --verify-no-changes` or `dotnet list package --vulnerable`. Both are quick additions, but they're covered as part of CI hardening in the infrastructure lesson.

---

## Summary

```
Health status: needs-attention
```

VeloRoute's backend is in strong shape: xUnit runs 56 tests cleanly, type safety is enforced at the compiler level in both stacks, and CI deploys both frontend and backend on push to main. The two gaps that matter most for agent-assisted work are the missing frontend test runner (the agent has no way to verify frontend changes) and a HIGH-severity advisory in the transitive `Microsoft.OpenApi 2.0.0` dependency. Both are addressable in under 30 minutes. The remaining findings (NuGet lockfile, formatter, outdated `@types/node`, missing `.env.example`) are quality-of-life items that reduce friction but do not block agent collaboration.

Next step: address Category A fixes 1 and 2 (HIGH vulnerability pin + Vitest setup), then proceed to agent onboarding.
