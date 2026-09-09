---
date: 2026-09-09T00:59:49+02:00
researcher: Łukasz Orawiec
git_commit: 3637b76a359b7863e9f518702d9f61964ebbc162
branch: main
repository: velo-route
topic: "Core anonymous flow end-to-end (test-plan phase 5, risk #7): browser e2e for generate → map → GPX with no session, plus the missing POST /routes/gpx integration test"
tags: [research, codebase, testing, playwright, e2e, gpx, clerk, anonymous-flow, ci]
status: complete
last_updated: 2026-09-09
last_updated_by: Łukasz Orawiec
---

# Research: Core anonymous flow end-to-end

**Date**: 2026-09-09T00:59:49+02:00
**Researcher**: Łukasz Orawiec
**Git Commit**: `3637b76a359b7863e9f518702d9f61964ebbc162` (pushed; on `origin/main`)
**Branch**: `main`
**Repository**: velo-route (`https://github.com/lukasz05/velo-route`)

Permalink base for every `file:line` below:
`https://github.com/lukasz05/velo-route/blob/3637b76a359b7863e9f518702d9f61964ebbc162/<file>#L<line>`

## Research Question

Ground `context/foundation/test-plan.md` §3 phase 5 — "Core anonymous flow end-to-end" — which defends risk #7 (High × High): *"v2 auth/library work regresses the anonymous generate → map → GPX flow; a stranger hits a broken core product."* Two halves:

1. **e2e**: prove a signed-out visitor completes start-point search → route generation → map render → GPX download in a real browser, with no Clerk session at any step.
2. **integration**: add the missing test for `POST /routes/gpx`, which has none today.

Plus the two prerequisites the test-plan defers into this change: the Vitest/Playwright spec-glob collision, and how an e2e run obtains `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY`.

## Summary

The phase is well-scoped and nothing blocks it, but research changes four things the plan should not inherit uncritically from the test-plan:

1. **`POST /routes/gpx` has two validation branches, not three.** The test-plan (`context/foundation/test-plan.md:128-129`) says "three untested validation branches (empty coordinates, non-finite values, out-of-range values)". The source folds non-finite and out-of-range into a *single* predicate with a *single* error message (`src/backend/VeloRoute/Program.cs:412-416`). Per test-plan §1 principle #3 ("if the plan and research disagree, research is ground truth"), the integration test should cover two branches × several inputs, not three branches.

2. **The Clerk key needs no CI secret.** A structurally valid development publishable key can be constructed offline — `pk_test_Y2xlcmsuZXhhbXBsZS5jb20k` (base64 of `clerk.example.com$`) passes Clerk's validator, verified against the installed SDK. This removes the "how do tests get a Clerk key" blocker entirely, which the archive confirms was never solved before (no `CLERK_*` or `ORS_*` secret exists in either CI workflow).

3. **The sharpest version of risk #7 is testable deterministically, and it is not "does the download work".** `handleDownload` never touches Clerk (`src/frontend/src/components/RouteInfoPanel.tsx:68-96`), so the download is structurally independent of auth. The real auth-shaped failure is that `RouteInfoPanel` cannot *render* if Clerk hooks fail, because the same component reads `useUser()`/`useAuth()` at `RouteInfoPanel.tsx:30-31` and `ClerkProvider` + `clerkMiddleware()` sit on every anonymous request. An e2e that lets clerk-js load normally tests the happy path; an e2e that **blocks the clerk-js network fetch** reproduces the failure mode the risk is actually about, deterministically and with no session. That distinction is the design decision this phase turns on.

4. **The start point can only be set through `/api/geocode`.** `RouteMap` exposes no click handler — `startPoint` arrives only as a prop from `RouteApp` (`src/frontend/src/components/RouteMap.tsx:10,14,63`), fed by `SearchBar`'s autocomplete. So the e2e must either mock `/api/geocode` or supply a live `ORS_API_KEY`. There is no third path.

Confirmed unchanged from the test-plan: Playwright is absent everywhere in the repo, and the Vitest spec-glob collision is real.

## Detailed Findings

### The anonymous path — component tree and Clerk contact points

`/` is a Server Component rendering two client trees (`src/frontend/src/app/page.tsx:5-14`):

