---
date: 2026-09-11T15:56:25+02:00
researcher: Łukasz Orawiec
git_commit: 04e1334ee3d7e7731ae5e7c227c30b9f0d028486
branch: refactor-opportunities
repository: velo-route
topic: "Refactor opportunities: which technical-debt findings from loop-route-generation-analysis are worth fixing, in what target shape, in what order"
tags: [research, codebase, technical-debt, refactor-ranking, routing, ci-deploy, contract-drift, verified]
status: complete
last_updated: 2026-09-11
last_updated_by: Łukasz Orawiec
verified_commit: 04e1334ee3d7e7731ae5e7c227c30b9f0d028486
---

# Research: Refactor opportunities

**Date**: 2026-09-11T15:56:25+02:00
**Researcher**: Łukasz Orawiec
**Git Commit**: `04e1334ee3d7e7731ae5e7c227c30b9f0d028486`
**Branch**: `refactor-opportunities`
**Repository**: velo-route

## Research Question

`context/changes/loop-route-generation-analysis/research.md` (and its prior, `context/map/repo-map.md`) documented technical debt and structural risk without saying which of it is worth fixing, in what target shape, or in what order. This research closes that gap: every problem those two documents note is listed and classified — **KANDYDAT** if fixing it would change code structure, otherwise kept only as feasibility/cost input (missing tests, stale docs, organizational risk). Each candidate is then investigated on three axes — current shape (with evidence), historical intentionality (conscious constraint vs. accidental complexity), and migration feasibility — by three parallel exploration-only sub-agents. No code was changed. No decision was made here; this ends in a ranked proposal for a separate planning session.

## Candidate audit

**KANDYDAT:**

| # | Problem | Source |
|---|---|---|
| C1 | `RouteResult` hand-mirrored in `route.ts`, no codegen, no runtime validation — proven drift (commit `3985e83`) | research.md §Debt-2 |
| C2 | Sharp-turn-flag scan runs 3x independently per response (`ToMetrics` once + `RouteResult.SmoothnessScore` + `RouteResult.MaxConsecutiveSharpTurns`) | research.md §4a |
| C3 | `RouteMetadataValidation.cs` lives in `Routing/`, framed by prior research as belonging to save/edit-route | research.md Architecture Insights |
| C4 | `TestInfrastructure.cs` ↔ `Program.cs` coupling; `FakeOpenRouteServiceClient` bypasses the real resilience handler entirely | research.md §1 + repo-map §3/§4.2 |
| C5 | Kudu migration step in CI/deploy — 5 consecutive targeted fixes, no tool coverage | repo-map §4.3 |
| C6 | `Program.cs` monolithic composition root — no controllers, all DI in one file | repo-map §4.5 |
| C7 | `IOpenRouteServiceClient.GetDirectionsAsync(start,end,...)` 2-coord overload — prod-unused, test-only | research.md Unknowns |
| C8 | `fetchRoutePreview()` → `/routes/preview` — dev page calls a route the backend doesn't have | research.md Unknowns |
| C9 | `RouteApp.tsx`/`RouteMap.tsx` high fan-out hubs + `RouteMap`'s coupling to a stateful external lib | repo-map §4.4 |

**NOT KANDYDAT** (feasibility/cost input only — none of these change code structure by themselves):
N1 `/routes/loop` input-validation guard clauses untested · N2 ORS-error-code switch branches `2009/2010/2004` untested · N3 `OrsHttpExecutor` HTTP-error path untested · N4 resilience/circuit-breaker handler never exercised in CI · N5 no frontend test for the `/api/routes/loop` proxy/`RouteApp`/`routingApi` · N6 `seed`'s effect on bearing untested · N7 `loop-route-algorithm.md` doc body stale (superseded by its own addendum) · N8 single-contributor/bus-factor (organizational, not code).

No candidate required redesigning a business concept rather than code structure — all nine stayed in scope for this research.

## Per-candidate findings

### C1 — `RouteResult` / `route.ts` contract drift

**Current shape (evidence).** Backend `RouteResult` is a record with 3 constructor fields (`Geometry`, `DistanceMeters`, `Segments`) plus 5 computed properties (`PavedRatio`, `SmoothnessScore`, `OverlapRatio`, `QualityWarning`, `MaxConsecutiveSharpTurns`) — `src/backend/VeloRoute/Routing/RouteResult.cs:3-13`. The frontend interface hand-mirrors all 8 fields — `src/frontend/src/types/route.ts:17-26 (report: 1-26)` — and they currently match exactly. Three unchecked `res.json() as RouteResult` casts exist: `src/frontend/src/lib/routingApi.ts:13,43` and `src/frontend/src/components/RouteApp.tsx:46`. No codegen, no zod/ajv/io-ts anywhere in `src/frontend` — confirmed by dependency and source grep. `AddOpenApi()` is registered (`Program.cs:18`) but gated behind `app.Environment.IsDevelopment()` (`Program.cs:123-132`) — not available against the deployed backend — and `RouteResult.cs` has zero XML doc-comments, so even if exposed, a codegen pass would carry no field descriptions.

