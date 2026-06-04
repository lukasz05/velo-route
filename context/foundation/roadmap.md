---
project: "VeloRoute"
version: 1
status: draft
created: 2026-05-27
updated: 2026-05-30
prd_version: 1
main_goal: speed
top_blocker: skills
---

# Roadmap: VeloRoute

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Road cyclists often lack a ready-made route when they want to ride. Planning one manually — searching forums, piecing together maps — costs time and can disappoint: wrong surface, too much traffic, wrong length. VeloRoute removes that friction: enter a start point and a distance range, receive a loop-route proposal tuned for road bikes (paved, low-traffic) displayed on an interactive map, and download it as a GPX file — entirely free, no account required. The product's bet is that free + loop-specific beats the paid, generic incumbents (Komoot, Strava, Google Maps) for this one job.

## North star

**S-01: loop route generated and displayed** — proving the core product hypothesis (the unvalidated bet that the backend can compute a useful loop route from a start point and distance range) as early as Prerequisites allow, because the GPX export, UI polish, and every v2 feature only matter if the routing algorithm produces something a road cyclist would actually ride.

> North star: the smallest end-to-end slice whose successful delivery proves the core product hypothesis — placed as early as Prerequisites allow because everything else only matters if this works.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| F-01 | `routing-api-wiring` | (foundation) road-network data API wired; .NET HTTP client callable; data contract defined | — | FR-003, Business Logic | done |
| S-01 | `loop-route-generation` | enter start point + distance range, trigger generation, view loop route on interactive map with total length shown | F-01 | US-01, FR-001, FR-002, FR-003, FR-004, FR-005, NFR (privacy, 5s) | done |
| H-01 | `project-rename` | (housekeeping) scaffold placeholder names replaced with VeloRoute in both projects; app metadata correct | — | — | proposed |
| S-02 | `gpx-export` | download the route as a GPX file importable to Strava, Garmin, and Komoot without modification | S-01 | US-01, FR-006 | proposed |
| S-03 | `loop-algorithm-tuning` | (quality) generated routes feel like real cycling loops — minimal overlap, good shape, distance lands close to the requested midpoint | S-01 | Business Logic (≤10% repetition, paved preference) | proposed |

## Baseline

What's already in place in the codebase as of 2026-05-27 (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** present — Next.js 16 + React 19 + TypeScript scaffolded (`src/frontend/`; per tech-stack.md)
- **Backend / API:** present — .NET 10 Minimal API with health + OpenAPI scaffold (`src/backend/Program.cs`)
- **Data:** absent — no DB driver, ORM, schema, or migrations (v1 is intentionally stateless; no server-side persistence by design)
- **Auth:** absent — no auth provider, session/token code, or middleware (v1 has no auth by design; deferred to v2)
- **Deploy / infra:** present — Azure SWA (Standard) + App Service (S1, Linux) provisioned; GitHub Actions frontend deploy pipeline live (user-confirmed 2026-05-27)
- **Observability:** partial — .NET default logging configured (`appsettings.json`); no error tracking, metrics, or distributed tracing

## Foundations

### F-01: Routing data API wiring

- **Outcome:** (foundation) a road-network data provider is selected, an HTTP client is implemented in the .NET backend, and the data contract (road segments, surface type, road classification) the routing algorithm will consume is established and reachable from `src/backend/`.
- **Change ID:** `routing-api-wiring`
- **PRD refs:** FR-003, Business Logic ("draws on surface type and road classification data drawn from publicly available road-network datasets")
- **Unlocks:** S-01 — the routing algorithm can't be designed until the data format and provider capabilities are known; the data contract is the input specification for loop-route computation.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** Which provider (OpenRouteService free tier vs. OSM Overpass API) delivers road-type and surface-quality data in a format suitable for loop-route computation? — Owner: TBD. Block: no (OpenRouteService cycling profiles are the indicated candidate per shape-notes; work proceeds while evaluating).
- **Risk:** OpenRouteService free tier imposes rate limits and may have data gaps for some regions. If the free tier is insufficient, an OSM Overpass API integration requires a different pattern (raw graph data rather than a routing service). Discovering this late forces a re-implementation of F-01 mid-sprint.
- **Status:** done