```
app/layout.tsx (Server) — ClerkProvider wraps the entire <html>   layout.tsx:22,32
 ├─ Header.tsx (client)                                           Header.tsx:7
 └─ app/page.tsx (Server)
     ├─ AccountDeletedBanner.tsx (client, in <Suspense>)           AccountDeletedBanner.tsx:6
     └─ RouteApp.tsx (client)                                      RouteApp.tsx:21
         ├─ RouteForm.tsx → SearchBar.tsx                          RouteForm.tsx:22 / SearchBar.tsx:11
         ├─ ErrorMessage.tsx                                       ErrorMessage.tsx:10
         ├─ RouteInfoPanel.tsx  (only once routeResult is set)     RouteInfoPanel.tsx:22
         └─ RouteMap.tsx  (next/dynamic, ssr:false)                RouteApp.tsx:11
```

Clerk contact points reachable by an anonymous visitor of `/`:

| Where | Reference | Note |
|---|---|---|
| `ClerkProvider` wrapping all of `<html>` | `src/frontend/src/app/layout.tsx:2,22,32` | Only provider in the app — no theme/query providers |
| `clerkMiddleware()` on every non-asset route | `src/frontend/src/middleware.ts:1,3,5-7` | Matcher `['/((?!_next|.*\\..*).*)']` — includes `/` |
| `Header` — `useAuth`, `useClerk`, `useUser` | `src/frontend/src/components/Header.tsx:5,8-10` | Renders only the "VeloRoute" link while `!isLoaded` (`Header.tsx:27-33`); "Sign in" button at `Header.tsx:53-55` |
| `RouteInfoPanel` — `useUser`, `useAuth` | `src/frontend/src/components/RouteInfoPanel.tsx:4,30,31` | **The only anonymous-path component reading Clerk** |

`RouteApp`, `RouteForm`, `SearchBar`, `RouteMap`, `ErrorMessage`, `AccountDeletedBanner` contain no Clerk imports. No `<SignedIn>`/`<SignedOut>` components are used anywhere — gating is imperative via `useUser().isSignedIn`.

Inside `RouteInfoPanel`, the Clerk-dependent UI (name/tags/Save) is gated at `RouteInfoPanel.tsx:112` by `{isSignedIn && (…)}`, and `getToken` is only called inside `handleSave` (`RouteInfoPanel.tsx:45`), which is unreachable while signed out. So for an anonymous visitor the panel renders exactly: "Total distance", "Surface quality", an optional `role="status"` quality notice, and the "Download GPX" button.

### Why "blocked clerk-js" is the load-bearing e2e case

Risk #7's stated mechanism is that `ClerkProvider` wraps the whole app so an auth-shaped failure can reach a page with no account features. Tracing it concretely:

- `handleDownload` (`RouteInfoPanel.tsx:68-96`) calls no Clerk API and sends no `Authorization` header. The download is auth-free by construction.
- But it lives in a component whose module-level render calls `useUser()` and `useAuth()`. If those throw or the provider fails to initialise, the panel does not render, the "Download GPX" button never exists, and the anonymous flow is broken by an auth failure — exactly risk #7.
- With a *working* Clerk key and network, this failure mode is never exercised. Clerk resolves, hooks return, the test passes and proves less than it appears to.
- Blocking the clerk-js script fetch (a `page.route(…).abort()` on the frontend-API host) makes the degraded state deterministic and reproducible without any session, any secret, or any Clerk account.

Whether the app *survives* that block is genuinely unknown and is the first open question below — it is a real behavioural question about the product, not a test-harness detail, which is what makes it worth an e2e.

### Selectors for the e2e (verified against source)

