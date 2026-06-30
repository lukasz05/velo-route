---
project: "VeloRoute"
version: 2
status: draft
created: 2026-05-27
updated: 2026-06-30
prd_version: 1
main_goal: speed
top_blocker: none
---

# Roadmap: VeloRoute

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Road cyclists often lack a ready-made route when they want to ride. Planning one manually — searching forums, piecing together maps — costs time and can disappoint: wrong surface, too much traffic, wrong length. VeloRoute removes that friction: enter a start point and a distance range, receive a loop-route proposal tuned for road bikes (paved, low-traffic) displayed on an interactive map, and download it as a GPX file — entirely free, no account required. The product's bet is that free + loop-specific beats the paid, generic incumbents (Komoot, Strava, Google Maps) for this one job.

## North star

**S-03: loop algorithm quality tuning** — the PRD's primary success criterion (generate and export a loop route) is now met by S-01 + S-02; S-03 validates that the generated routes satisfy the Business Logic constraints defined in the PRD — ≤ 10% segment repetition, paved-surface preference — before calling v1 done.

> North star here means the remaining validation milestone for v1: the slice whose completion closes the gap between "features working" and "features working well enough to ship." Placed as early as its one prerequisite (S-01, done) allows.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| F-01 | `routing-api-wiring` | (foundation) ORS HTTP client wired; road-network data contract defined | — | FR-003, Business Logic | done |
| F-02 | `testing-backend-bootstrap` | (foundation) xUnit project bootstrapped; 43 tests cover ORS mapping and GPX serialiser correctness | — | Business Logic, FR-006 | done |
| F-03 | `route-generation-integration-tests` | (foundation) integration tests verify distance/overlap constraints and ORS timeout behaviour | F-02 | Business Logic (≤10% repetition, distance bounds), Success Criteria (5 s) | done |
| F-04 | `security-privacy-guards` | (foundation) integration tests confirm no input coordinates in logs and no API key in error responses | — | NFR (location inputs leave no trace) | done |
| F-05 | `backend-deploy` | (foundation) .NET backend deployed and publicly reachable on Azure; GitHub Actions CI/CD live; `dotnet test` gate on every PR | — | Success Criteria (5 s), NFR (cross-browser, mobile) | ready |
| S-01 | `loop-route-generation` | enter start point + distance range, trigger generation, view loop route on interactive map with total length shown | F-01 | US-01, FR-001, FR-002, FR-003, FR-004, FR-005, NFR (privacy, 5 s) | done |
| S-02 | `gpx-export` | download route as a GPX file importable to Strava, Garmin, and Komoot without modification | S-01 | US-01, FR-006 | done |
| S-03 | `loop-algorithm-tuning` | generate routes that feel like real cycling loops — minimal self-overlap, recognisably loop-shaped, total distance close to the requested midpoint | S-01 | Business Logic (≤10% repetition, paved preference) | done |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme | Chain | Note |
|---|---|---|---|
| A | Core feature pipeline | `F-01` → `S-01` → `S-02` → `S-03` | Main v1 feature delivery; S-03 is the north star. |
| B | Test foundation | `F-02` → `F-03` | Unit bootstrap done; integration tests ready to run parallel with S-03. |
| C | Security & privacy | `F-04` | Standalone; verifies PRD privacy NFR; parallel with S-03. |
| D | Backend deploy & CI | `F-05` | Standalone; needed before v1 is accessible to real users; parallel with S-03. |

## Baseline

What's already in place in the codebase as of 2026-06-15 (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** present — Next.js 15 + React 19 + TypeScript; MapLibre GL; RouteForm, RouteMap, RouteInfoPanel components functional (`src/frontend/`)
- **Backend / API:** present — .NET 10 minimal API; `POST /routes/loop` + `POST /routes/gpx` endpoints; ORS HTTP client with retry/circuit-breaker; GPX serialiser (`src/backend/`)
- **Data:** absent — stateless by design; no DB driver, ORM, or migrations (v1 intentionally stateless)
- **Auth:** absent — no auth provider, session/token code, or middleware; deferred to v2 by design
- **Deploy / infra:** partial — Azure SWA CI/CD for frontend live (`.github/workflows/azure-static-web-apps-purple-sky-08f4fb710.yml`); no backend deployment config or pipeline
- **Observability:** partial — .NET default logging in `appsettings.json`; no error tracking, distributed tracing, or metrics
- **Tests:** partial — xUnit project at `src/backend/VeloRoute.Tests/`; 43 unit tests (ORS mapping + GPX serialiser) + integration tests covering distance bounds, ≤10% overlap, and ORS timeout; no frontend tests; no CI gate

