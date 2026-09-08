# Core Anonymous Flow End-to-End — Implementation Plan

## Overview

Close `context/foundation/test-plan.md` §3 phase 5, which defends risk #7 (High × High):
*"v2 auth/library work regresses the anonymous generate → map → GPX flow; a stranger hits
a broken core product."*

Two halves, plus the tooling the phase must bring with it:

1. **Integration**: an xUnit contract test for `POST /routes/gpx`, which has none today.
2. **e2e**: a Playwright spec proving a signed-out visitor completes search → generate →
   map → GPX download in a real browser, **with clerk-js deliberately blocked** — the
   condition that reproduces risk #7's actual mechanism.
3. **Tooling + CI**: install and pin Playwright, resolve the Vitest spec-glob collision,
   and gate the Azure SWA deploy on the new job.

## Current State Analysis

- `POST /routes/gpx` (`src/backend/VeloRoute/Program.cs:407-420`) is the last hop of the
  anonymous flow and is called from three frontend files. It has **zero** tests.
- Playwright is absent repo-wide — no config, no dependency, no `e2e/` directory.
- `src/frontend/vitest.config.ts:7-11` sets no `include`/`exclude`, so Vitest 4's default
  `**/*.{test,spec}.?(c|m)[jt]s?(x)` glob will collect any Playwright spec and fail it.
- CI has two workflows. The SWA workflow
  (`.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`) runs a `test` job
  and `build_and_deploy_job` carries `needs: test`. Path filter is `src/frontend/**` only.
  No Clerk or ORS secret is wired into CI anywhere.
- `RouteInfoPanel.tsx:30-31` is the only component on the anonymous path that reads Clerk,
  and it is the component that owns the "Download GPX" button — the structural reason an
  auth failure can break an auth-free feature.

### Key Discoveries

Grounded in `research.md`, then extended by direct verification in this planning session.
Where the two disagree, the verified result below is authoritative.

- **The app survives clerk-js being blocked — settled at source, no browser needed.**
  `useUser()` returns `{ isLoaded: false, isSignedIn: undefined, user: undefined }` when
  clerk-js has not loaded (`node_modules/@clerk/shared/dist/react/index.mjs:2034-2042`),
  and `ClerkProvider` **catches** the script-load failure, emits status `"error"`, and
  `console.error`s it rather than throwing
  (`node_modules/@clerk/react/dist/ClerkProvider-COmoIHBK.mjs:1357-1362`). So
  `RouteInfoPanel` renders, `isSignedIn` is falsy, the Save block at `RouteInfoPanel.tsx:112`
  stays hidden, and the Download GPX button at `:149-155` is present. Research's open
  question #1 is closed: no bug, and the spec is deterministic.

- **`clerkMiddleware()` hard-requires `CLERK_SECRET_KEY` server-side.** Research solved only
  the publishable key. Verified: without the secret key *every* request 500s with
  `Error: @clerk/nextjs: Missing secretKey`. A synthetic
  `CLERK_SECRET_KEY=sk_test_0000…` is sufficient — anonymous requests carry no session, so
  the key is never validated against Clerk. Verified `GET /` → 200 and
  `POST /api/routes/gpx` → 200 with both synthetic keys. **This is the single fact that
  would otherwise have broken the e2e in CI.**

- **Why research missed it:** `next start` silently loads `.env.local`, so a local run is
  masked by the developer's real key. Explicit `webServer.env` removes that dependence.

- **`next start` warns under `output: 'standalone'` but works.** Verified: it serves
  HTTP 200 while printing
  `"next start" does not work with "output: standalone" configuration. Use "node .next/standalone/server.js" instead.`
  The standalone server also works, but only after copying `public/` and `.next/static`
  into `.next/standalone/` — shell-portability-sensitive steps. Research's open question #2
  is closed in favour of `next start` (rationale under Implementation Approach).

- **The non-finite branch is reachable, but not the obvious way.** A bare `NaN` JSON literal
  is rejected by `System.Text.Json` at model binding and never reaches the guard — it
  produces a `BadHttpRequestException` with a *different* response shape, no
  `code: "INVALID_INPUT"`. Verified reachable inputs: **`1e400`** (overflows to `Infinity`)
  and **quoted `"NaN"`** (Web defaults allow reading numbers from strings). Both return
  `"One or more coordinates are out of range"` / `INVALID_INPUT`.