| Element | Selector that works | Source |
|---|---|---|
| Start-location input | `getByRole('combobox')` or `getByPlaceholder('Search for a start location…')` | `SearchBar.tsx:79-92` |
| — | **`getByLabel('Start location')` will NOT work** — the label has no `htmlFor` and the input no `id` | `RouteForm.tsx:49-50` |
| Suggestion list / items | `getByRole('listbox')`, `getByRole('option')` | `SearchBar.tsx:93-113`; renders only when `suggestions.length > 0`, min 2 chars (`SearchBar.tsx:22`), 300 ms debounce (`SearchBar.tsx:41`) |
| Min km | `getByLabel('Min km')` — proper `htmlFor`/`id` | `RouteForm.tsx:55-64`; default `30` (`RouteForm.tsx:24`) |
| Max km | `getByLabel('Max km')` | `RouteForm.tsx:66-77`; default `60` (`RouteForm.tsx:25`) |
| Generate | `getByRole('button', { name: 'Generate' })` | `RouteForm.tsx:81-87`; disabled until `startPoint !== null && minKm >= 5 && maxKm <= 300 && minKm < maxKm` (`RouteForm.tsx:33`); text flips to `'Generating…'` while loading (`RouteForm.tsx:86`) |
| Download GPX | `getByRole('button', { name: 'Download GPX' })` | `RouteInfoPanel.tsx:149-155`; text flips to `'Downloading…'` |
| Distance readout | text `"Total distance"` + `"{km} km"` | `RouteInfoPanel.tsx:100-101` |
| Surface readout | text `"Surface quality"` + `"Unknown"` / `"{n}% paved"` | `RouteInfoPanel.tsx:102-105` |
| Quality notice | `getByRole('status')` | `RouteInfoPanel.tsx:106-111` |
| Route-generation error | `getByRole('alert')` | `ErrorMessage.tsx:13-16`; renders `null` when no error (`ErrorMessage.tsx:11`) |
| GPX error | text `'GPX export failed. Please try again.'` — plain `<p>`, **no role** | `RouteInfoPanel.tsx:156-158` |

Error strings available as assertions (`ErrorMessage.tsx:3-8`): `NO_ROUTE`, `RATE_LIMITED`, `TIMEOUT`, `NO_VALID_RESULT`, fallback `'Something went wrong — please try again.'` (`ErrorMessage.tsx:12`).

**Map assertions.** The route line is a MapLibre `Source`/`Layer` (`RouteMap.tsx:74-79`) drawn to canvas — not DOM-queryable, and test-plan §7 excludes canvas-pixel assertions. Two DOM-queryable signals do exist: the `maplibregl-marker` div rendered by `<Marker>` (`RouteMap.tsx:82-84`), and the map root `.maplibregl-map` / `canvas[aria-label="Map"]` that maplibre-gl creates. `fitBounds` only fires after `onLoad` sets `isMapLoaded` (`RouteMap.tsx:23,33-34,66-72`, with an explicit warning comment at `RouteMap.tsx:19-22` that pre-load camera moves are unreliable). The panel's text readouts are the reliable "route rendered" proxy.

### The three network hops, and what each can be mocked against

All three are browser-initiated, so all three are interceptable by Playwright `page.route`:

| Hop | Browser request | Server side | External dependency |
|---|---|---|---|
| 1. Geocode | `GET /api/geocode?...` from `SearchBar` | `src/frontend/src/app/api/geocode/route.ts:1` | **ORS geocode API directly**, using server-side `process.env.ORS_API_KEY` (`route.ts:9,15-19`) — does *not* go through the .NET backend |
| 2. Generate | `POST /api/routes/loop` | `src/frontend/src/app/api/routes/loop/route.ts:4` → `lib/routingApi.ts:16` → `${VELO_API_URL}/routes/loop` | .NET backend → ORS directions |
| 3. GPX | `POST /api/routes/gpx` | `src/frontend/src/app/api/routes/gpx/route.ts:3` → `${VELO_API_URL}/routes/gpx` | **None** — pure serialisation |

None of the three sends an `Authorization` header; all three proxy handlers skip `requireAuthHeader` (`src/frontend/src/lib/apiProxy.ts:1-7`). Backend base URL is `process.env.VELO_API_URL ?? 'http://localhost:5098'` (`apiProxy.ts:15`, `routingApi.ts:17`), matching `launchSettings.json:8`.

Note `lib/routingApi.ts:7` **throws** `'VELO_API_URL is not set'` — but only in `getRoutePreview` (the `/routes/preview` path used by `/dev`), not in the loop path, which uses the `??` fallback at line 17.

The GPX hop is the only one with no external dependency, and it is the one already covered by the other half of this phase (the xUnit integration test). That gives a clean cost×signal split: mock all three in the browser and let the .NET test own the GPX contract, so the e2e proves *wiring and browser behaviour* (button → fetch → blob → anchor → download event) while the integration test proves *format*.

### The GPX download mechanism (what an e2e must actually catch)

