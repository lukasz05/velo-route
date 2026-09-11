---
title: "VeloRoute — Module 4 Architectural Report (10xArchitect)"
created: 2026-09-11
type: architect-report
---

# VeloRoute — Module 4 Architectural Report

Single repository throughout (VeloRoute, `10x-architect-artifacts` branch, HEAD `c9eb8a0` at research time). All four artifacts below are grounded in that repo; no MISSING artifacts.

## 1. Projects described

**VeloRoute** — free road-cycling loop-route planner. Next.js 15/React 19/TypeScript frontend + ASP.NET Core .NET 10 minimal-API backend, split over HTTP, Postgres/EF Core, OpenRouteService (ORS) for routing, Clerk for auth. ~4 months old (2026-05-20 → 2026-09-11), single contributor (3 git identities, not a team). Scale: solo/low-QPS MVP, now in v2 (auth + route library + sharing) on top of a shipped v1 (anonymous generation, no persistence).

| Artifact | File | Repo |
|---|---|---|
| L2 Repo map | `context/map/repo-map.md` | VeloRoute |
| L3 Feature research | `context/changes/loop-route-generation-analysis/research.md` | VeloRoute |
| L4 Refactor plan | `context/changes/refactor-opportunities/plan.md` | VeloRoute |
| L5 Domain notes | `context/domain/01-domain-distillation.md`, `02-invariant-aggregate-refactor.md`, `03-anti-corruption-layer.md` | VeloRoute |

## 2. Project map (from L2)

- Structurally clean: zero import cycles on either side (dependency-cruiser on the frontend, ArchUnitNET namespace rules on the backend); all declared layer-boundary rules hold.
- Git history reveals a coupling the import graph can't see: `Program.cs` (composition root, no controllers) co-changes with `TestInfrastructure.cs` (shared test harness) on >50% of its own commits — a debt, not a maintained abstraction.
- Top risk zones: `LoopRouteGenerator`/`Routing/` (highest fan-out=11, top-rated risk in the test plan), the `Program.cs` seam itself, and the CI/deploy Kudu migration step (five consecutive targeted fixes, no static-analysis coverage at all — outside both JS/TS and the .NET type graph).
- Single contributor = no bus factor; "who to ask" resolves to "which commit trail to read."

## 3. Feature analysis (from L3)

Investigated `POST /routes/loop` — the product's core flow — because L2 flagged `Routing/`/`LoopRouteGenerator` as the top risk zone with the least test visibility.

**Feature overview:** frontend form submit → Next.js proxy → backend validates range/coords → `LoopRouteGenerator` fires 6 concurrent ORS requests (3 round-trip + 3 DIY-sector, ast-grep confirmed) → scores on overlap/paved/smoothness/turn-spikes → strict-then-fallback selection → `RouteResult` serialized as the HTTP response, hand-mirrored in the frontend's `types/route.ts`.

**Technical debt (top 3):**
1. **Untested seam, not untested logic.** 28 test methods cover the scoring algorithm; zero cover the endpoint's input-validation guards or the ORS-error-code→HTTP-status switch (`Program.cs:398-404`) — ast-grep confirmed codes `"2009"/"2010"/"2004"` never appear in any test.
2. **Hidden, silent contract coupling.** `RouteResult` is the wire contract, hand-mirrored with no generated client and no runtime validation; commit `3985e83` proves the drift risk is real (both sides hand-edited together, enforced only by a commit-message checklist and a CI-only e2e run).
3. **Redundant computation, newly surfaced.** ast-grep found `SmoothnessCalculator.ComputeSharpTurnFlags` has a second, independent call site (`SpikeDetector.cs:11`) — the winning route gets the full bearing scan three times per response, not once.

## 4. Refactoring plan (from L4)

**Target:** close the `RouteResult`/`route.ts` contract drift (C1, from a 9-candidate ranked list) via OpenAPI codegen replacing the hand-mirrored TypeScript interface, bundled with one zero-risk quick win (C7 — delete a dead overload). Chosen because research classified it as proven, unguarded drift, not a deliberate trade-off.

**Explicitly NOT doing:** the other 7 ranked candidates — each rejected for a stated reason (already deliberate, no proven incident, ops-script rather than app code) — keeping this plan's blast radius to two items.