- **Two validation branches, not three** (confirms `research.md` §1 against
  `test-plan.md:128-129`). Verified live: `{"coordinates":[]}` and `{}` both hit the *empty*
  branch; out-of-range and non-finite share the second predicate **and its message**, so a
  test cannot distinguish them by response — only by input.

- **Success response carries `Content-Type: application/gpx+xml` and no
  `Content-Disposition`** — verified. That absence is a deliberate decision
  (`context/archive/2026-06-04-gpx-export/plan.md:69-71`, "Filename is set in the browser").

- **`/health` (`Program.cs:130`) is unconditional** and returns 200 — a usable Playwright
  `webServer.url` readiness probe for the backend.

- **The map style is external**: `RouteMap.tsx:71` loads
  `https://tiles.openfreemap.org/styles/liberty`. Left live, it is a third-party dependency
  and a flake source inside a test that is supposed to be hermetic.

- **Playwright 1.63.0** is current latest (verified via `npm view`, published 2026-09-04).

- **`node_modules/next/dist/docs/` does not exist** — verified; `next` ships no markdown at
  that path at all. The instruction at `.github/copilot-instructions.md:8` and
  `src/frontend/AGENTS.md:3` cannot be followed as written.

## Desired End State

- `dotnet test` covers `POST /routes/gpx`: success shape, both validation branches, and
  anonymous accessibility.
- `npx playwright test` from `src/frontend/` starts the .NET backend and the Next server,
  runs one anonymous spec with clerk-js blocked, and asserts a `veloroute-*.gpx` download.
- `npm test` (Vitest) still passes and does **not** collect the Playwright spec.
- A frontend **or backend** change runs the e2e in CI, and the SWA deploy is blocked on it.
- `test-plan.md` §4 pins Playwright, §6.5 is no longer "TBD", and phase 5 reads `shipped`.

Verification: `dotnet test` green from `src/backend/`; `npm test` and
`npx playwright test` green from `src/frontend/`; a PR shows the e2e job as a required
predecessor of `build_and_deploy_job`.

## What We're NOT Doing

- **Not covering the `/r/[token]` share page or `/my-routes/[id]`**, despite the GPX
  download handler being triplicated across all three. Both need a real share token, which
  needs a signed-in user and Postgres — the opposite of this phase's point.
- **Not extracting the triplicated `handleDownload`.** Real duplication, wrong change.
- **Not adding e2e error-path specs** (NO_ROUTE alert, GPX failure text). Already covered
  cheaply by Vitest component tests; promoting them is the anti-pattern `test-plan.md` §1
  names explicitly.
- **Not asserting map canvas pixels** (`test-plan.md` §7).
- **Not re-testing GPX serialisation.** `GpxSerializerTests` already covers it with 5 cases
  including `pl-PL`/`de-DE` culture flips. The new test owns the *HTTP contract* only.
- **Not adding any CI secret.** Both Clerk keys are synthetic and inline.
- **Not touching risk #8** (config-failure loudness) — that is phase 6.

## Implementation Approach

**Two independent halves, sequenced so value lands early.** Phase 1 (xUnit) needs no new
tooling and defends the last hop of the anonymous flow on its own; it is committable even
if the browser half stalls. Phases 2–4 build the browser half incrementally: harness, then
spec, then the CI gate.

**Mocking split (decided).** The e2e mocks `/api/geocode` and `/api/routes/loop` in the
browser but lets `/api/routes/gpx` reach a real .NET backend. Geocode and loop both depend
on ORS, so mocking them removes the only secret dependency; GPX has no external dependency,
so the real hop is free and catches fixture-vs-backend drift on the contract that matters.
The map style is stubbed with a minimal valid MapLibre style so the map genuinely loads
(letting `onLoad` → `isMapLoaded` → `fitBounds` run) while staying hermetic.