`RouteInfoPanel.tsx:68-96` — POSTs `{ coordinates: route.geometry.coordinates }`, reads `res.text()`, wraps in `new Blob([...], { type: 'application/gpx+xml' })`, creates an object URL, appends a synthetic `<a download="veloroute-{timestamp}.gpx">`, clicks it, then removes the node and revokes the URL in a `finally`. Filename format comes from `formatTimestamp` (`RouteInfoPanel.tsx:8-11`).

This is a blob-URL download, not a navigation — the Playwright assertion is `page.waitForEvent('download')` plus `download.suggestedFilename()` matching `/^veloroute-.*\.gpx$/`, and optionally reading the body to confirm it is the mocked GPX text.

The same handler is duplicated verbatim (not extracted into a shared helper) in two other files: `src/frontend/src/app/my-routes/[id]/page.tsx:74-103` and `src/frontend/src/app/r/[token]/page.tsx:44-73`. All three POST the identical body to `/api/routes/gpx` with no auth header. `/r/[token]` is itself fully anonymous with no Clerk check on the page.

### `POST /routes/gpx` — the integration half

`src/backend/VeloRoute/Program.cs:407-420`:

```csharp
app.MapPost("/routes/gpx", (GpxRequest req) =>
{
    if (req.Coordinates is null || req.Coordinates.Count == 0)
        return Results.BadRequest(new { error = "Coordinates must not be empty", code = "INVALID_INPUT" });

    if (req.Coordinates.Any(c =>
            !double.IsFinite(c.Latitude)  || !double.IsFinite(c.Longitude) ||
            c.Latitude  < -90  || c.Latitude  > 90 ||
            c.Longitude < -180 || c.Longitude > 180))
        return Results.BadRequest(new { error = "One or more coordinates are out of range", code = "INVALID_INPUT" });

    var gpx = GpxSerializer.Serialize(req.Coordinates);
    return Results.Text(gpx, "application/gpx+xml");
});
```

- **Two** branches, both `400` with `code = "INVALID_INPUT"`. Non-finite (`NaN`, `±Infinity`) and out-of-range share the second predicate *and its message* — a test cannot distinguish them by response, only by input.
- Success: `Results.Text(gpx, "application/gpx+xml")`. **No `Content-Disposition`, no filename** — deliberate, per `context/archive/2026-06-04-gpx-export/plan.md:69-71` ("Filename is set in the browser"). A test asserting a filename header would be asserting a decision the project explicitly rejected.
- No `.RequireAuthorization()` — consistent with `/routes/loop` and `/shares/{token}`, and in contrast to all eleven ownership-scoped endpoints. An `PostRoutesGpx_NoToken_IsNotUnauthorized` case mirrors the existing `AuthMiddlewareTests.cs:30-42` pattern and directly defends the PRD-v2 constraint.

Request shape — `GpxRequest(IReadOnlyList<RouteCoordinate> Coordinates)` (`Program.cs:429`) over `RouteCoordinate(double Longitude, double Latitude)` (`src/backend/VeloRoute/Routing/RouteResult.cs:17`). No `PropertyNamingPolicy` is configured anywhere; minimal APIs default to `JsonSerializerDefaults.Web` camelCase, confirmed by the body existing tests already send (`LoopRouteIntegrationTests.cs:8`). Test bodies must therefore use:

```json
{ "coordinates": [ { "longitude": 16.37, "latitude": 48.20 } ] }
```

`GpxSerializer` (`src/backend/VeloRoute/Routing/GpxSerializer.cs:5,7`) is `internal static`, already reachable from the test project (used by `GpxSerializerTests`), emits `<trk>/<trkseg>/<trkpt>` with namespace `http://www.topografix.com/GPX/1/1` (`GpxSerializer.cs:21`) and enforces `CultureInfo.InvariantCulture` explicitly (`GpxSerializer.cs:13-14`). Its unit-level behaviour is already covered by 5 cases in `GpxSerializerTests.cs` including `pl-PL`/`de-DE` culture flips — so the new endpoint test should **not** re-litigate serialisation; its job is the HTTP contract (status codes, error codes, content type, anonymous access).