**Phases (one line each):**
1. Delete the dead 2-coordinate `GetDirectionsAsync` overload from the interface, real client, and test fake (C7) — automated: `dotnet build`, `dotnet test`, grep finds no remaining references.
2. Add a characterization test (`RouteResultContractTests`) pinning all 8 fields of the `/routes/loop` JSON response before the contract is touched — automated: `dotnet test`.
3. Un-gate `/openapi/v1.json` outside Development, add `openapi-typescript`, and commit the generated types — automated: `OpenApiExposureTests`, `tsc --noEmit`, `npm test`, `npm run lint`, and a CI job that fails on stale generated types; manual: spec reachable under `ASPNETCORE_ENVIRONMENT=Production`, Swagger UI still dev-only.
4. Re-point `route.ts`'s `RouteResult` to the generated type — automated: `tsc --noEmit`, `npm test`, `npm run build`, `npm run lint`, `npm run e2e`, and the Phase 2 test passing unmodified; manual: generate a route in the running app, diff the generated type against the deleted interface by eye.

## 5. Domain according to DDD (from L5)

**Ubiquitous language (5 of 13 extracted concepts):** *Loop route* (start=end, within a user km range), *Segment overlap* (self-retrace fraction), *Route (saved)* (owned by exactly one User), *Share* (revocable, unauthenticated, live read-through, dies with its Route), *Quality warning* (a non-PRD, implementation-level signal).

**Most important model-vs-code divergences:** (1) the PRD states "≤10% segment-overlap" as a hard, unmodified v1 guarantee — the code enforces it only as a *soft preferred tier*, silently falling back to any candidate regardless of overlap; a 10–40% band produces no signal to the caller at all. (2) The PRD's Business Logic Changes section narrates OSM scenic/low-traffic + cyclist-POI preference in the present tense as already governing generation — zero implementation exists (confirmed by full-tree grep); this is a tracked-but-parked gap (`S-07`), not a silent one.

**Invariant #1 and its aggregate:** the overlap ceiling (I1) — simultaneously high core-value, inconsistently spread across 5 files (two disagreeing constants: `0.10` vs. an undocumented `0.40`), and declared-but-not-enforced. Guardian: a pure, stateless `LoopRouteQualityPolicy` — the sole place `MaxOverlapRatio` is declared and the sole gate a candidate must pass, throwing a named exception instead of degrading silently. Counter-example: `I5` (ownership) is spread just as wide but *correctly* enforced — spread alone isn't the risk signal, spread plus inconsistent enforcement is.

**Anti-Corruption Layer:** the worst leak is Clerk (`@clerk/nextjs`) on the frontend UI — 6 files import it directly, with the same "get a token, build a Bearer header" sequence hand-reconstructed 11 times, and Clerk's own `UserResource` type read straight into JSX with no domain type between. Worse than MapLibre/NetTopologySuite (each confined to 1 file) and worse than Clerk's *own backend* usage, already isolated behind an `IClerkClient` port. The roadmap shows the provider already swapped once (Entra → Clerk) with zero frontend abstraction in place. Fix: an `AuthSession` value object + narrow `AuthSessionPort`, with one `clerkAdapter.ts` as the sole SDK-touching file. Verifiable: `grep -rl "@clerk/nextjs" src/frontend/src` narrows from 8 files to 3.

## 6. Decisions that belong to me

The agent proposed three defaults I overrode. The L2 structure prompt assumed a single JS workspace, so the agent mapped only the frontend with dependency-cruiser; I rejected that because every top risk zone on the map — `Routing/`, `LoopRouteGenerator`, `Program.cs` — lives in the .NET backend, and chose ArchUnitNET over NDepend because it is free, runs as xUnit tests, and exposes a type model I could script fan-in/fan-out metrics from. For the `RouteResult` drift, L4 research recommended piloting a zod runtime schema; I chose OpenAPI codegen instead, because zod would add a third hand-maintained copy of a contract already proven to drift (`3985e83`), whereas codegen makes the backend the single source and moves the check to compile time, with a CI step that fails on a stale generated file. Publishing `/openapi/v1.json` outside Development I treat as no cost: it reveals nothing the browser's own requests to the backend don't already expose. L5 flags the Clerk leak as the worst ACL gap, yet I sequence the contract fix first: the overlap-invariant refactor already plans to remove `QualityWarning` from `RouteResult`, so the contract changes again soon, while the Clerk leak costs nothing until a provider swap that no roadmap item plans.