**`next start`, not the standalone server (decided).** Both work. `next start` wins on two
grounds: no `cp -r` build steps whose flags differ between pwsh (the developer's shell) and
bash (CI), and Playwright's `webServer.env` takes precedence over `.env.local` — Next only
applies `.env` values when the variable is not already in `process.env` — so the synthetic
keys win deterministically. The Next warning line is expected output, not a defect.

**Synthetic Clerk keys inline in `playwright.config.ts` (decided).** Clone-and-run, no
setup, impossible to accidentally exercise real credentials. Both need a comment saying so,
because two `pk_test_`/`sk_test_` strings in source read as leaked secrets otherwise.

## Critical Implementation Details

**Do not fail the run on console or page errors.** Blocking clerk-js is *designed* into this
spec, and it legitimately produces (a) a `console.error` from `ClerkProvider`'s catch block
and (b) an unhandled rejection from `loadClerkJSScript`, whose `loadScript(...).catch(err => { throw ... })`
re-throws outside any awaited chain. A `pageerror`-fails-the-test helper would make the spec
red on its intended condition.

**Do not wait for Clerk to settle.** `loadClerkJSScript` uses a 15 s `scriptLoadTimeout`, and
the aborted-script path does not short-circuit it. The UI is fully usable long before that
fires, so assert against the panel and never against an `isLoaded` state. Any
`waitForLoadState('networkidle')` risks paying the full 15 s — use element assertions.

**Block the Clerk host, not a script-name pattern.** The synthetic key decodes to frontend
API `clerk.example.com`, so `https://clerk.example.com/**` is exact and stable. A pattern
matching `clerk.browser.js` would silently stop matching if Clerk renames the bundle.

**Order inside Phase 2 matters.** The Vitest `exclude` must land in the *same commit* as the
first spec file. Add the spec first and `npm test` goes red.

---

## Phase 1: `POST /routes/gpx` Integration Test

### Overview

Cover the endpoint's HTTP contract with xUnit. No new tooling, no Postgres, no Docker
dependency for these cases specifically.

### Changes Required:

#### 1. New endpoint test class

**File**: `src/backend/VeloRoute.Tests/Routing/GpxEndpointTests.cs`

**Intent**: Encode the HTTP contract of `POST /routes/gpx` — success shape, both validation
branches, and anonymous accessibility — so a future auth or serialisation change that
breaks the last hop of the anonymous flow fails in CI rather than in a user's Garmin.

**Contract**: Class `GpxEndpointTests` in namespace `VeloRoute.Tests.Routing`, following the
existing `MethodUnderTest_Scenario_ExpectedOutcome` convention. Uses
`new VeloRouteWebApplicationFactory()` with no arguments — this leaves `AppDbContext`
registered but never instantiated, and the endpoint touches neither the DB nor ORS, so no
Postgres is required (matches `LoopRouteIntegrationTests` / `AuthMiddlewareTests`).

Request bodies are camelCase raw JSON strings:
`{"coordinates":[{"longitude":16.37,"latitude":48.20}]}`.

Cases:

- `PostRoutesGpx_ValidCoordinates_Returns200WithGpxContentType` — asserts `200`,
  `Content-Type` media type `application/gpx+xml`, body contains `<trkpt`, and **no
  `Content-Disposition` header** (encoding the deliberate no-filename decision).
- `PostRoutesGpx_EmptyCoordinates_Returns400InvalidInput` — body `{"coordinates":[]}`;
  asserts `400` and `code":"INVALID_INPUT"`.
- `PostRoutesGpx_MissingCoordinatesProperty_Returns400InvalidInput` — body `{}`; falls into
  the *same* empty branch (`Coordinates is null`).
- `PostRoutesGpx_OutOfRangeOrNonFinite_Returns400InvalidInput` — a `[Theory]` over raw JSON
  bodies, one `[InlineData]` per input: latitude `91`, latitude `-91`, longitude `181`,
  longitude `-181`, latitude `1e400` (overflows to `Infinity`), and latitude `"NaN"`
  (quoted — Web defaults read numbers from strings).
- `PostRoutesGpx_NoToken_IsNotUnauthorized` — mirrors
  `AuthMiddlewareTests.cs:30-42`; asserts `Assert.NotEqual(HttpStatusCode.Unauthorized, …)`,
  directly defending the PRD-v2 guardrail that GPX export stays reachable without auth.

**Do not** add an `[InlineData]` for a bare `NaN` JSON literal — it fails at model binding
with a different response shape and would be testing `System.Text.Json`, not the endpoint.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` from `src/backend/`
- New tests pass: `dotnet test --filter FullyQualifiedName~GpxEndpointTests` from `src/backend/`
- Full backend suite green: `dotnet test` from `src/backend/` (needs Docker; see the
  `DOCKER_API_VERSION=1.41` workaround if Testcontainers tests fail to start)

#### Manual Verification:

- Each `[InlineData]` row fails for the intended reason — temporarily invert one assertion
  and confirm the expected row is the one that goes red

---

## Phase 2: Playwright Harness

### Overview

Install and pin Playwright, add the config with its dual `webServer`, and resolve the Vitest
glob collision — proven by a trivial spec before any real assertions exist.

### Changes Required:

#### 1. Dependency

**File**: `src/frontend/package.json`

**Intent**: Pin the e2e runner, and add scripts so the e2e is discoverable without reading
the config.

**Contract**: `@playwright/test` at `1.63.0` in `devDependencies` (exact pin — `test-plan.md`
§4:162 reserves the pin for this phase). New scripts: `"e2e": "playwright test"` and
`"e2e:ui": "playwright test --ui"`. `package-lock.json` updated by `npm install`.

#### 2. Playwright config

**File**: `src/frontend/playwright.config.ts` (new)

**Intent**: Define the harness — where specs live, which browser, and how both servers start
— so `npx playwright test` is the only command needed locally and in CI.

**Contract**: `testDir: './e2e'`, `chromium` project only, `forbidOnly: !!process.env.CI`,
`retries: process.env.CI ? 2 : 0`, `reporter` of `list` locally and `html` in CI,
`use.baseURL` matching the Next server port.

`webServer` is an **array of two** entries (Playwright starts both and waits for each `url`):

| # | `command` | `cwd` | `url` (readiness) |
|---|---|---|---|
| 1 | `dotnet run --project VeloRoute` | `../backend` | `http://localhost:5098/health` |
| 2 | `npm run build && npm start` | — | the `baseURL` |

Both carry `reuseExistingServer: !process.env.CI` and a `timeout` generous enough for a cold
`dotnet run` plus a `next build` (≥ 180 s).

The Next entry's `env` sets, with a comment stating these are deliberately synthetic and
never reach Clerk:

```ts
env: {
  // Structurally valid, intentionally fake. clerk.example.com does not exist and is
  // blocked in the specs; anonymous requests carry no session, so neither key is ever
  // validated against Clerk. Explicit env here also wins over a developer's .env.local.
  NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY: 'pk_test_Y2xlcmsuZXhhbXBsZS5jb20k',
  CLERK_SECRET_KEY: 'sk_test_000000000000000000000000000000000000000000',
  VELO_API_URL: 'http://localhost:5098',
}
```

Expect `next start` to print the `output: standalone` warning — this is known and accepted;
do not "fix" it by switching to the standalone server without re-reading Implementation
Approach.

#### 3. Vitest exclusion

**File**: `src/frontend/vitest.config.ts`

**Intent**: Stop Vitest collecting Playwright specs. Must land in the same commit as the
first spec.

**Contract**: Add `exclude` under `test`, preserving Vitest's defaults rather than replacing
them — the default array is not merged, so spell out `'**/node_modules/**'`, `'**/.git/**'`,
and add `'**/e2e/**'`.

#### 4. Smoke spec

**File**: `src/frontend/e2e/smoke.spec.ts` (new, temporary)

**Intent**: Prove the harness — both servers boot, the page serves, and Vitest ignores the
file — before any real assertions depend on it. Deleted in Phase 3.

**Contract**: One test navigating to `/` and asserting the `VeloRoute` heading is visible.

#### 5. Ignore Playwright artifacts

**File**: `.gitignore`

**Intent**: Keep run output out of the repo.

**Contract**: Add `src/frontend/test-results/`, `src/frontend/playwright-report/`, and
`src/frontend/blob-report/`.

### Success Criteria:

#### Automated Verification:

- Browser installs: `npx playwright install --with-deps chromium` from `src/frontend/`
- Smoke spec passes: `npx playwright test` from `src/frontend/`
- Vitest still green and does not collect the spec: `npm test` from `src/frontend/` reports
  the same 47 cases as before
- Lint passes: `npm run lint` from `src/frontend/`

#### Manual Verification:

- With no backend running, `npx playwright test` starts one itself and the run still passes
- With a backend already running, `reuseExistingServer` picks it up rather than failing on a
  port clash
- `git status` shows no `test-results/` or `playwright-report/` noise

---

## Phase 3: The Anonymous e2e Spec

### Overview

The spec that defends risk #7: signed-out, clerk-js blocked, mocked ORS-dependent hops, real
GPX hop, asserting the download.

### Changes Required:

#### 1. Route fixtures

**File**: `src/frontend/e2e/fixtures/route.ts` (new)

**Intent**: Hold the two mocked response bodies and the stub map style in one place, typed
against the app's own interfaces so a shape change fails at compile time rather than
silently drifting.

**Contract**: Exports a `GeocodingFeature`-shaped geocode response
(`{ features: [{ geometry: { coordinates: [lon, lat] }, properties: { label } }] }`) and a
full `RouteResult` (`geometry.coordinates`, `distanceMeters`, `segments`, `pavedRatio`,
`smoothnessScore`, `overlapRatio`, `qualityWarning`, `maxConsecutiveSharpTurns`) imported
from `@/types/route`.

Two constraints on the coordinate list: **≥ 2 points**, or `RouteMap` renders no `Source`
(`RouteMap.tsx:47`); and a `distanceMeters` inside the form's default 30–60 km bounds so the
readout is coherent. Also exports the minimal MapLibre style
`{ version: 8, sources: {}, layers: [] }`.

#### 2. The spec

**File**: `src/frontend/e2e/anonymous-flow.spec.ts` (new)

**Intent**: Prove a signed-out visitor with a broken Clerk completes search → generate → map
→ GPX download. This is the phase's deliverable.

**Contract**: One `test` in a `describe`, ordered as:

*Routing setup, before `goto`:*

- `page.route('https://clerk.example.com/**', r => r.abort())` — the load-bearing block.
- `page.route('**/api/geocode**', …)` → fulfil with the geocode fixture.
- `page.route('**/api/routes/loop', …)` → fulfil with the `RouteResult` fixture.
- `page.route('https://tiles.openfreemap.org/**', …)` → fulfil the style URL with the stub
  style JSON; abort everything else on that host.
- `/api/routes/gpx` is deliberately **not** routed — it must reach the real backend.

*Steps:*

1. `goto('/')`.
2. Fill the combobox — `getByRole('combobox')` (**not** `getByLabel('Start location')`; the
   label has no `htmlFor`, `RouteForm.tsx:49-50`) with ≥ 2 characters, then click the
   `getByRole('option')` entry. The 300 ms debounce (`SearchBar.tsx:41`) is absorbed by
   Playwright's auto-waiting on the option.
3. Leave Min/Max km at their 30/60 defaults; assert
   `getByRole('button', { name: 'Generate' })` is enabled — this is the assertion that the
   start point registered (`RouteForm.tsx:33`).
4. Click Generate.
5. Assert the panel: `Total distance` and the `km` readout, plus `Surface quality`
   (`RouteInfoPanel.tsx:100-105`). This is the "route rendered" proxy — the line itself is
   canvas, excluded by `test-plan.md` §7.
6. Assert the map mounted: the `.maplibregl-map` container is visible.
7. **Assert the Save block is absent** — `getByLabel('Name')` has zero count. This is what
   makes it an *anonymous* run and not merely an unauthenticated one, and it is the
   assertion that would catch Clerk misreporting a session.
8. Trigger the download inside `page.waitForEvent('download')`, then assert
   `download.suggestedFilename()` matches `/^veloroute-\d{8}T\d{6}\.gpx$/`
   (`RouteInfoPanel.tsx:8-11,83`) and that the body — read via `download.createReadStream()`
   — contains `<trkpt`, proving the real backend served it.

Do **not** register a `pageerror`/`console` failure hook — see Critical Implementation
Details.

#### 3. Remove the smoke spec

**File**: `src/frontend/e2e/smoke.spec.ts` (delete)

**Intent**: It existed to prove the harness; the real spec now covers `/` loading.

**Contract**: File removed.

### Success Criteria:

#### Automated Verification:

- Spec passes: `npx playwright test` from `src/frontend/`
- Passes repeatedly without flake: `npx playwright test --repeat-each=3`
- Vitest unaffected: `npm test` from `src/frontend/`
- Lint passes: `npm run lint` from `src/frontend/`

#### Manual Verification:

- The clerk-js block is genuinely load-bearing: remove the `abort` route, confirm the spec
  still passes (proving it is not accidentally dependent on Clerk being broken), then
  restore it
- The GPX hop is genuinely real: stop the backend, confirm the spec fails at the download
  step rather than passing against a mock
- `npx playwright show-report` renders a trace for a deliberately failed run

---

## Phase 4: CI Gate + Documentation Sync

### Overview

Run the e2e in CI on both frontend and backend changes, block the SWA deploy on it, and
bring every doc the change touched back into truth.

### Changes Required:

#### 1. e2e job and widened triggers

**File**: `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`

**Intent**: Make the e2e a release gate, and make a backend change able to trigger it — the
real-GPX-hop decision means a backend regression can now break the anonymous flow without
any frontend diff.

**Contract**: Add `src/backend/**` to both the `push` and `pull_request` path filters. Add
an `e2e` job mirroring the existing `test` job's setup (`actions/checkout@v4`,
`actions/setup-node@v4` with `node-version: 'lts/*'` and the npm cache on
`src/frontend/package-lock.json`, `npm ci` in `src/frontend`) plus:

- `actions/setup-dotnet@v4` with `dotnet-version: '10.x'` — matching `backend.yml:23-25`
- `npx playwright install --with-deps chromium`
- `npx playwright test`, `working-directory: src/frontend`
- `actions/upload-artifact@v4` for `src/frontend/playwright-report/`, with `if: failure()`

Carry the same `if:` guard as `test` so it is skipped on `closed` PR events. Then set
`build_and_deploy_job` to `needs: [test, e2e]`. `close_pull_request_job` keeps **no**
dependency, for the reason recorded in `test-plan.md:116-123`.

Known consequence to accept, not fix: a backend-only PR now also runs
`build_and_deploy_job`, redeploying identical frontend content. Narrowing that needs
job-level changed-file detection, which is more machinery than the duplicate upload costs.

#### 2. Test plan

**File**: `context/foundation/test-plan.md`

**Intent**: The plan is the contract for how tests get written here; leaving it describing a
pre-Playwright world is exactly the staleness the repo's workflow conventions forbid.

**Contract**: Four edits.

- §3 phase 5 row: Status `not started` → `shipped`, change folder
  `context/changes/anonymous-flow-e2e`.
- §3 "Phase 5 scope note": correct "three untested validation branches" to **two** — the
  source folds non-finite and out-of-range into one predicate with one message
  (`Program.cs:412-416`), per §1 principle #3.