**Guardrail reality (evidence).** The only CI check that would catch drift is `src/frontend/e2e/anonymous-flow.spec.ts:53-56`, which asserts rendered text depending on `distanceMeters`/`pavedRatio` only. A rename of `smoothnessScore`, `overlapRatio`, `maxConsecutiveSharpTurns`, or `segments` is caught by **nothing** in CI — `RouteInfoPanel.test.tsx` builds its own fixture matching the hand-written interface, so it can't detect drift either.

**Intentionality verdict: accidental complexity.** No ADR or design doc ever weighed and rejected codegen or a runtime validator — grepped across `context/foundation` and `context/changes`, nothing found. `RouteResult.cs` and `route.ts` were created hours apart in the same original change (`routing-api-wiring`, commits `b79408e`/`52f2dcb`) — split by implementation phase, not neglect — and every later field addition (`3985e83`, `df173b5`) touched both files in the same commit with a manual "Touched: ..." checklist as the only enforcement. The project's own prior research already flags this as an open question rather than a settled trade-off (`context/changes/loop-route-generation-analysis/research.md:227`). Verdict: nobody decided against tooling here; it was simply never introduced.

**Feasibility.** No existing seam — this is a net-new abstraction either way. First prerequisite: make `/openapi/v1.json` available outside Development (currently gated at `Program.cs:123-132`) if pursuing spec-driven codegen; no backend change needed to start a zod-at-the-boundary pilot instead. Either path is incremental and reversible (one route can adopt it first, e.g. `generateLoopRoute`).

### C2 — Redundant sharp-turn-flag computation

**Current shape (evidence).** `LoopRouteGenerator.ToMetrics` computes `SmoothnessCalculator.ComputeSharpTurnFlags` once per candidate and feeds it into both scoring calls — `LoopRouteGenerator.cs:78-88` — but that cached array lives only in the local `CandidateMetrics` struct used for ranking. `RouteResult.SmoothnessScore` (`RouteResult.cs:9`) and `RouteResult.MaxConsecutiveSharpTurns` (`RouteResult.cs:12`) each independently re-run the full scan when read — confirmed 3 independent call sites total, firing on every `Results.Ok(result.Value)` response (`Program.cs:408`).

**Intentionality verdict: conscious constraint, with an explicitly accepted residual gap.** `context/archive/2026-08-05-route-quality-tuning/plan.md:367-394` designed the shared-flags helper specifically to avoid duplicated trig. A post-implementation review (`impl-review.md`, finding F1) caught the redundancy, and it was **partially** fixed in `ccab43e` by introducing the `CandidateMetrics`/`ToMetrics` caching — but the same review explicitly documents the remaining `RouteResult`-property recomputation as unresolved ("full elimination would need Fix A's memoization") rather than an oversight. This is a knowingly-deferred trade-off, not accidental complexity.

**Feasibility.** No existing seam for the remainder: `RouteResult` has no constructor/factory that accepts pre-computed flags — closing it needs a new abstraction (e.g., a factory that takes the already-computed metrics). Guardrail tests exist and pin exact values (`SpikeDetectorTests.cs:23,39,57`; threshold-style checks in `RouteQualityTests.cs:226-227`), so a refactor that preserves output values while cutting call count is safe against the existing suite. Cost of the debt itself is low — the original research already called this "cheap in absolute terms at current route lengths."

### C3 — `RouteMetadataValidation.cs` in `Routing/`

**Current shape (evidence).** Validates only `name`/`tags` (route metadata), not geometry — `RouteMetadataValidation.cs:9-11,23-60` — and its own file comment states it is deliberately "shared by the save and edit paths so they cannot drift apart" (`:3-6`). Used at exactly two production call sites, both outside loop generation: the save-route and edit-route endpoints (`Program.cs:185,261`). Zero type coupling to `RouteResult`/ORS/loop-generation code — it shares only the `Routing` namespace by folder placement, nothing structural.

**Intentionality verdict: conscious constraint — but not for the reason prior research assumed.** The file was originally created at the project root (`88ae903`), exactly per its plan (`context/archive/2026-09-08-edit-route/plan.md:74`). A post-implementation review (finding F9) flagged that placement as violating this repo's folder-per-namespace convention (`Auth/`, `Data/`, `Routing/`) — not a domain-boundary argument — and the fix (`69e5105`) explicitly moved it into `Routing/` "to match the folder-per-namespace convention." So its current home is the result of a deliberate convention-following move, not an accident, and not evidence it was meant to model loop-generation concerns.

**Feasibility.** ArchUnitNET's 4 namespace-boundary rules (`VeloRoute.Tests/Architecture/ArchitectureTests.cs:18-77`) would not break either way — the class has no cross-namespace dependency. A move to a new namespace (e.g., a dedicated `Routes/` slice for save/edit) is a clean move-and-rename (3 call sites total: `Program.cs:185,261` plus two test files), but it requires inventing a namespace slice that doesn't exist today, against a convention the codebase has otherwise followed consistently.