## Foundations

### F-01: Routing data API wiring

- **Outcome:** (foundation) OpenRouteService HTTP client implemented in .NET backend; road-network data contract (SurfaceType, RoadClass, RouteResult) defined; resilience pipeline (retry, circuit breaker, timeout) configured.
- **Change ID:** `routing-api-wiring`
- **PRD refs:** FR-003, Business Logic ("draws on surface type and road classification data from publicly available road-network datasets")
- **Unlocks:** S-01 — loop-route algorithm required the ORS data contract before the routing logic could be designed.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** ORS free-tier rate limits and data gaps in some regions were the primary concern; ORS proved sufficient during implementation.
- **Status:** done

### F-02: Backend test bootstrap

- **Outcome:** (foundation) xUnit project at `src/backend/VeloRoute.Tests/` bootstrapped; `VeloRoute.sln` at repo root; `OrsMapper` extracted from `OpenRouteServiceClient` for testability; 43 tests covering ORS surface/road-class mapping and GPX serialiser locale-safety.
- **Change ID:** `testing-backend-bootstrap`
- **PRD refs:** Business Logic (ORS mapping correctness), FR-006 (GPX export guardrails — InvariantCulture decimal formatting)
- **Unlocks:** F-03 — integration test project inherits the xUnit setup and OrsMapper testability surface established here; provides regression baseline for S-03 quality work.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** `OrsMapper` was extracted from `OpenRouteServiceClient` as a production refactor required for testability — regression risk is low (all 43 tests pass) but the extraction path is load-bearing.
- **Status:** done

### F-03: Route generation integration tests

- **Outcome:** (foundation) integration tests verify that `LoopRouteGenerator` produces routes within [min_km, max_km] and with ≤ 10% overlap regardless of waypoint geometry path, and that the ORS timeout deadline fires correctly under slow-response conditions.
- **Change ID:** `route-generation-integration-tests`
- **PRD refs:** Business Logic (≤10% repetition rule, distance bounds), Success Criteria secondary ("within 5 seconds")
- **Unlocks:** regression gate for S-03 quality work — any waypoint geometry change can be verified against the PRD constraints without manual testing; covers Risk #2 and Risk #5 from `context/foundation/test-plan.md`.
- **Prerequisites:** F-02
- **Parallel with:** S-03, F-04, F-05
- **Blockers:** —
- **Unknowns:**
  - Which HTTP mocking approach — `WireMock.Net` vs. a custom `IOpenRouteServiceClient` fake — gives the best signal-to-setup ratio for constraint verification? — Owner: TBD. Block: no (resolvable during planning; both options are viable).
- **Risk:** Integration tests that mock ORS at the HTTP boundary can pass while real ORS responses differ in shape. Use the data contracts from F-01 (`RouteResult`, `SurfaceType`, `RoadClass`) as the oracle to avoid this drift.
- **Status:** done

### F-04: Security and privacy guards

- **Outcome:** (foundation) integration tests confirm that (a) completed route-generation requests leave no input coordinate values in backend logs, and (b) ORS HTTP error responses forwarded to the caller contain no API key string.
- **Change ID:** `security-privacy-guards`
- **PRD refs:** NFR ("location inputs submitted during route generation leave no trace in operator-accessible storage after the request that consumed them completes")
- **Unlocks:** verifies the PRD privacy NFR at the code level; covers Risk #4 and Risk #6 from `context/foundation/test-plan.md`; required before calling v1 compliance-complete.
- **Prerequisites:** —
- **Parallel with:** S-03, F-03, F-05
- **Blockers:** —
- **Unknowns:**
  - Does the .NET HTTP client emit request bodies (and therefore ORS coordinates) at Debug log level by default in the development profile? — Owner: TBD. Block: no (resolvable by inspection of `appsettings.Development.json` during planning).