- §4 stack table: replace the Playwright "candidate, not installed" row with `1.63.0`,
  noting `chromium` only and the two synthetic Clerk keys requiring no CI secret.
- §6.5 "Adding an e2e test": replace `TBD` with the cookbook — the mock-geocode-and-loop /
  real-GPX split, the `clerk.example.com` block and why it is the load-bearing condition,
  the `getByRole('combobox')` selector caveat, the download-event assertion, and the
  anti-pattern: *asserting a download occurred without asserting the filename or that the
  Save block is absent — a signed-in session would pass such a test while proving nothing
  about the anonymous path.*

Also add the Playwright pin to §8's "Stack versions last verified" line with today's date.

#### 3. Agent and contributor docs

**Files**: `src/frontend/AGENTS.md`, `.github/copilot-instructions.md`, `AGENTS.md`,
`src/frontend/README.md`, root `README.md`

**Intent**: Keep the standing instruction set accurate — a new test layer and a new CI gate
both change what a contributor or agent must know.

**Contract**:

- `src/frontend/AGENTS.md` — add an e2e section to Testing: `npm run e2e`, specs in `e2e/`
  as `*.spec.ts`, Vitest excludes that directory, `npx playwright install chromium` is a
  one-time prerequisite, and the harness starts the .NET backend itself.