**No Postgres needed.** `new VeloRouteWebApplicationFactory()` with no args leaves the `AppDbContext` registration untouched and never instantiates it (`TestInfrastructure.cs:133-145,168-176`); the DB is only reached by handlers that query it. `/routes/gpx` does not. Startup requires neither `ORS:ApiKey` nor a connection string — both default to `""` in `appsettings.json`, neither is validated, and the dev-only migration at `Program.cs:107-112` is guarded on a non-empty connection string. `appsettings.Development.json` does not exist. This matches the DB-free pattern in `LoopRouteIntegrationTests` / `SecurityPrivacyIntegrationTests` / `AuthMiddlewareTests`.

Naming convention to follow: class `Routing/GpxEndpointTests.cs`, methods `MethodUnderTest_Scenario_ExpectedOutcome` (e.g. `PostRoutesGpx_ValidCoordinates_Returns200WithGpxContentType`).

Current backend suite: 20 files, 87 `[Fact]` + 4 `[Theory]` across `src/backend/VeloRoute.Tests/`.

### Tooling: what adding Playwright touches

- **Playwright is absent repo-wide** — no `playwright.config.*`, no `cypress/`, no `e2e/` or `tests/` directory, no dependency in `src/frontend/package.json:22-46`. Confirmed by search, not by inference.
- **Version**: `@playwright/test` latest stable is **1.63.0**, published 2026-09-04 (`npm view @playwright/test`); `engines: { node: '>=20' }`. Local Node is v26.4.0, CI uses `node-version: 'lts/*'`. No pin exists yet — test-plan §4:162 explicitly reserves the pin for this phase.
- **Vitest collision is real, and verified at the source rather than from the doc**: `node_modules/vitest/dist/chunks/defaults.9aQKnqFk.js:5-6` gives `defaultInclude = ["**/*.{test,spec}.?(c|m)[jt]s?(x)"]` and `defaultExclude = ["**/node_modules/**", "**/.git/**"]`. `vitest.config.ts:7-11` sets neither. So *any* `*.spec.ts` anywhere under `src/frontend/` — including a top-level `e2e/` folder — gets collected by Vitest and fails there. The exclude must land in the same commit as the first spec.
- **Current frontend suite**: 47 cases across 8 co-located `*.test.ts(x)` files; no `*.spec.*` exists yet.
- **`next.config.ts:15` sets `output: 'standalone'`.** No incompatibility guard for `next start` was found in the installed CLI (`next-start.js`), so `next start` is the likely Playwright `webServer.command` — but this needs an empirical check during implementation rather than a claim here.
- **CI**: two workflows only. `azure-static-web-apps-purple-sky-08f4fb710.yml:19-33` runs the `test` job (`npm ci`, `npm test`, `working-directory: src/frontend`, `node-version: 'lts/*'`, npm cache on the lockfile), and `build_and_deploy_job` carries `needs: test` (`:36`). Path filter is `src/frontend/**` plus the workflow file (`:7-9,14-16`). Secrets present: only `AZURE_STATIC_WEB_APPS_API_TOKEN_…` and `GITHUB_TOKEN` — **no Clerk or ORS secret is wired into CI anywhere**, in either workflow. `backend.yml` is path-filtered to `src/backend/**` and runs `dotnet test` (`:26`) with `deploy` gated by `needs: test` (`:30`).
- Adding an e2e job to the deploy gate means extending `needs:` on `build_and_deploy_job`, plus a `npx playwright install --with-deps chromium` step (and its cache).

### The Clerk key, solved

`ClerkProvider` fails hard on a missing key: `parsePublishableKey` throws `"Publishable key is missing…"` when empty and `"Publishable key not valid."` when malformed (`node_modules/@clerk/shared/dist/keys.mjs:80-86`). Validation is purely structural (`keys.mjs:113-124` → `isValidDecodedPublishableKey`, `keys.mjs:61-66`): prefix `pk_test_`/`pk_live_`, exactly three underscore-separated parts, and a base64 payload that decodes to `something.with.a.dot$`.

Verified against the installed SDK in this repo:

```
synthetic key: pk_test_Y2xlcmsuZXhhbXBsZS5jb20k
isPublishableKey: true
parse: {"instanceType":"development","frontendApi":"clerk.example.com"}
```