## Housekeeping

### H-01: Project rename, READMEs, and license

- **Outcome:** all scaffold-generated placeholder names replaced with VeloRoute; both projects have real READMEs; repo has a LICENSE; app metadata is correct and no `bootstrap-scaffold` or `bootstrap_scaffold` identifiers remain in source.
- **Change ID:** `project-rename`
- **PRD refs:** —
- **Prerequisites:** — (can run any time, parallel with any slice)
- **Blockers:** —
- **Scope:**
  - **`README.md` (repo root, new)** — project overview, monorepo layout, dev commands for both projects, required env vars, link to PRD
  - **`LICENSE` (repo root, new)** — MIT (confirm before implementing if a different license is preferred)
  - **`src/frontend/package.json`** — `name` → `velo-route`; add `description`, `author`, `license`, `repository`, `homepage` fields
  - **`src/frontend/src/app/layout.tsx`** — `metadata.title` → `"VeloRoute"`; `metadata.description` → real description
  - **`src/frontend/README.md`** — replace generic create-next-app content with VeloRoute frontend dev guide (how to run, env vars, `local-ca.pem` note)
  - **`src/backend/README.md` (new)** — backend dev guide: how to run, user secrets setup, Swagger URL, project structure
  - **`src/backend/bootstrap-scaffold.csproj`** — rename to `velo-route.csproj`; `<RootNamespace>` → `VeloRoute`; add `<AssemblyName>`, `<Version>`, `<Description>`, `<Authors>` properties
  - **`src/backend/Program.cs`** — `using bootstrap_scaffold.Routing` → `using VeloRoute.Routing`; remove dead `WeatherForecast` record
  - **`src/backend/Routing/*.cs`** (10 files) — `namespace bootstrap_scaffold.Routing` → `namespace VeloRoute.Routing`
  - **`src/backend/bootstrap-scaffold.http`** — rename to `velo-route.http`; update `@bootstrap_scaffold_HostAddress` variable; remove stale `/weatherforecast/` request
- **Risk:** renaming the C# namespace touches every source file; solution references need updating too. Low risk overall — pure rename, no logic change.
- **Status:** proposed

## Slices

### S-01: Loop route generation and display

- **Outcome:** user can enter a starting point via a search bar (with map confirmation), specify a minimum and maximum distance in km, trigger route generation, and see the resulting loop route displayed on an interactive map with the total length shown.
- **Change ID:** `loop-route-generation`
- **PRD refs:** US-01, FR-001, FR-002, FR-003, FR-004, FR-005, NFR ("location inputs leave no trace in operator-accessible storage after the request completes"; results within 5 seconds)
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - What graph-traversal and loop-constraint approach will reliably produce usable road-bike loop routes within the stated distance range, respecting the ≤ 10% repetition rule (PRD Business Logic), on the first build? — Owner: TBD. Block: no (proceed by spiking; output quality is the uncertainty, not whether to attempt it).
  - Which geocoding service powers the start-point search bar (FR-001), and does it require a separate API key or quota management? — Owner: TBD. Block: no (several free options exist; resolvable at planning time).
- **Risk:** The routing algorithm is the highest-effort, highest-uncertainty item in this roadmap. The user has identified algorithm complexity as the top blocker (`top_blocker: skills`). If the first approach produces poor routes (wrong length, too much repetition, avoids paved roads), iteration cost is the primary schedule threat against a 3-week, after-hours-only timeline.
- **Status:** done (impl_reviewed — commit 82767d8)