- `.github/copilot-instructions.md` — add Playwright to the dev-commands block; update the
  frontend test-runner sentence to name both runners.
- Root `AGENTS.md` — the CI gate description (documented as a single-job gate) is now stale;
  state that `build_and_deploy_job` needs `test` **and** `e2e`, and that the SWA workflow
  now also triggers on `src/backend/**`.
- READMEs — add the e2e command wherever `npm test` is currently documented.

#### 4. The dead Next docs instruction

**Files**: `.github/copilot-instructions.md:8`, `src/frontend/AGENTS.md:3`

**Intent**: Both files instruct agents to read `node_modules/next/dist/docs/` before writing
Next.js code. Verified: that directory does not exist and `next` ships no markdown there, so
the instruction is unfollowable — every agent either ignores it or wastes a tool call.

**Contract**: Keep the *warning* (Next 15 / React 19 differ from older training data) and
replace the dead path with a followable instruction: consult the installed package's own
types and source under `node_modules/next/`, or the official docs. Strictly a correction of
a verified-false statement, in the spirit of the repo's "never let a commit leave these docs
describing a state that no longer exists" convention.

#### 5. Change status

**File**: `context/changes/anonymous-flow-e2e/change.md`

**Contract**: `status: done`, `updated:` to the completion date.

### Success Criteria:

#### Automated Verification:

- Workflow is valid YAML and the job graph parses: `gh workflow view "Azure Static Web Apps CI/CD"`
- A pushed branch shows `test` and `e2e` running, and `build_and_deploy_job` queued behind
  both: `gh run list --branch anonymous-flow-e2e`
- The e2e job passes in CI on a clean runner (no `.env.local`, no Docker, no secrets)
- Full local suites still green: `dotnet test` from `src/backend/`; `npm test` and
  `npx playwright test` from `src/frontend/`

#### Manual Verification:

- A backend-only commit triggers the SWA workflow and runs the e2e
- Deliberately break the anonymous path (e.g. throw in `RouteInfoPanel`), push, and confirm
  `build_and_deploy_job` is blocked rather than deploying
- No doc in the change's blast radius still describes Playwright as absent, the CI gate as
  single-job, or `node_modules/next/dist/docs/` as readable

---

## Testing Strategy

### Unit Tests

None added. `GpxSerializer`'s behaviour is already covered by `GpxSerializerTests`
(5 cases, including `pl-PL`/`de-DE` culture flips); re-testing it at the endpoint layer
would duplicate coverage at higher cost.

### Integration Tests

`GpxEndpointTests` — success shape, both validation branches (6 `[InlineData]` rows across
the second), and anonymous accessibility. `WebApplicationFactory`-hosted, no Postgres.

### End-to-End Tests

One spec: the anonymous flow with clerk-js blocked. Geocode and loop mocked at the browser
boundary; GPX real against the .NET backend; map style stubbed.

### Manual Testing Steps

1. `docker compose up -d`, then `dotnet test` from `src/backend/` — full suite green.
2. From `src/frontend/`: `npm test` (47 cases, no spec files collected), then
   `npx playwright test` with nothing else running — Playwright boots both servers.
3. `npx playwright test --headed` and watch the flow: suggestions appear, Generate enables,
   panel shows distance, **no Name/Tags/Save fields**, the browser saves a
   `veloroute-*.gpx`.
4. Open the downloaded file — it must contain `<trk>/<trkseg>/<trkpt>` with `.` decimal
   separators.
5. Stop the backend and re-run — the spec must fail at the download step (proving the hop is
   real).

## Performance Considerations

The e2e job adds roughly 2–4 minutes to frontend CI: Chromium install (~1–2 min, cacheable
via the setup-node npm cache plus a dedicated `~/.cache/ms-playwright` cache if it proves
slow), a cold `dotnet run`, and a `next build`. Accepted deliberately — risk #7 is the
highest-rated risk on the map, and a non-blocking test does not defend it.

