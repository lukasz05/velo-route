# Structural Map — dependency-cruiser + ArchUnitNET

Follows on from `context/map/artifact-1-territory.md`. Scope note: this repo is a two-language monorepo (Next.js/TS frontend + .NET backend), unlike the mattermost-style single-JS-workspace layout the source prompt series assumes. dependency-cruiser only understands JS/TS module graphs, so it covers `src/frontend/src` only. The backend (`src/backend/VeloRoute`) has no single equivalent tool — NDepend's visual diagrams are Windows-GUI-only and `dotnet-depends` only walks NuGet package trees, not internal namespaces — so backend coverage combines two pieces: **ArchUnitNET** for rule-based boundary/cycle assertions (pass/fail, run as xUnit tests), and a small custom **PowerShell script** (`src/backend/scripts/backend-dep-graph.ps1`) that reuses ArchUnitNET's loaded type graph to compute the fan-in/fan-out metrics and rendered subgraph ArchUnitNET itself doesn't provide.

Everything below is grouped by side of the stack — **Frontend** (dependency-cruiser) then **Backend** (ArchUnitNET + the PowerShell script) — since the two use different tools with different capabilities; nothing in one section implicitly covers the other.

---

# Frontend — dependency-cruiser

## Tool setup

Added `dependency-cruiser@18.2.0` as a frontend devDependency and a `src/frontend/.dependency-cruiser.cjs` config:

- `tsConfig: { fileName: "tsconfig.json" }` + `tsPreCompilationDeps: true` — resolves the `@/*` → `src/*` path alias and TS-only imports.
- `doNotFollow: { path: "node_modules" }` — keeps the graph scoped to first-party code (without this, cycles inside `next`, `@vis.gl/react-maplibre`, and testing-library leak into the report and drown the signal).
- Five rules: `no-circular` (warn), `no-orphans` (info, excludes test files), and three project-specific layer rules — see "Layer boundaries" below.

Run: `npx depcruise src --config .dependency-cruiser.cjs --output-type <json|err-html|dot>` from `src/frontend/`.

## Exploration ideas (top 3, for future passes)