### C4 — `TestInfrastructure.cs` ↔ `Program.cs` coupling

**Current shape (evidence).** `TestInfrastructure.cs` bundles `TestJwtFactory`, `RouteTestHelpers`, `FakeOpenRouteServiceClient`, `FakeClerkClient`, and the `VeloRouteWebApplicationFactory` harness (`TestInfrastructure.cs:20-204`). `ConfigureWebHost` finds the real `IOpenRouteServiceClient`/`IClerkClient` registrations by `ServiceType` reflection and swaps in the fakes (`:150-188`) — required because those registrations are inline top-level statements in `Program.cs`, not named extension methods. The real client is wrapped in `AddStandardResilienceHandler` (retry + circuit breaker, `Program.cs:38-58`); `FakeOpenRouteServiceClient` is a bare `ConcurrentQueue`-backed fake with no such wrapping — the resilience pipeline is never exercised by any test.

**Intentionality verdict: conscious constraint.** The shared harness was created by an explicitly-labeled refactor (`5ddbb99`, "extract shared test infrastructure") specifically so multiple test files could reuse the factory/fake instead of each declaring `file sealed` copies — documented directly in `context/archive/2026-06-20-security-privacy-guards/plan-brief.md:34`. The `Program.cs:468` `public partial class Program {}` marker is the standard, unavoidable seam `WebApplicationFactory<Program>` requires.

**Feasibility.** 13 test files currently depend on the shared fake's simple queue-based API — a direct change to `FakeOpenRouteServiceClient`'s shape risks all of them. The resilience-parity gap is real (production retry/circuit-breaker behavior is genuinely untested, not just an abstraction nicety), and the safe incremental path is **additive**: introduce a second, narrower fake or `HttpMessageHandler`-based double for resilience-specific tests, leaving the existing 13-file fake untouched. First prerequisite step: decide the new fake's shape (message-handler-level, so it can sit behind the same `AddStandardResilienceHandler` pipeline) before writing the first resilience-behavior test.

### C5 — Kudu migration step in CI/deploy

**Current shape (evidence).** All Kudu logic (VFS PUT of the migration bundle, `/api/command` execution, secret-scrubbing, exit-code checks) is inline bash inside one `run: |` step in `.github/workflows/backend.yml:49-106` — no separate script file, no dry-run, no lint/shellcheck gate. The only related CI check is `dotnet ef migrations has-pending-model-changes` (`:27-31`), which validates the migration exists, not that the Kudu mechanics work.

**Intentionality verdict: conscious constraint.** Kudu was chosen with the alternative explicitly rejected in writing: `context/archive/2026-09-09-ci-deploy-hardening/plan-brief.md:22` — a direct Postgres-firewall/IP-allowlist path was "infeasible" (6,980 CIDRs). The risk was pre-declared, not discovered after the fact (`plan-brief.md:66`: "hasn't been validated end-to-end... documented manual fallback exists"). Each of the five follow-up fixes ties to one diagnosed, named failure (shell-invocation tokenizing, VFS If-Match conflict, base64 padding, cookie persistence, secret-leak in logs) — iterative hardening in response to real incidents, not organic drift.

**Feasibility.** No current guardrail exercises this path before a real deploy. The repo already has a precedent for extracting CI-adjacent logic to a standalone script (`src/backend/scripts/backend-dep-graph.ps1`), so extraction to a parameterized script is a natural, low-novelty first step — and a prerequisite for any dry-run/lint gate, since inline YAML heredocs can't be tested as-is. Note: this candidate is CI/ops code, not application code — include with that caveat.

### C6 — `Program.cs` monolithic composition root

**Current shape (evidence).** 468 lines; 14 endpoints (report: ~18) (`/health`, `/auth/sync`, `/account`, `/routes` CRUD, share, `/routes/loop`, `/routes/gpx`) are inline `app.Map*` calls in top-level statements at `Program.cs:129,139,142,153,180,209,224,241,284,302,344,364,380,416`, alongside all DI/service registration. No extension-method grouping (`MapXEndpoints`-style) exists anywhere in the codebase, and no `Extensions/`/`Endpoints/` folder exists.

**Intentionality verdict: conscious constraint, held without deviation.** "Minimal API pattern: endpoints are registered directly in `Program.cs`, not in controller classes" was written into `.github/copilot-instructions.md` at project inception (`3e3a169`), immediately after the initial scaffold — codifying the template default as a rule, not letting it drift. `git log -S "ControllerBase"` across all history returns zero hits: the rule has held through ~20+ feature commits.