Inside the spec, all remote hosts except the backend are intercepted, so runtime is bounded
by local server startup rather than third-party latency. `retries: 2` in CI absorbs
cold-start jitter without masking a real regression, since a genuine break fails all three.

## Migration Notes

Not applicable — no schema, data, or API contract changes. The only externally visible
change is CI topology: the SWA workflow now triggers on `src/backend/**` and requires two
jobs before deploying.

## References

- Research: `context/changes/anonymous-flow-e2e/research.md`
- Change identity: `context/changes/anonymous-flow-e2e/change.md`
- Test plan: `context/foundation/test-plan.md` §2 risk #7, §3 phase 5, §6.5
- Endpoint under test: `src/backend/VeloRoute/Program.cs:407-420`
- Anonymous-access test pattern: `src/backend/VeloRoute.Tests/Routing/AuthMiddlewareTests.cs:30-42`
- DB-free factory pattern: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:133-145`
- Download mechanism: `src/frontend/src/components/RouteInfoPanel.tsx:68-96`
- Clerk gate: `src/frontend/src/components/RouteInfoPanel.tsx:112`
- Selectors: `src/frontend/src/components/SearchBar.tsx:79-113`, `RouteForm.tsx:33,49-77,81-87`
- Map style + load gate: `src/frontend/src/components/RouteMap.tsx:18-35,71`
- Clerk hook behaviour when unloaded: `node_modules/@clerk/shared/dist/react/index.mjs:2034-2042`
- Clerk load-failure catch: `node_modules/@clerk/react/dist/ClerkProvider-COmoIHBK.mjs:1357-1362`
- GPX decision history: `context/archive/2026-06-04-gpx-export/plan.md:69-71`
- Prior deferral of Playwright: `context/archive/2026-09-08-test-plan-refresh-2026-09-08/plan.md:45`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: `POST /routes/gpx` Integration Test

#### Automated

- [x] 1.1 Backend builds: `dotnet build` from `src/backend/`
- [x] 1.2 New tests pass: `dotnet test --filter FullyQualifiedName~GpxEndpointTests`
- [x] 1.3 Full backend suite green: `dotnet test` from `src/backend/`

#### Manual

- [x] 1.4 Each `[InlineData]` row fails for the intended reason

### Phase 2: Playwright Harness

#### Automated

- [ ] 2.1 Browser installs: `npx playwright install --with-deps chromium`
- [ ] 2.2 Smoke spec passes: `npx playwright test`
- [ ] 2.3 Vitest green and does not collect the spec: `npm test` reports 47 cases
- [ ] 2.4 Lint passes: `npm run lint`

#### Manual

- [ ] 2.5 With no backend running, Playwright starts one and the run passes
- [ ] 2.6 With a backend already running, `reuseExistingServer` avoids a port clash
- [ ] 2.7 `git status` shows no Playwright artifact noise

### Phase 3: The Anonymous e2e Spec

#### Automated

- [ ] 3.1 Spec passes: `npx playwright test`
- [ ] 3.2 No flake: `npx playwright test --repeat-each=3`
- [ ] 3.3 Vitest unaffected: `npm test`
- [ ] 3.4 Lint passes: `npm run lint`

#### Manual

- [ ] 3.5 Spec still passes with the clerk-js block removed, then block restored
- [ ] 3.6 With the backend stopped, the spec fails at the download step
- [ ] 3.7 `npx playwright show-report` renders a trace for a failed run

### Phase 4: CI Gate + Documentation Sync

#### Automated

- [ ] 4.1 Workflow parses: `gh workflow view "Azure Static Web Apps CI/CD"`
- [ ] 4.2 `test` and `e2e` run with `build_and_deploy_job` queued behind both
- [ ] 4.3 e2e job passes on a clean CI runner (no `.env.local`, no Docker, no secrets)
- [ ] 4.4 Full local suites green: `dotnet test`; `npm test` and `npx playwright test`

#### Manual

- [ ] 4.5 A backend-only commit triggers the SWA workflow and runs the e2e
- [ ] 4.6 A deliberately broken anonymous path blocks `build_and_deploy_job`
- [ ] 4.7 No doc still describes Playwright as absent, the gate as single-job, or the dead Next docs path