So the e2e can supply `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=pk_test_Y2xlcmsuZXhhbXBsZS5jb20k` with no Clerk account, no dashboard, and no CI secret. clerk-js will then try to load from `clerk.example.com` and fail — which, per the analysis above, is the *desired* condition, not a defect, since it is the auth-shaped failure risk #7 describes. It does need to be blocked deliberately in the harness rather than left to DNS.

`NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and `CLERK_SECRET_KEY` have no direct `process.env` read site in app code (`.env.example:5-6` only) — both are consumed implicitly by the SDK via `ClerkProvider` and `clerkMiddleware()`.

## Code References

- `src/backend/VeloRoute/Program.cs:407-420` — `POST /routes/gpx`, two validation branches, unauthenticated
- `src/backend/VeloRoute/Program.cs:429` — `GpxRequest` DTO
- `src/backend/VeloRoute/Routing/RouteResult.cs:17` — `RouteCoordinate(double Longitude, double Latitude)`
- `src/backend/VeloRoute/Routing/GpxSerializer.cs:13-14,21` — InvariantCulture, GPX 1.1 namespace
- `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:133-145` — factory constructor and options
- `src/backend/VeloRoute.Tests/Routing/AuthMiddlewareTests.cs:30-42` — pattern for "anonymous endpoint is not 401"
- `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs:8` — camelCase JSON body evidence
- `src/frontend/src/app/layout.tsx:2,22,32` — `ClerkProvider` wrapping all of `<html>`
- `src/frontend/src/middleware.ts:1,3,5-7` — `clerkMiddleware()` on `/`
- `src/frontend/src/components/RouteInfoPanel.tsx:4,30-31` — the only anonymous-path Clerk read
- `src/frontend/src/components/RouteInfoPanel.tsx:68-96` — blob-URL GPX download, no Clerk, no auth header
- `src/frontend/src/components/RouteInfoPanel.tsx:112` — `isSignedIn` gate around Save UI
- `src/frontend/src/components/RouteForm.tsx:33,49-50,55-77,81-87` — validity rule, labels, inputs, submit
- `src/frontend/src/components/SearchBar.tsx:22,41,79-113` — combobox, debounce, listbox
- `src/frontend/src/components/RouteMap.tsx:10,14,63,66-84` — start point is prop-only; no click handler
- `src/frontend/src/app/api/geocode/route.ts:9,15-19` — direct ORS call with `ORS_API_KEY`
- `src/frontend/src/lib/apiProxy.ts:1-7,15` — `requireAuthHeader`, `VELO_API_URL` fallback
- `src/frontend/vitest.config.ts:7-11` — no `include`/`exclude`
- `src/frontend/next.config.ts:15` — `output: 'standalone'`
- `.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml:19-36` — `test` job and `needs: test`
- `node_modules/vitest/dist/chunks/defaults.9aQKnqFk.js:5-6` — Vitest default globs (verified, not quoted from docs)
- `node_modules/@clerk/shared/dist/keys.mjs:61-66,80-86,113-124` — publishable-key validation

## Architecture Insights

- **Auth gating is imperative, never declarative.** No `<SignedIn>`/`<SignedOut>` anywhere; every gate is `useUser().isSignedIn` in a component that also renders anonymous content. That is precisely why an auth failure can take down an anonymous feature — the anonymous and authenticated UI share a component, and `RouteInfoPanel` is the shared one.
- **The frontend has two distinct backend relationships.** `/api/geocode` calls ORS *directly* from the Next server; everything else proxies to .NET. An e2e that only mocks "the backend" would still hit live ORS through the geocode hop.
- **GPX is deliberately backend-owned and filename-free.** `context/archive/2026-06-04-gpx-export/plan.md:8-9,43-45,69-71` — one canonical serialiser, no client-side generation, no `Content-Disposition`. Tests should encode that contract, not fight it.
- **The GPX download handler is triplicated** across `RouteInfoPanel.tsx`, `my-routes/[id]/page.tsx`, `r/[token]/page.tsx`. Not this change's job to fix, but worth knowing that e2e coverage of one call site says nothing about the other two.
- **`internal` + existing test access.** `GpxSerializer` is `internal` yet already called from tests, so `InternalsVisibleTo` is in place — a new test class needs no extra plumbing.

## Historical Context (from prior changes)

- `context/archive/2026-09-08-test-plan-refresh-2026-09-08/plan.md:45` — "**Not installing Playwright** or writing any e2e test. §4 records it as an unpinned candidate; phase 5 evaluates and pins it." Playwright was *deferred by design*, never evaluated and rejected.
- `context/archive/2026-06-04-gpx-export/reviews/impl-review.md` F2 — the per-coordinate range guard now at `Program.cs:412-416` was added as a review fix; it has been untested since it landed. F1 (blob-URL cleanup) explains the `try/finally` in `handleDownload`.
- `context/archive/2026-07-04-auth-provider-scaffold/plan.md:78` — the origin of `ClerkProvider` at the root layout ("can wrap the root `layout.tsx` directly"). Neither that plan nor `2026-07-15-magic-link-auth` ever discussed the anonymous path sitting inside the provider as a risk; that framing first appears in the 2026-09-08 test-plan refresh (`test-plan.md:89`).
- **Stale archive prose**: `context/archive/2026-07-15-magic-link-auth/plan.md` still describes magic-link auth throughout. The correction lives only in `context/foundation/roadmap.md:112,119` and `prd-v2.md:82-90,172` — auth switched to Clerk `email_code` on 2026-09-08, a Dashboard-only change with no code diff. Archives are frozen artifacts; consult `roadmap.md` for current auth behaviour. (Matches the repo memory note on this.)
- `context/foundation/prd-v2.md:146-154` Constraints — "Anonymous route generation must continue to work without an account in v2"; Guardrails at `prd-v2.md:56-59` add "GPX export from the map page must remain accessible without authentication." These are the two lines this phase's tests exist to defend.
- `context/foundation/roadmap.md` — all v2 slices done except S-07 `routing-quality-osm` (`parked`, blocked on an OSM/Overpass data-source decision).
- **No test-secret precedent exists.** No prior artifact decides how tests obtain Clerk/ORS keys; `test-plan.md:143-145` explicitly defers it here. The synthetic-key finding above closes it.
- **`context/foundation/lessons.md` does not exist.** Lesson-shaped notes live inline in `roadmap.md` (e.g. `roadmap.md:243`).

## Related Research

No prior `research.md` covers e2e, Playwright, or the anonymous flow. The closest artifacts are `context/archive/2026-09-08-test-plan-refresh-2026-09-08/plan.md` (which scoped this phase) and `context/foundation/test-plan.md` §2 risk #7 / §3 phase 5.

## Open Questions

1. **Does the app render usably when clerk-js cannot load?** Unknown, and not answerable without a browser. If `ClerkProvider` degrades gracefully (hooks return `isLoaded: false`, `isSignedIn: undefined`), the whole anonymous flow works offline from Clerk and the e2e is trivially deterministic. If it throws or suspends forever, that is *itself* a finding against risk #7 and the phase has found a real bug. Either outcome is a result; the plan should treat this as the first thing implementation checks, not an assumption. Fallback if it hard-fails: allow the clerk-js request through in the default spec and keep the blocked case as a separate, explicitly-marked spec.
2. **Does `next start` work with `output: 'standalone'` in Next 15.5?** No guard found in the installed CLI, but unverified. Determines whether `webServer.command` is `npm start` or `node .next/standalone/server.js` (the latter also needs `public/` and `.next/static` copied). Cheap to settle by running it once.
3. **Should the e2e job gate the SWA deploy?** Adding it to `build_and_deploy_job`'s `needs:` makes it a release gate and buys a browser install (~1–2 min) on every frontend PR. The alternative is running it non-blocking at first. Not a research question — a call for the plan, flagged because `AGENTS.md:50` documents the current single-job gate and goes stale either way.
4. **Does `clerkMiddleware()` make a network call on an anonymous request?** Anonymous requests carry no token, so JWKS fetch is unlikely, but unverified. Only matters if the harness blocks the Clerk host at the *server* level rather than in the page.

## Note on a stale repo instruction (found incidentally)

`.github/copilot-instructions.md:8` and `src/frontend/AGENTS.md:3` both instruct agents to read `node_modules/next/dist/docs/` before writing Next.js code. **That directory does not exist** in the current install (`next` ships only `license.md` and `README.md` as markdown). The instruction cannot be followed as written. Not in this change's scope, but it is exactly the class of doc staleness the repo's own workflow conventions ask to fix on sight, and phase 5 will be writing Next-adjacent tooling.