**Feasibility.** Splitting into extension methods (`MapRouteEndpoints`, etc.) would not violate the stated rule (which forbids controllers, not grouping) and the `public partial class Program {}` seam required by the `WebApplicationFactory`-based test pattern (82 references; report said "82-usage `WebApplicationFactory<Program>`" — almost all are via the `VeloRouteWebApplicationFactory` subclass, only 4 are the literal `WebApplicationFactory<Program>` generic instantiation) is preserved either way. But no convention for such grouping exists yet in this codebase — the first step is a naming/grouping design decision, not a mechanical extraction. No incident or bug has been tied to the current shape; the risk is speculative (repo-map flags it on commit-touch centrality alone).

### C7 — Dead 2-coordinate `GetDirectionsAsync` overload

**Current shape (evidence).** Declared in `IOpenRouteServiceClient.cs:5-8`; both the real client (`OpenRouteServiceClient.cs:19-23`) and the test fake (`TestInfrastructure.cs:77-81`) implement it only by delegating to the list-based overload. A full repo-wide grep for `GetDirectionsAsync` finds exactly two actual invocations, and both call the **list** overload (`LoopRouteGenerator.cs:63`; `OpenRouteServiceClientTests.cs:80`) — the 2-coordinate overload itself has zero callers anywhere, production or test.

**Intentionality verdict: conscious constraint at origin, unrevisited since.** The 2-coordinate signature was the *original* method (`b79408e`); when the list-based overload was added, the original was deliberately kept as a delegating convenience wrapper rather than deleted (`5091af8`, commit message: "implement multi-waypoint overload; delegate existing method") — a backward-compatibility choice, not duplication by accident. No later commit or doc revisited whether to remove it once it stopped being called (unknown — not found in history).

**Feasibility.** Zero-risk deletion: no test exercises it, so no test needs adapting. Remove 3 declarations (interface + 2 implementations); nothing else changes.

### C8 — `fetchRoutePreview()` / `/routes/preview` / `app/dev/page.tsx`