- **Outcome:** generated routes feel like routes a road cyclist would actually choose — minimal self-overlap, recognisably loop-shaped, with total distance landing close to the requested midpoint. Includes measurable quality criteria so regressions are detectable.
- **Change ID:** `loop-algorithm-tuning`
- **PRD refs:** Business Logic (≤10% repetition rule, paved surface preference)
- **Prerequisites:** S-01
- **Parallel with:** S-02
- **Blockers:** —
- **Unknowns:**
  - What waypoint placement geometry (radius formula, bearing count, waypoint count) produces the best loop shapes across varied European cities? — Owner: TBD. Block: no (resolvable through research + empirical testing during S-03 planning).
  - What automated quality metrics are feasible without a test runner (e.g. overlap ratio thresholds, compactness score, distance accuracy)? — Owner: TBD. Block: no.
- **Risk:** ORS snaps waypoints to the road network, so ideal geometric placement does not guarantee ideal route shape. Algorithm improvements may yield diminishing returns for certain geographies (dense urban grids vs. rural areas). Define a "good enough" acceptance threshold before starting to avoid open-ended tuning.
- **Status:** proposed

### S-02: GPX export

- **Outcome:** user can download a GPX file for the generated route proposal; the exported file is importable to Strava, Garmin, and Komoot without modification.
- **Change ID:** `gpx-export`
- **PRD refs:** US-01, FR-006
- **Prerequisites:** S-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Does the GPX format produced by .NET serialisation satisfy all three platform import requirements (Strava, Garmin, Komoot) without a cross-platform smoke test? — Owner: TBD. Block: no (GPX 1.1 is the standard; the main risk is element type — `<trkseg>` vs. `<rte>` — resolvable at implementation time with a quick import test).
- **Risk:** GPX cross-platform compatibility (Strava + Garmin + Komoot) is a Guardrail in the PRD. If the emitted file fails import on any platform, it is a launch blocker. Validate against all three platforms before marking S-02 done.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| F-01 | `routing-api-wiring` | Wire road-network data API (provider selection + .NET HTTP client + data contract) | yes | Run `/10x-plan routing-api-wiring` | **done** |
| S-01 | `loop-route-generation` | Loop route generation and interactive map display (FR-001–FR-005) | no | Depends on F-01 |
| S-02 | `gpx-export` | GPX export (FR-006) | no | Depends on S-01 |
| S-03 | `loop-algorithm-tuning` | Loop route quality tuning — waypoint geometry, quality metrics, regression criteria | no | Depends on S-01; can run in parallel with S-02 |

## Open Roadmap Questions

1. **Which external road-network data provider (OpenRouteService vs. OSM Overpass API vs. other) will be used for route data?** — Owner: TBD. Block: F-01 (resolved during F-01 foundation work; does not block the roadmap, only gates F-01's implementation approach).
2. **What loop-route generation algorithm will produce reliably usable results on the first build?** — Owner: TBD. Block: S-01 (the central unknown for algorithm design; resolved during S-01 planning and spiking — this is why `top_blocker: skills`).

## Parked

- **Point-to-point routes** — Why parked: PRD §Non-Goals ("Only loop routes (start = end) are generated. Point-to-point support is deferred to v2.").
- **User accounts, saved routes, route library** — Why parked: PRD §Non-Goals ("Authentication and persistence are deferred to v2. GPX export serves as the persistence mechanism for v1.").
- **Miles / imperial units** — Why parked: PRD §Non-Goals ("Kilometres only. Miles support is deferred to v2.").
- **Multiple route proposals per request** — Why parked: PRD §Non-Goals ("A single route proposal is generated per request. Multiple-proposal support is deferred to v2 once the algorithm is proven."). Also: `main_goal: speed` — reducing to 1 proposal removes API cost and algorithmic complexity in the MVP.
- **Social / sharing features** — Why parked: PRD §Non-Goals ("explicitly out of scope").
- **Offline-first / PWA** — Why parked: PRD §Non-Goals ("The app requires a network connection to generate routes.").

## Done

- **F-01 `routing-api-wiring`** — ORS HTTP client, data contracts (`SurfaceType`, `RoadClass`, `RouteResult`), and config wired in `.NET` backend. Verified 2026-05-30.
- **S-01 `loop-route-generation`** — start point search, km range input, loop generation (3-bearing triangular algorithm), interactive map, distance display. All 4 phases implemented, impl-review triaged. Commit 82767d8.