1. **Fan-in/fan-out hotspot tracking over time** — re-run the JSON reporter each sprint and diff fan-in on `src/types/route.ts` and `src/lib/apiProxy.ts`; a sudden jump signals a module turning into a de-facto God object before it shows up as a review pain point.
2. **Reachability from API route handlers** — `--reaches` from each `src/app/api/**/route.ts` shows exactly which backend-facing contracts (`types/route.ts` shapes, `lib/apiProxy.ts`) a given proxy touches, useful when the backend changes a response shape and you need the blast radius on the frontend side.
3. **Orphan sweep restricted to non-Next.js-convention files** — the default orphan rule is noisy here because every `page.tsx`/`route.ts`/`layout.tsx` is a legitimate orphan (Next's file-based router loads them, nothing imports them). A rule that additionally excludes `**/page.tsx`, `**/layout.tsx`, `**/route.ts` would isolate genuine dead code instead.

Reports available beyond JSON/dot: `err-html` (violations only, good for CI gating), `text` (grep-friendly for scripting), `mermaid` (renders inline in markdown viewers without invoking Graphviz).

## Cycles in active areas

**Zero circular-dependency violations inside `src/`** across all territory-map hotspots (`src/app`, `src/components`, `src/lib`, `src/types`). The only `no-circular` hits in the raw (unscoped) run were inside `node_modules` (`next`, `@vis.gl/react-maplibre`, testing-library) — excluded once `doNotFollow` was set.

| Observation | Evidence | Why it matters for change |
|---|---|---|
| No cycles among first-party modules | 0 `error`/`warn` violations in `depcruise src --config .dependency-cruiser.cjs` JSON summary | A cycle-free graph means any module can be reasoned about (and tested) without pulling in a mutually-dependent partner — consistent with the app being young (4 months) and not yet accumulated the kind of circular coupling `artifact-1-territory.md` flagged for `TestInfrastructure.cs` on the backend side. |
| Territory's `Program.cs` ↔ `TestInfrastructure.cs` coupling (6 commits) is out of scope for dependency-cruiser | N/A — backend, not JS/TS | dependency-cruiser cannot confirm or refute whether that coupling is a true cycle; it's a co-change signal from git history, not an import-graph fact. **Resolved in the Backend section below**, via ArchUnitNET's namespace-level cycle rule — see "Backend → Layer boundaries & cycles". |

## Layer boundaries

Chose three project-specific boundary rules (this codebase has no `platform/client` vs `channels` split — the closest analog is `types` → `lib` → `components` → `app`, plus `app/api` as a backend-proxy layer that shouldn't reach into UI):

| Boundary checked | Result | Evidence | Why it matters | Territory link |
|---|---|---|---|---|
| `src/types` must not import `app` or `components` | **Holds** | 0 violations of `types-are-foundation` rule | `route.ts` types (fan-in 14 — highest in the codebase) stay a true leaf dependency; nothing downstream can be surprised by a type file reaching back up | Territory flags `src/frontend/src/types/route.ts` as a top-7 hottest file (7 commits) and part of the `app ↔ components ↔ types` co-change triangle — this confirms the triangle is a clean DAG, not a cycle |
| `src/lib` must not import `src/components` | **Holds** | 0 violations of `lib-no-components` rule | `lib/apiProxy.ts` (fan-in 7) and `lib/routingApi.ts` stay UI-agnostic, so they can be exercised in tests or reused without a React tree | Not called out by name in territory, but sits directly under the `app ↔ components` co-change pair as their shared dependency |
| `src/app/api/**` (Next.js route handlers proxying to the backend) must not import `src/components` | **Holds** | 0 violations of `api-routes-no-components` rule | Confirms the proxy layer territory identifies (`app/api` co-changing with backend `Program.cs`) is a genuine server-only seam, not accidentally bundling client UI code into a route handler | Matches the copilot-instructions description of `app/api/**/route.ts` as pure proxy files |

No surprising imports found — the frequently-changed areas (`admin`/`my-routes` equivalents here: `app`, `components`, `types`) use the layers predictably. This is a smaller, younger codebase than the one the prompt series was written for, so "predictable" here mostly means "hasn't had the opportunity to rot yet" rather than proof of a hardened boundary — re-check after the OSM-routing-quality work lands (per `context/foundation/roadmap.md` v2-remaining scope), since that's likely to add new shared modules.

## Testability risks

**Summary**: The frontend is small (40 first-party modules) and shallow — no import chain exceeds a handful of hops — so testability risk concentrates in a few hub nodes rather than being spread thin.

**Risk list**:
- `src/components/RouteApp.tsx` — fan-out 8, the highest in the codebase. It's the client-side orchestrator wiring form input, map, API calls, and result panel together. Unit-testing it in isolation means mocking most of the component tree; an integration-style test (render + user-event, already the pattern per `RouteMap.test.tsx` etc.) is the better fit, matching what's already in the repo.
- `src/components/RouteMap.tsx` — fan-in 4 **and** fan-out 5 (the only module with both numbers non-trivial). It sits between `@vis.gl/react-maplibre`/maplibre-gl (external, stateful, hard to mock cleanly) and multiple consumers. Territory confirms it as a hands-on hotspot indirectly (7 commits on `RouteInfoPanel.tsx`, the sibling component). Changes here carry the highest blast radius of any component — a map-library upgrade or props contract change ripples to 4 dependents.
- `src/lib/apiProxy.ts` — fan-in 7, zero fan-out. Correctly a leaf: it's the one place HTTP calls to the backend proxy live, so anything doing data fetching depends on it. Good testability property (mock this one module and every consumer is isolated from network), but also means any change to it is a wide, quiet blast radius — nothing in the import graph forces a review of all 7 dependents.
- `src/app/my-routes/[id]/page.tsx` — fan-out 9, the highest of any file overall (edges out `RouteApp.tsx`). As a page-level component under App Router it's naturally an orphan from other first-party code (Next's router loads it), which means static analysis can't tell you who "uses" it — e2e (Playwright) is the only test type that meaningfully exercises this file end-to-end, consistent with `npm run e2e` already covering that route.

**Most suspicious modules**: `RouteApp.tsx` and `RouteMap.tsx` — both are hub nodes where import-graph centrality lines up with git-history hotness from `artifact-1-territory.md`. Any change here should default to component/integration tests over unit tests with heavy mocking.

## Rendered subgraph

`context/map/routeapp-subgraph.svg` — focused on `src/components/RouteApp.tsx` (`--focus`, not the full `webapp`), answering "what does the highest-fan-out component actually pull in, and could a change here ripple sideways." Generated via:

```
npx depcruise src --config .dependency-cruiser.cjs \
  --focus '^src/components/RouteApp\.tsx$' \
  --output-type dot | dot -T svg -o ../../context/map/routeapp-subgraph.svg
```

---

# Backend — ArchUnitNET + custom PowerShell script

dependency-cruiser has no .NET equivalent. Backend coverage here is two separate pieces, kept apart deliberately since they answer different questions: **ArchUnitNET** (pass/fail rule assertions, run as xUnit tests) for layer boundaries and cycles; a **custom PowerShell script** for the fan-in/fan-out metrics and rendered graph ArchUnitNET's rule-only API doesn't provide.

## Tool setup

Added `TngTech.ArchUnitNET.xUnit@0.13.4` to `src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj` and a new test file, `VeloRoute.Tests/Architecture/ArchitectureTests.cs`. Run with `dotnet test --filter "FullyQualifiedName~Architecture"` from `src/backend/` (no Postgres/Testcontainers needed — these tests only load the compiled assembly's type graph via Mono.Cecil, they don't touch the database, unlike the rest of the suite).

`src/backend/scripts/backend-dep-graph.ps1` reuses that same `ArchLoader`-built `Architecture` object rather than a separate console project — PowerShell (Core, cross-platform) can invoke .NET objects it loads at runtime by ordinary member access, no compile step needed to iterate. Run with `pwsh src/backend/scripts/backend-dep-graph.ps1` from `src/backend/` (rebuilds `VeloRoute.Tests` first; `-SkipBuild` reuses the existing build output; `-FocusNamespace` re-centers the rendered subgraph, default `VeloRoute.Routing`).

## Layer boundaries & cycles (ArchUnitNET)

The backend's minimal-API layout has four leaf namespaces off the `VeloRoute` composition root (`Program.cs`): `VeloRoute.Data` (EF Core entities + `AppDbContext`), `VeloRoute.Routing` (route-generation logic), `VeloRoute.Auth` (Clerk/claims helpers), `VeloRoute.Json` (shared converters). A manual grep for cross-namespace `using`/type references (`grep -rn "Data\.\|Routing\.\|Auth\." <namespace-dir>`) found **zero** hits in any direction before writing a single rule — ArchUnitNET's results below independently confirm that by construction rather than by inspection.

| Boundary checked | Result | Evidence | Why it matters | Territory link |
|---|---|---|---|---|
| `VeloRoute.Data` must not depend on `Routing` or `Auth` | **Holds** | `Data_Does_Not_Depend_On_Routing_Or_Auth` — pass | Entities/`AppDbContext` stay persistence-only; a routing-logic or auth change can't silently ripple into the EF model | `AppDbContext`/entities aren't individually top-line hot in territory, but `Data` sits directly behind the v2 save/library/share features the roadmap calls "done" |
| `VeloRoute.Routing` must not depend on `Data` or `Auth` | **Holds** | `Routing_Does_Not_Depend_On_Data_Or_Auth` — pass | Confirms route generation stays usable standalone for the fully-anonymous v1 flow (copilot-instructions: "still fully anonymous, nothing persisted") — a persistence or auth dependency here would be a scope leak | `Routing/` is the single hottest backend folder in the territory map (27 file-touches) and the one `context/foundation/test-plan.md` calls the highest-risk module (`LoopRouteGenerator`) — good news that it's structurally isolated |
| `VeloRoute.Auth` must not depend on `Data` or `Routing` | **Holds** | `Auth_Does_Not_Depend_On_Data_Or_Routing` — pass | Claims/Clerk helpers stay a cross-cutting concern usable from any endpoint, not coupled to one feature's persistence or routing code | Not itself a territory hotspot, but a violation here would have meant JWT/Clerk logic silently gating on a specific entity shape |
| `VeloRoute.Json` must not depend on `Data`, `Routing`, or `Auth` | **Holds** | `Json_Is_A_Foundation_Layer` — pass | `Optional<T>` and other shared converters stay reusable from any layer, including future ones — matches the copilot-instructions' own description of `Json/` as shared JSON converters | Low-churn folder in territory, consistent with a stable foundation layer |
| No cycles between top-level `VeloRoute.*` namespaces | **Holds** | `Top_Level_Namespaces_Are_Free_Of_Cycles` (`SliceRuleDefinition.Slices().Matching("VeloRoute.(*).**")`) — pass | A namespace-level cycle would mean two "layers" can't be understood independently — same property dependency-cruiser confirmed on the frontend | **Answers the question left open in the Frontend section**: territory's `Program.cs` ↔ `TestInfrastructure.cs` co-change (6 commits) turns out to be a one-way `Migrations`/`Data` reference from generated EF code, not a namespace cycle — the slice rule passing rules out the cycle interpretation, though it can't rule out a *file-level* two-way coupling between `Program.cs` and the test harness specifically (ArchUnitNET reasons about namespaces/types, not individual test-infra co-editing) |

All 5 tests pass — the backend, like the frontend, has a clean layered structure with no boundary violations or cycles at the namespace level. Given this is a 4-month-old codebase (per territory map), read this as "hasn't accumulated debt yet," not as a guarantee that stays true once the OSM-routing-quality work (v2-remaining, per `context/foundation/roadmap.md`) adds new shared modules to `Routing/`.

## Metrics & graph (custom script)

ArchUnitNET only answers yes/no on rules — no fan-in/fan-out, no rendered graph. Rather than pay for NDepend (whose diagrams are Windows-GUI-only anyway, per the earlier tool comparison), `backend-dep-graph.ps1` walks every first-party type's `Dependencies` and writes:

- `context/map/backend-metrics.json` — fan-in/fan-out/member-count per type (same shape as the frontend's fan-in/fan-out table), plus a `ReferencedByCompositionRoot` flag (see mitigation below).
- `context/map/routing-subgraph.dot` + `.svg` — an ego graph of every edge touching `VeloRoute.Routing` (the hottest backend folder per territory), rendered via the same `dot` binary used for the frontend subgraph.

**Scope note discovered while building this**: ArchUnitNET's loader excludes the top-level-statements `Program` type entirely (45 types loaded total; 0 of them named `Program`, vs. 34 after filtering out `VeloRoute.Migrations.*`) — it's dropped as compiler-generated before `Architecture.Types` is populated. `Program.cs` is the composition root wiring every minimal-API endpoint to `Routing`/`Data`/`Auth`, so **any type called only from an endpoint lambda in `Program.cs` shows undercounted fan-in here** — a 0 means "nothing else in the codebase depends on this," not "nothing depends on this."

**Mitigation**: `backend-dep-graph.ps1` supplements the Cecil-derived edges with a text scan of `Program.cs` — for every first-party type, does its short name appear as a whole word in `Program.cs`? A hit sets `ReferencedByCompositionRoot: true` on that type in `backend-metrics.json` and adds a dashed, distinctly-labelled `"text scan"` edge from a synthetic `Program.cs (composition root)` node in `routing-subgraph.svg`, so a `Program.cs`-only consumer no longer looks identical to a genuinely unused type. This is a source-text heuristic, not an IL-verified dependency — it's kept as a separate edge set rather than folded into `FanIn`/`FanOut`, and can false-negative (a type referenced only via a base/interface name Program.cs never spells out) or, in principle, false-positive (short name coincidentally appears in an unrelated identifier). The scan found **8** FanIn=0 types referenced by `Program.cs`: `LoopRouteGenerator`, `OpenRouteServiceClient`, `AppDbContext`, `OptionalJsonConverterFactory`, `ClerkClient`, `GpxSerializer`, `OpenRouteServiceOptions`, `RouteMetadataValidation`.

**Top fan-in** (34 first-party types, `Program.cs` excluded per above):

| Type | Fan-in | Fan-out | Members |
|---|---|---|---|
| `Routing.RouteCoordinate` | 9 | 0 | 18 |
| `Routing.RouteResult` | 7 | 6 | 31 |
| `Routing.SmoothnessCalculator` | 3 | 3 | 5 |
| `Routing.RoutingResult<T>` | 3 | 2 | 9 |
| `Routing.RouteWaySegment` | 3 | 2 | 24 |

**Top fan-out**:

| Type | Fan-out | Fan-in | Members |
|---|---|---|---|
| `Routing.LoopRouteGenerator` | 11 | **0** ⚠ (`ReferencedByCompositionRoot: true`) | 14 |
| `Routing.RouteResult` | 6 | 7 | 31 |
| `Routing.OpenRouteServiceClient` | 6 | **0** ⚠ (`ReferencedByCompositionRoot: true`) | 6 |
| `Routing.IOpenRouteServiceClient` | 5 | 2 | 3 |
| `Routing.PavedRatioCalculator` | 5 | 1 | — |
| `Data.AppDbContext` | 4 | **0** ⚠ (`ReferencedByCompositionRoot: true`) | 8 |

The three ⚠ rows are exactly the "called only from `Program.cs`" case flagged above — `LoopRouteGenerator` is the route-generation orchestrator wired to the generate-route endpoint, `OpenRouteServiceClient` is registered as `IOpenRouteServiceClient` via DI in `Program.cs`, `AppDbContext` is registered and injected the same way. Their real fan-in is "the whole request pipeline," just not visible to a type-graph tool that drops the entry point. All three carry `ReferencedByCompositionRoot: true` per the mitigation above, so this is no longer just an inference — the text scan independently confirms `Program.cs` is where their real consumers live. This reproduces, in a small concrete way, exactly the `Program.cs`-is-the-seam finding from `artifact-1-territory.md`'s co-change analysis (`Program.cs` ↔ `Routing/` ↔ `VeloRoute.Tests/Routing`, 12 commits) — the git-history signal and the (partial) import-graph signal agree on where the seam is, even though the import graph can't see the seam itself.

`RouteCoordinate` topping fan-in (9, zero fan-out) is the backend's `route.ts`-equivalent: a small value type nearly everything in `Routing` touches, and — like the frontend's `types/route.ts` — a true leaf with no outgoing first-party dependencies. `RouteResult` is the one type with both fan-in and fan-out in the double digits combined (7 in, 6 out): it's the shape returned by generation and consumed downstream (GPX serialization, API response mapping), so a change to its shape has the widest blast radius of anything actually visible to this analysis.

**Rendered subgraph**: `context/map/routing-subgraph.svg` — every first-party edge touching `VeloRoute.Routing` (50 IL-derived edges, solid, plus 7 dashed edges from the composition-root mitigation above), the backend counterpart to the frontend's `RouteApp.tsx` ego graph. Styling matches the frontend's dependency-cruiser dot output (`context/map/routeapp-subgraph.svg`) rather than graphviz defaults: Helvetica 9pt labels, rounded light-blue filled nodes grouped into per-namespace clusters (the backend's equivalent of dependency-cruiser's per-folder clusters), semi-transparent black edges. The composition-root text-scan edges reuse dependency-cruiser's own "external/uncertain module" red (`#c40b0a`) rather than a new color, since it already means the same thing in both graphs: not a verified first-party import. Regenerate with `pwsh src/backend/scripts/backend-dep-graph.ps1` (rebuilds `VeloRoute.Tests` first; pass `-SkipBuild` to reuse the existing build output, or `-FocusNamespace VeloRoute.Data`/`VeloRoute.Auth` to re-center the graph elsewhere).

---

## Caveats

- `tsPreCompilationDeps` plus the `@/*` alias resolution depends on `tsconfig.json` staying in sync; if the alias changes, re-verify the frontend config still resolves cleanly (`npx depcruise src --config .dependency-cruiser.cjs --output-type err-html` should report 0 violations before trusting a re-run).
- The ArchUnitNET rules are hand-picked to mirror the frontend's layer analogy (foundation → leaf namespaces around a composition root). They encode today's intent, not an enforced constraint from the framework — nothing stops a future PR from adding a `using VeloRoute.Data` inside `Routing/`; the tests exist specifically to catch that the next time `dotnet test` runs.
- SSH.NET NU1903 (moderate) surfaces in `dotnet build` output — pre-existing via `Testcontainers.PostgreSql`, confirmed unrelated to the ArchUnitNET addition (reproduces on `main` before this change too). Not addressed here; out of scope for a structure-mapping pass.
- `backend-dep-graph.ps1`'s `FanIn`/`FanOut` numbers exclude `Program.cs` itself (see "Backend → Metrics & graph" above); check `ReferencedByCompositionRoot` in `backend-metrics.json` before treating any 0-fan-in type as dead code — it's a regex-on-source-text heuristic, not IL-verified, so it can still miss an indirect reference (e.g. via an interface/base type Program.cs never names directly).
- The script rebuilds `VeloRoute.Tests` and loads its output DLLs via a runtime `AssemblyLoadContext.Resolving` hook rather than a fixed reference list — reasonable for an ad hoc analysis script, but it means adding a new NuGet dependency to either project doesn't require touching the script (it resolves whatever's already in the build output), which is a feature, not a bug, but worth knowing before assuming the resolver is an exhaustive allowlist.