- **Risk:** Logging configuration can change silently across .NET minor versions; the test must capture `ILogger` output during a live request and assert no coordinate values appear, not merely assert that a log level is set.
- **Status:** done

### F-05: Backend deployment and CI gate

- **Outcome:** (foundation) .NET backend deployed and publicly reachable on Azure App Service; GitHub Actions pipeline publishes the backend on push to `main`; `dotnet test VeloRoute.sln` runs as a required CI gate on every PR.
- **Change ID:** `backend-deploy`
- **PRD refs:** Success Criteria secondary ("results page loads within 5 seconds"), NFR ("usable on the latest two major versions of Chrome, Firefox, Safari, and Edge"; "fully usable on small-screen mobile devices")
- **Unlocks:** v1 accessible to real users; 5 s load-time NFR measurable in production; CI gate locks the test floor established by F-02 and extended by F-03.
- **Prerequisites:** —
- **Parallel with:** S-03, F-03, F-04
- **Blockers:** —
- **Unknowns:**
  - Deployment method for .NET on Azure App Service: direct `dotnet publish` in GitHub Actions, container image, or Azure deployment slots? — Owner: user. Block: no (all options viable on the existing S1 App Service plan).
  - Does the App Service need ORS API key and other environment-specific config surfaced as App Settings, or is `appsettings.json` + user secrets sufficient? — Owner: user. Block: no.
- **Risk:** Backend (Azure App Service) and frontend (Azure SWA) are deployed separately. CORS configuration must allow the SWA origin or cross-origin requests will fail silently in production — validate this as the first smoke test after deploy.
- **Status:** ready

## Slices

### S-01: Loop route generation and display

- **Outcome:** user can enter a starting point via a search bar (with map confirmation), specify a minimum and maximum distance in km, trigger route generation, and see the resulting loop route displayed on an interactive map with the total length shown.
- **Change ID:** `loop-route-generation`
- **PRD refs:** US-01, FR-001, FR-002, FR-003, FR-004, FR-005, NFR ("location inputs leave no trace"; results "within 5 seconds")
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Routing algorithm was the highest-uncertainty item in this roadmap; resolved by a 3-bearing triangular approach. Residual output quality is tracked in S-03.
- **Status:** done

### S-02: GPX export

- **Outcome:** user can download a GPX file for the generated route proposal; the exported file is importable to Strava, Garmin, and Komoot without modification.
- **Change ID:** `gpx-export`
- **PRD refs:** US-01, FR-006
- **Prerequisites:** S-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** GPX cross-platform compatibility (Strava + Garmin + Komoot) is a PRD Guardrail. `<trk>/<trkseg>/<trkpt>` structure and InvariantCulture decimal formatting were validated by F-02 unit tests. A cross-platform smoke test against all three platforms is recommended before launch.
- **Status:** done

### S-03: Loop algorithm quality tuning

- **Outcome:** user receives routes that feel like real road cycling loops — minimal self-overlap, recognisably loop-shaped, total distance landing close to the requested midpoint — with measurable quality criteria so regressions are detectable.
- **Change ID:** `loop-algorithm-tuning`
- **PRD refs:** Business Logic ("at most 10% of the route length may repeat"; "segments on unpaved or low-quality surfaces … are deprioritised or excluded")
- **Prerequisites:** S-01
- **Parallel with:** F-03, F-04, F-05
- **Blockers:** —
- **Unknowns:**
  - What waypoint placement geometry (radius formula, bearing count, waypoint count) produces the best loop shapes across varied geographies (dense urban, rural, coastal)? — Owner: TBD. Block: no (current approach documented in `context/foundation/loop-route-algorithm.md`; research + empirical testing is the path to improvement).
  - What "good enough" acceptance threshold (overlap %, distance accuracy %, compactness score) defines S-03 as done? — Owner: user. Block: no (must define before starting to avoid open-ended tuning; given `main_goal: speed`, this is the primary scope-creep risk).
