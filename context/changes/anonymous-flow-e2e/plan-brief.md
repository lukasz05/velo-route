# Core Anonymous Flow End-to-End — Plan Brief

> Full plan: `context/changes/anonymous-flow-e2e/plan.md`
> Research: `context/changes/anonymous-flow-e2e/research.md`

## What & Why

Test-plan phase 5 defends risk #7 (High × High, the highest-rated risk on the map):
*v2 auth/library work regresses the anonymous generate → map → GPX flow; a stranger hits a
broken core product.* `ClerkProvider` wraps the entire app, and `RouteInfoPanel` — the
component that owns the "Download GPX" button — reads Clerk hooks, so an auth-shaped failure
can take down a feature that needs no account. We add a Playwright e2e that proves the
signed-out flow works **with clerk-js deliberately blocked**, plus the missing
`POST /routes/gpx` integration test.

## Starting Point

`POST /routes/gpx` has zero tests despite being the last hop of the anonymous flow and being
called from three frontend files. Playwright is absent repo-wide. Vitest's default glob
would collect any `*.spec.ts` and fail it. CI runs `npm test` before the SWA deploy, filtered
to `src/frontend/**`, with no Clerk or ORS secret wired in anywhere.

## Desired End State

`npx playwright test` from `src/frontend/` boots the .NET backend and the Next server, drives
a signed-out browser through search → generate → map → GPX download with Clerk broken, and
asserts a `veloroute-*.gpx` file actually lands. `dotnet test` covers the GPX endpoint's HTTP
contract. Both frontend and backend changes trigger the e2e, and the SWA deploy is blocked
until it passes.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Which hops to mock | Geocode + loop mocked; **GPX real** | Mocking the two ORS-dependent hops removes the secret dependency, while the real GPX hop costs nothing and catches fixture-vs-backend drift. | Plan |
| clerk-js handling | Block `clerk.example.com` in every spec | Reproduces risk #7's actual mechanism deterministically; leaving it to DNS is a flake source. | Plan |
| CI gating | Gate the SWA deploy | A test that doesn't block the deploy doesn't defend the highest-rated risk. | Plan |
| Backend startup | Playwright `webServer` array | One command works identically locally and in CI; Playwright owns readiness and teardown. | Plan |
| Workflow triggers | Widen SWA filter to `src/backend/**` | The real-GPX-hop choice means a backend change can break the anonymous flow with no frontend diff. | Plan |
| Spec breadth | One happy path + download assertion | Smallest spec that states risk #7's proof condition; error paths stay at Vitest level per test-plan §1. | Plan |
| Clerk keys | Synthetic, inline in `playwright.config.ts` | Clone-and-run, no CI secret, impossible to hit real credentials. | Plan |
| Next server | `next start`, not the standalone server | Avoids `cp -r` steps whose flags differ between pwsh and bash; `webServer.env` beats `.env.local`. | Plan |
| Non-finite test inputs | `1e400` and quoted `"NaN"` | A bare `NaN` literal fails at model binding and never reaches the guard. | Plan |
| GPX branch count | **Two**, not three | Source folds non-finite and out-of-range into one predicate with one message. | Research |

## Scope

**In scope:** `GpxEndpointTests.cs`; Playwright 1.63.0 pinned + `playwright.config.ts`;
Vitest `exclude`; one anonymous e2e spec; e2e CI job gating the deploy; doc sync across
test-plan, AGENTS.md, copilot-instructions, and READMEs.

**Out of scope:** the `/r/[token]` and `/my-routes/[id]` GPX call sites; extracting the
triplicated `handleDownload`; e2e error-path specs; map canvas assertions; re-testing GPX
serialisation; any CI secret; risk #8 (phase 6).

## Architecture / Approach

```
Playwright (chromium)
  ├── webServer[0]  dotnet run  → :5098   (readiness: /health)
  └── webServer[1]  next start  → :3000   (synthetic Clerk env inline)

Browser, per spec:
  clerk.example.com/**        → abort      ← the load-bearing condition
  /api/geocode**              → fixture
  /api/routes/loop            → fixture (RouteResult)
  tiles.openfreemap.org/**    → stub style {version:8,sources:{},layers:[]}
  /api/routes/gpx             → NOT routed → real Next proxy → real .NET backend
```

The panel's text readouts are the "route rendered" proxy (the line is canvas, excluded by
test-plan §7). Asserting the **Save block is absent** is what makes it an anonymous run
rather than merely an unauthenticated one.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. GPX integration test | `GpxEndpointTests.cs` — success shape, both branches, anonymous access | Low; a `NaN` literal row would test System.Text.Json, not the endpoint |
| 2. Playwright harness | Pinned dep, config with dual `webServer`, Vitest `exclude`, smoke spec | Vitest `exclude` must land in the same commit as the first spec, or `npm test` goes red |
| 3. Anonymous e2e spec | The spec that defends risk #7 | Debounce/auto-wait timing; a `networkidle` wait would pay Clerk's full 15 s timeout |
| 4. CI gate + doc sync | e2e job, widened filters, `needs: [test, e2e]`, docs | Backend-only PRs now also redeploy identical frontend content |

**Prerequisites:** Docker running for the full `dotnet test` sweep (`DOCKER_API_VERSION=1.41`
if Testcontainers fails); `npx playwright install --with-deps chromium` once locally. No
Clerk account, no ORS key, no CI secret.

**Estimated effort:** ~2 sessions across 4 phases; phase 1 is independently committable.

## Open Risks & Assumptions

- **Assumption, verified at source not in a browser:** the app renders usably with clerk-js
  blocked — `useUser()` returns `isLoaded: false` and `ClerkProvider` catches the load
  failure rather than throwing. Phase 3 confirms it empirically on first run.
- `next start` prints a warning under `output: 'standalone'`. It serves correctly today
  (verified HTTP 200); if a future Next release promotes that to an error, switch to the
  standalone server plus asset-copy steps.
- Widening the SWA path filter means backend-only PRs redeploy unchanged frontend content.
  Accepted — narrowing it needs job-level changed-file detection.
- `retries: 2` in CI absorbs cold-start jitter. If it ever masks a real flake, the spec's
  determinism assumptions need revisiting rather than the retry count raising.

## Success Criteria (Summary)

- A signed-out visitor's generate → map → GPX flow is proven in a real browser, with Clerk
  broken, on every frontend **and** backend change.
- `POST /routes/gpx` can no longer regress its status codes, error codes, content type, or
  anonymous accessibility without CI going red.
- A broken anonymous path blocks the production deploy instead of reaching users.