**Current shape (evidence, refining the prior research's "unconfirmed" status to confirmed).** No `/routes/preview` endpoint exists anywhere in `Program.cs` (grep confirms zero matches). The only consumer is `src/frontend/src/app/dev/page.tsx`, gated by `if (process.env.NODE_ENV !== 'development') notFound()` (`:5`) so it never runs outside development. In development, the fetch does execute and 404s against the real backend, caught by the page's own `try/catch` and rendered as an error message (`:14-23`) — not a crash, but a permanently broken diagnostic page. Neither `npm run build`/`tsc`, `npm test`, nor the Playwright e2e spec touches this path — nothing in CI would notice if it were deleted or further broken.

**Intentionality verdict: conscious constraint.** Both the backend stub and the frontend caller were created together as an explicit end-to-end wiring smoke test (`context/archive/2026-05-30-routing-api-wiring/plan.md:187,275`). The backend stub was deliberately removed the same day once the real `/routes/loop` endpoint landed (`24fe250`: "remove `/routes/preview` dev stub, add Swagger UI"). Crucially, the plan for that same commit explicitly chose to leave the frontend side: `context/archive/2026-05-30-loop-route-generation/plan.md:376` — "Keeps the existing `fetchRoutePreview()` for now (it's harmless)." That "for now" has persisted roughly 3.5 months (still flagged in `context/changes/loop-route-generation-analysis/research.md:215` and `context/archive/2026-09-09-anonymous-flow-e2e/research.md:123`) unaddressed, but its origin was a deliberate, reasoned deferral, not neglect.

**Feasibility.** No other file references `fetchRoutePreview`/`app/dev` — deletion is call-graph-safe. No CI gate protects either choice (delete vs. wire up a real preview endpoint), so verification is manual either way.

### C9 — `RouteApp.tsx` / `RouteMap.tsx` fan-out and map-library coupling

**Current shape (evidence).** `RouteApp.tsx` (87 lines) mixes client state (`selectedPoint`, `routeResult`, `isLoading`, `error`, an abort-controller ref), the fetch/abort/error-parsing logic itself, derived map-pin state, and layout composition of 4 children — `RouteApp.tsx:22-86`. `RouteMap.tsx` (87 lines) is a pure presentational wrapper around `@vis.gl/react-maplibre`/`maplibre-gl` with direct imperative calls (`resize`, `fitBounds`, `flyTo`) through a typed `MapRef` and a hardcoded style URL — no adapter layer between the component and the library. `RouteMap.tsx` has 3 consumers (`RouteApp.tsx`, `my-routes/[id]/page.tsx`, `r/[token]/page.tsx`); `RouteApp.tsx` has 1 (`app/page.tsx`).

**Intentionality verdict: split.** The `RouteMap` module boundary itself is deliberate and present since creation (`80b611e`) — `dynamic(() => import('./RouteMap'), { ssr: false })` is forced by maplibre-gl needing browser APIs unavailable during SSR, a real technical constraint, not an organizational choice. But the *specific* fan-out shape (state + fetch + derivation + layout all inline in `RouteApp.tsx`, no extracted hook) was never discussed in any `context/changes/**`/`context/archive/**` doc — when new responsibility (GPX download) was added later, it was deliberately routed into the sibling `RouteInfoPanel.tsx` instead of growing `RouteApp.tsx` further, which shows restraint, but the underlying fan-out was never itself revisited. Verdict on the SSR boundary: conscious. Verdict on the current internal shape of `RouteApp.tsx`: unknown.

**Feasibility.** No dedicated test file exists for `RouteApp.tsx` at all — only the coarse `anonymous-flow.spec.ts` e2e spec exercises it indirectly (map-visible assertion, not logic-level). `RouteMap.tsx` does have its own test file. The natural extraction point (a `useRouteGeneration` hook pulling out the fetch/abort/state logic from `handleGenerate`, `RouteApp.tsx:28-58`) does not exist yet — it would need to be created, not just exposed. First prerequisite step: add `RouteApp.test.tsx` component-level coverage for the generate/error/abort paths before attempting any decomposition, since e2e alone is too coarse to safely guard a hook extraction.

## Refactor opportunities

Ranked by (debt cost it removes) vs. (cost/risk of the incremental first step), using evidence from all three lenses.

### 1. C1 — Close the `RouteResult` / `route.ts` contract drift

**Current → target shape.** Hand-mirrored, unvalidated interface → either (a) types generated from `/openapi/v1.json`, or (b) a zod (or equivalent) runtime validator at the frontend's HTTP boundary (`routingApi.ts`). Either removes the silent-failure mode; they are not mutually exclusive but (b) is cheaper to pilot.

**Why it earns this rank.** This is the only candidate in the set that is (a) **accidental**, not a deliberate trade-off someone already weighed, (b) currently causing a **proven** failure mode (commit `3985e83`'s hand-sync pattern), and (c) has a **confirmed, non-hypothetical CI blind spot**: 4 of 8 response fields (`smoothnessScore`, `overlapRatio`, `maxConsecutiveSharpTurns`, `segments`) are checked by nothing in CI today. Debt cost is high and silent; every other candidate's debt is either already mitigated, deliberately accepted, or speculative.

**Blast radius.** Narrow but structurally certain: `routingApi.ts`, `RouteApp.tsx`, `RouteInfoPanel.tsx`, `types/route.ts` on the frontend; `Program.cs`'s OpenAPI registration on the backend if the codegen path is chosen. No backend logic changes either way.

**Incremental path sketch.** Pilot on one call site (`generateLoopRoute`) first: add a zod schema mirroring `RouteResult`, validate the response, keep the existing TS interface as the compile-time type (schema and interface can coexist during migration). Only after that proves out, consider replacing the hand-written interface with an inferred `z.infer<>` type, and separately evaluate exposing `/openapi/v1.json` outside Development for a complementary generated-client path.

**First prerequisite step.** Add zod as a frontend dependency and write the schema for `RouteResult` next to `types/route.ts`; no backend change required to start.

### 2. C4 — Give the ORS test double resilience parity (or an explicit resilience-specific double)

**Current → target shape.** `FakeOpenRouteServiceClient` bypasses `AddStandardResilienceHandler` entirely (zero tests exercise retry/circuit-breaker behavior) → an additional, narrower fake or `HttpMessageHandler`-level double that sits behind the same resilience pipeline the production client uses, exercised by a small new test suite.

**Why it earns this rank.** This is the candidate with the clearest **correctness** stake among the nine: the resilience handler is real production code (`Program.cs:38-58`) guarding every ORS call, and it is currently untested end-to-end by anything in CI. Unlike C1, the underlying coupling (`TestInfrastructure.cs` ↔ `Program.cs`) is itself a deliberate, well-reasoned extraction (not itself broken) — the actionable debt is narrower and more specific: the resilience gap it enabled.

**Blast radius.** If done additively (new double, not a rewrite of the existing fake), the blast radius is contained to new test files — the 13 files currently depending on `FakeOpenRouteServiceClient`'s queue-based API are untouched.

**Incremental path sketch.** Build a second fake that implements resilience-relevant failure modes (timeout, transient 5xx, rate-limit 429) at the `HttpMessageHandler` level so it can be registered behind the same `AddStandardResilienceHandler(...)` pipeline as production; write a first test asserting the circuit breaker actually opens/retries under the same conditions the production config expects.

**First prerequisite step.** Decide the new double's exact shape (message-handler-level vs. a resilience-aware wrapper around the existing fake) before writing the first test — this is a small design decision, not a large migration.

### 3. C9 — Add test coverage to `RouteApp.tsx`, then decompose its fan-out

**Current → target shape.** All state, fetch/abort logic, and derived state inline in `RouteApp.tsx` with zero dedicated tests → a `useRouteGeneration`-style hook carrying the fetch/abort/error/loading state, covered by component-level tests, with `RouteApp.tsx` reduced to composition/layout.

**Why it earns this rank.** This is the product's core interaction path (start point → generate → render), it is a repo-map-flagged risk zone on structural grounds (fan-out 8, hardest-to-mock external dependency), and — unlike C6 (which is guarded by an explicit, unbroken repo convention) — nothing here contradicts a stated project rule. But its evidence is weaker than C1/C4's: no proven incident, only absence of test coverage and un-revisited fan-out. It ranks third, not first, because the debt is speculative rather than demonstrated.

**Blast radius.** `RouteApp.tsx` and its one consumer (`app/page.tsx`); `RouteMap.tsx` is unaffected (already has its own tests and a justified SSR boundary).

**Incremental path sketch.** Phase 1 (safe, non-breaking): add `RouteApp.test.tsx` covering `handleGenerate`'s success/error/abort paths against the current implementation. Phase 2 (only after phase 1 lands): extract the hook, re-run the same tests unchanged as a regression guard.

**First prerequisite step.** Write `RouteApp.test.tsx` against the current (pre-refactor) implementation — this is valuable on its own even if the extraction is deferred.

## Candidates considered and rejected (from the top ranking)

- **C2 — redundant sharp-turn-flag computation.** Real, but already triaged: a prior implementation review found this exact issue, fixed the larger part of it, and explicitly accepted the remainder as "cheap in absolute terms" rather than an oversight. Fixing the residual needs a new `RouteResult` construction path for marginal, already-acknowledged benefit. Low priority, not rejected outright — a reasonable pickup if bundled with unrelated `Routing/` work, not worth its own change.
- **C3 — `RouteMetadataValidation.cs` location.** The prior research's framing ("belongs to save/edit-route flow") doesn't survive investigation: the file's current placement in `Routing/` is itself the result of a deliberate move to match this repo's folder-per-namespace convention, not an accident. Moving it again would mean inventing a new namespace slice against a convention the codebase has followed everywhere else, for a class with zero actual coupling problems. Reject.
- **C5 — Kudu migration CI hardening.** Real debt, but (a) already stabilized through five reactive fixes with no new incident since, and (b) is CI/ops script, a weaker fit for "code structure" than the other candidates. Worth a future pass (extracting the inline bash to a script is a low-novelty first step, per the `backend-dep-graph.ps1` precedent already in this repo) but not competitive with C1/C4/C9 right now.
- **C6 — `Program.cs` monolithic composition root.** The strongest "conscious constraint" verdict in the set: written into `copilot-instructions.md` at project inception and held without a single deviation across ~20+ feature commits. No incident has ever been tied to its size. Splitting it would fight an explicit, working project convention for a risk that is speculative (commit-touch centrality, not a documented pain point). Reject for now.
- **C7 — dead 2-coordinate `GetDirectionsAsync` overload.** Confirmed zero-risk, trivial deletion — but also trivial value (a few harmless lines, kept deliberately as backward-compat convenience when the overload was superseded). Fine as an opportunistic quick-win alongside any other `Routing/` change; not worth ranking on its own.
- **C8 — `fetchRoutePreview()` / `/routes/preview` dev page.** Confirmed broken (always 404s), but explicitly and deliberately deferred as "harmless" when the backend stub was removed, gated to development only, and touches nothing user-facing. Low debt cost, low fix cost — a fine opportunistic cleanup, not a priority.

## Claim verification (ast-grep)

Verified the 13 structural claims the ranking and classification above rest on: field/method counts, "computes once, but recomputes elsewhere" patterns, call-site counts, and mirrored type pairs. Method: `ast-grep 0.45.3` for every structural pattern; every zero result from ast-grep confirmed by a separate plain `grep`. No result undermines the position of any of the three ranked candidates (C1, C4, C9) — all their claims are confirmed exactly or with a minor line-number correction. Two corrections concern a candidate already rejected from the ranking (C6) and additionally *strengthen* the rejection rationale (`Program.cs` is smaller/less fragmented than the report suggested), so nothing here needs a "to be decided at the planning stage" annotation.

| # | Claim | Verdict | Evidence (file:line) | Method (pattern/rule) |
|---|---|---|---|---|
| 1 | `RouteResult.cs` has 3 constructor fields + 5 computed properties (8 total); `route.ts` mirrors all 8 fields of the `RouteResult` interface | **refined** (field count correct, line range wrong) | `RouteResult.cs:3-13`; `route.ts:17-26 (report: 1-26)` — lines 1-16 are neighboring types (`RouteCoordinate`/`RouteWaySegment`/`RouteGeometry`), not the `RouteResult` interface itself | Read + manual field comparison (ast-grep does not distinguish TS `interface` members without a dedicated rule) |
| 2 | Exactly 3 unchecked `res.json() as RouteResult` casts (or similar types) | **confirmed** | `routingApi.ts:13`, `routingApi.ts:43`, `RouteApp.tsx:46` | `ast-grep -p 'res.json() as $TYPE'` (per file) |
| 3 | Zero uses of zod/ajv/io-ts in `src/frontend` | **confirmed (zero)** | absent from `package.json` and from sources — zero hits | `ast-grep -p "import $$$ from 'zod'"` (0 results) + `grep -rn "from 'zod'\|'ajv'\|'io-ts'"` (exit 1, confirms zero) |
| 4 | `ComputeSharpTurnFlags` is called from exactly 3 independent places (`ToMetrics` once + `RouteResult.SmoothnessScore` + `RouteResult.MaxConsecutiveSharpTurns`, each recomputing from scratch) | **confirmed** | `LoopRouteGenerator.cs:80`; `SmoothnessCalculator.cs:5`; `SpikeDetector.cs:11` | `ast-grep -p 'ComputeSharpTurnFlags($$$ARGS)'` (bare call, 1 hit) + `ast-grep -p 'SmoothnessCalculator.ComputeSharpTurnFlags($$$ARGS)'` (qualified, 2 hits) — two patterns needed because ast-grep treats bare and qualified calls as different AST nodes |
| 5 | `RouteMetadataValidation.Validate` is used in exactly 2 production places | **confirmed** | `Program.cs:185`, `Program.cs:261` | `ast-grep -p 'RouteMetadataValidation.Validate($$$ARGS)'` |
| 6 | 13 test files depend on shared `TestInfrastructure.cs` types | **confirmed** | 13 files in `VeloRoute.Tests/Routing/` (incl. `EditRouteTests.cs`, `ShareRouteTests.cs`, `RouteQualityTests.cs`, ...) | `grep -rl` on the names of the 5 shared types, `wc -l` = 13 |
| 7 | "82-usage `WebApplicationFactory<Program>`" as the test pattern (C4/C6) | **refined** | Literal `WebApplicationFactory<Program>` = **4** occurrences; the broader `WebApplicationFactory` (mostly via the `VeloRouteWebApplicationFactory` subclass) = **82** occurrences — 82 is correct but was attributed to the wrong pattern in the report | `ast-grep -p 'WebApplicationFactory<Program>'` (4) + `grep -rn "WebApplicationFactory"` (82) |
| 8 | Zero endpoint grouping via extension methods (`this IServiceCollection`/`this WebApplication`, `MapXEndpoints` style) in the backend | **confirmed (zero)** | none in `src/backend/VeloRoute` | `ast-grep -p 'public static $RET $NAME(this IServiceCollection $$$)'` and the same for `this WebApplication` (0 results) + `grep -rn "static.*this IServiceCollection\|MapXEndpoints\|Endpoints(this"` (exit 1, confirms zero) |
| 9 | `Program.cs` has 468 lines | **confirmed** | `Program.cs` | `wc -l` (a numeric fact, not an AST pattern — there is no meaningful ast-grep pattern for "file line count") |
| 10 | "~18 endpoints" registered inline in `Program.cs` | **refined** | **14** (report: ~18) — `Program.cs:129,139,142,153,180,209,224,241,284,302,344,364,380,416` | `ast-grep -p 'app.$METHOD($$$ARGS)'` filtered to `MapGet/MapPost/MapPatch/MapDelete/MapPut` |
| 11 | The 2-argument `GetDirectionsAsync(start, end, ...)` overload has no real call outside its own implementation (dead code) | **confirmed** | Declaration: `IOpenRouteServiceClient.cs:5-8`. The only "calls" are the overload's own delegation to the list variant: `OpenRouteServiceClient.cs:23`, `TestInfrastructure.cs:81`. The only external test (`OpenRouteServiceClientTests.cs:80`) calls the list variant (`waypoints: IReadOnlyList<RouteCoordinate>`), not the 2-argument one | `ast-grep -p '$X.GetDirectionsAsync($A, $B, $$$REST)'` — the pattern is type-blind and also matched the list variant; resolving it required reading the signatures (`IOpenRouteServiceClient.cs`) and the test's arguments directly |
| 12 | No `/routes/preview` endpoint in `Program.cs` | **confirmed (zero)** | `Program.cs` — zero hits for "preview" | `ast-grep -p 'app.$METHOD($PATH, $$$)'` filtered by "preview" (0) + `grep -in "preview" Program.cs` (exit 1, confirms zero) |
| 13 | `RouteMap.tsx` has exactly 3 consumers; `RouteApp.tsx` has exactly 1 | **confirmed** | RouteMap: `RouteApp.tsx:11`, `my-routes/[id]/page.tsx:12`, `r/[token]/page.tsx:9` (all via `dynamic(() => import(...))`); RouteApp: `app/page.tsx` | `grep -rn "dynamic(() => import" \| grep -i routemap"` — the static pattern `ast-grep -p "import $$$ from '$PATH'"` did not match (the import is wrapped in a `dynamic()` call, not a top-level `import` statement) |

**Methodology note:** for claims #1, #7, #10 the line number/range was corrected or the pattern description refined in the report text above (format "X (report: Y)"), per the correction-trail rule. The "Refactor opportunities" sections and the intentionality verdicts in "Per-candidate findings" are unchanged — no correction undermines them.

## Code References

- `src/backend/VeloRoute/Routing/RouteResult.cs:3-13` — wire contract, computed properties (C1, C2)
- `src/frontend/src/types/route.ts:17-26` (report: 1-26) — hand-mirrored frontend contract (C1)
- `src/frontend/src/lib/routingApi.ts:13,43` and `src/frontend/src/components/RouteApp.tsx:46` — unchecked JSON casts (C1)
- `src/frontend/e2e/anonymous-flow.spec.ts:53-56` — the only CI check touching the contract, and only 2 of 8 fields (C1)
- `src/backend/VeloRoute/Routing/LoopRouteGenerator.cs:78-88` — `ToMetrics` cached-flags pattern (C2)
- `src/backend/VeloRoute/Routing/SmoothnessCalculator.cs:5`, `SpikeDetector.cs:10-11` — the two independent re-scans (C2)
- `src/backend/VeloRoute/Routing/RouteMetadataValidation.cs:3-6,9-11,23-60` — metadata-only validator (C3)
- `src/backend/VeloRoute/Program.cs:185,261` — its two production call sites (C3)
- `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:72-109,150-188` — shared harness, fake wiring (C4)
- `src/backend/VeloRoute/Program.cs:38-58` — real resilience handler registration, never exercised by tests (C4)
- `.github/workflows/backend.yml:27-31,49-106` — Kudu migration step, inline (C5)
- `src/backend/scripts/backend-dep-graph.ps1` — precedent for extracting CI-adjacent scripts (C5)
- `src/backend/VeloRoute/Program.cs:129-429` — all endpoint registrations inline (C6)
- `.github/copilot-instructions.md` — the explicit minimal-API-no-controllers rule (C6)
- `src/backend/VeloRoute/Routing/IOpenRouteServiceClient.cs:5-8`, `OpenRouteServiceClient.cs:19-23` — dead overload (C7)
- `src/frontend/src/lib/routingApi.ts:6-14`, `src/frontend/src/app/dev/page.tsx:1-24` — broken dev-only preview path (C8)
- `src/frontend/src/components/RouteApp.tsx:22-86`, `RouteMap.tsx` — fan-out, no `RouteApp` test file (C9)

## Architecture Insights

- Every candidate that looked like an accident at first read except C1 turned out, on git archaeology, to be a **documented, deliberate** decision — several caught and partially addressed by this project's own implementation-review discipline (C2, C3). That discipline is a real asset: it means most of this repo's rough edges are already known and triaged by the person who introduced them, not silent drift.
- The one genuine "nobody decided this" gap (C1) is exactly the kind the M4L3 lesson's connascence framing predicts: long-distance connascence of meaning, invisible to any import graph or namespace-cycle check, caught only by discipline and a coarse e2e assertion.
- Two candidates (C4, C9) share a pattern: the *structural* decision around them was deliberate and sound, but a *specific consequence* of that decision (resilience-path untested; `RouteApp.tsx` untested) was left unaddressed. The fix in both cases is additive (new test/new double), not a rewrite of the sound part.

## Historical Context (from prior changes)

- `context/changes/loop-route-generation-analysis/research.md` — the base report this research answers; every candidate traces back to its §Debt/§4a/Architecture Insights sections or to `context/map/repo-map.md` §4.
- `context/archive/2026-08-05-route-quality-tuning/plan.md` and its `reviews/impl-review.md` — origin and partial fix of C2.
- `context/archive/2026-09-08-edit-route/plan.md` and `reviews/impl-review.md` (finding F9) — origin and resolution of C3's file placement.
- `context/archive/2026-06-20-security-privacy-guards/plan-brief.md` — origin of the shared test harness behind C4.
- `context/archive/2026-09-09-ci-deploy-hardening/plan-brief.md` — origin and risk-declaration for C5.
- `context/archive/2026-05-30-routing-api-wiring/plan.md` and `context/archive/2026-05-30-loop-route-generation/plan.md` — origin and deliberate deferral of C8.

## Related Research

- `context/map/repo-map.md` — the wide-scan prior this and the base deep-focus report were scoped from.
- `context/changes/loop-route-generation-analysis/research.md` — the deep-focus report this research is a direct sequel to.

## Open Questions

- C1: codegen from `/openapi/v1.json` vs. a zod runtime validator vs. both — a planning-time choice, not resolved here.
- C4: exact shape of the resilience-aware test double (message-handler-level fake vs. wrapper) — a small design decision for the planning session.
- C9: whether `RouteApp.test.tsx` alone is sufficient before extraction, or whether the hook extraction should be scoped into the same change — planning-time sequencing question.
- C5/C6: not ranked in the top 3 now, but worth revisiting if a new Kudu incident occurs (C5) or if `Program.cs` crosses some size/complexity threshold that starts causing real friction (C6) — no such trigger has occurred yet.