- **Risk:** ORS snaps waypoints to the road network, so ideal geometric placement does not guarantee ideal route shape; improvements may yield diminishing returns in certain geographies. Defining a concrete acceptance threshold before implementation starts is the single most important risk mitigant for this slice.
- **Status:** done

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| F-01 | `routing-api-wiring` | Wire road-network data API (ORS HTTP client + data contract + resilience) | — | **done** |
| F-02 | `testing-backend-bootstrap` | Backend test bootstrap — ORS mapping and GPX locale coverage (Phase 1 of test-plan.md) | — | **done** (impl_reviewed) |
| F-03 | `route-generation-integration-tests` | Route generation integration tests — distance/overlap constraints + ORS timeout (Phase 2) | — | **done** |
| F-04 | `security-privacy-guards` | Security and privacy guards — coordinate logging + API key leakage (Phase 3) | — | **done** |
| F-05 | `backend-deploy` | Backend deployment to Azure App Service + GitHub Actions CI/CD + `dotnet test` gate | yes | Run `/10x-plan backend-deploy` |
| S-01 | `loop-route-generation` | Loop route generation and interactive map display (FR-001–FR-005) | — | **done** |
| S-02 | `gpx-export` | GPX export — download route as GPX (FR-006) | — | **done** |
| S-03 | `loop-algorithm-tuning` | Loop route quality tuning — waypoint geometry, acceptance threshold, regression criteria | — | **done** |

## Open Roadmap Questions

None. All PRD Open Questions resolved during implementation. Remaining unknowns are per-slice (see individual Unknowns fields above).

## Parked

- **Point-to-point routes** — Why parked: PRD §Non-Goals ("Only loop routes (start = end) are generated. Point-to-point support is deferred to v2.").
- **User accounts, saved routes, route library** — Why parked: PRD §Non-Goals ("Authentication and persistence are deferred to v2. GPX export serves as the persistence mechanism for v1.").
- **Miles / imperial units** — Why parked: PRD §Non-Goals ("Kilometres only. Miles support is deferred to v2.").
- **Multiple route proposals per request** — Why parked: PRD §Non-Goals ("A single route proposal is generated per request. Multiple-proposal support is deferred to v2 once the algorithm is proven.").
- **Social / sharing features** — Why parked: PRD §Non-Goals ("explicitly out of scope").
- **Offline-first / PWA** — Why parked: PRD §Non-Goals ("The app requires a network connection to generate routes.").

## Done

- **F-01 `routing-api-wiring`** — ORS HTTP client, data contracts (`SurfaceType`, `RoadClass`, `RouteResult`), and resilience pipeline wired in .NET backend. Verified 2026-05-30.
- **S-01 `loop-route-generation`** — Start point search, km range input, 3-bearing triangular loop generation, interactive MapLibre map, distance display. Impl-reviewed (commit 82767d8). 2026-05-30.
- **H-01 `project-rename`** — Scaffold placeholder names replaced with VeloRoute across both projects; READMEs, C# namespace (`VeloRoute.Routing`), csproj, and app metadata updated. PR #2 merged.
- **S-02 `gpx-export`** — GPX 1.1 download via `POST /routes/gpx`; `<trk>/<trkseg>/<trkpt>` structure; InvariantCulture decimal formatting. PR #3 merged (commit 5934d0b).
- **F-02 `testing-backend-bootstrap`** — xUnit project bootstrapped; `VeloRoute.sln` created; `OrsMapper` extracted; 43 tests (ORS mapping + GPX serialiser). PR #4 impl-reviewed (commit a2767a4).
- **F-03 `route-generation-integration-tests`** — integration tests verify `LoopRouteGenerator` distance bounds and ≤10% overlap constraint; ORS timeout deadline tested. PR merged (commit ed88527).
- **F-04 `security-privacy-guards`** — integration tests confirm no input coordinates in backend logs and no API key leakage in ORS error responses. Merged 2026-06-20.
- **S-03 `loop-algorithm-tuning`** — paved ratio computed from ORS segment data; paved preference as primary candidate selection key; smoothness score (bearing-change rate) as tiebreaker; 6 fake-ORS quality regression tests + 3 live ORS smoke tests (CI-skipped). PR #7 merged 2026-06-30.
