# Refactor Opportunities — Implementation Plan

## Overview

Close the `RouteResult` / `route.ts` contract drift (C1) — the one candidate `research.md`'s three-lens investigation classified as **accidental complexity with proven, currently-unguarded drift**, not a deliberate trade-off. Target shape: OpenAPI-driven codegen replaces the hand-mirrored TypeScript interface. Bundled with one independent, zero-risk quick-win (C7 — delete dead code) as the cheapest, most independent first step. All other ranked/rejected candidates (C4, C9, C2, C3, C5, C6, C8) are explicitly out of scope — see "What We're NOT Doing."

## Current State Analysis

- `RouteResult` (backend, `src/backend/VeloRoute/Routing/RouteResult.cs:3-13`) is a record with 3 constructor fields + 5 computed properties (8 fields total). `route.ts` (`src/frontend/src/types/route.ts:17-26`) hand-mirrors all 8 as a TypeScript interface, maintained by a manual "Touched: ..." commit-message checklist — the only enforcement.
- Three unchecked `res.json() as RouteResult`-family casts exist: `src/frontend/src/lib/routingApi.ts:13,43`, `src/frontend/src/components/RouteApp.tsx:46`. None validate the response shape at runtime.
- The only CI check touching this contract is `src/frontend/e2e/anonymous-flow.spec.ts:53-56`, which asserts on `distanceMeters`/`pavedRatio` only — 2 of 8 fields. A rename or drop of `smoothnessScore`, `overlapRatio`, `maxConsecutiveSharpTurns`, or `segments` is caught by nothing in CI today.
- `AddOpenApi()` is already registered (`Program.cs:18`) and emits `/openapi/v1.json`, but `app.MapOpenApi()` is gated inside `if (app.Environment.IsDevelopment())` (`Program.cs:123-132`), alongside Swagger UI and the `/auth/probe` diagnostic endpoint — so the spec is unreachable against the deployed backend.
- No codegen tooling (openapi-typescript, orval, etc.) and no runtime validator (zod, ajv, io-ts) exist anywhere in `src/frontend` today (confirmed by research's dependency/source grep).
- Dead code (C7): `IOpenRouteServiceClient.GetDirectionsAsync(start, end, ...)` — the 2-coordinate overload (`IOpenRouteServiceClient.cs:5-8`) — has zero callers anywhere; both the real client (`OpenRouteServiceClient.cs:19-23`) and the test fake (`TestInfrastructure.cs:77-81`) implement it only by delegating to the list-based overload, which is the only one ever actually called.

## Desired End State

- `/openapi/v1.json` is reachable in every environment (Swagger UI and `/auth/probe` remain dev-only).
- The frontend `RouteResult` type is generated from that spec, not hand-typed — `types/route.ts` re-exports the generated type so no other import site changes.
- A new backend integration test pins the full 8-field JSON shape of a `/routes/loop` response, added *before* the codegen cutover, and still green *after* it (regression guard).
- The 2-coordinate `GetDirectionsAsync` overload no longer exists in the interface, the real client, or the test fake.

**Verification:** `dotnet test` (backend), `npm test` (frontend), `npm run build` (frontend, verifies the generated types compile and downstream usages still typecheck), `npm run lint`, `npm run e2e` all pass; the anonymous-flow e2e continues to render distance/paved-ratio from the live loop-route response.

### Key Discoveries

- The gate for `/openapi/v1.json` and Swagger UI/`/auth/probe` is the *same* `if` block (`Program.cs:123-132`) — only `app.MapOpenApi()` needs to move outside it; Swagger UI and the probe endpoint stay dev-only.
- ASP.NET Core's default JSON casing (camelCase) already matches `route.ts`'s hand-written field names — no naming-policy mismatch expected between the generated schema and the existing interface.
- Existing backend JSON-shape tests use `JsonDocument.Parse(body)` + `doc.RootElement.GetProperty(...)` (`RouteQualityTests.cs`, `RouteLibraryTests.cs`, `DeleteRouteTests.cs`) — the new characterization test follows this established pattern, not a new one.
- `VeloRouteWebApplicationFactory` + `FakeOpenRouteServiceClient.Results.Enqueue(...)` (`TestInfrastructure.cs`) is the established integration-test harness (`LoopRouteIntegrationTests.cs` is the direct precedent) — reused as-is, untouched.

## What We're NOT Doing

- **C4** (resilience-untested ORS test double) — real correctness gap, but the underlying `TestInfrastructure.cs` ↔ `Program.cs` coupling it lives in is itself a sound, deliberate extraction (`5ddbb99`). Ranked #2, not #1; deferred to a future change.
- **C9** (`RouteApp.tsx` fan-out decomposition) — research's own verdict: "debt is speculative rather than demonstrated," no proven incident. Deferred.
- **C2** (redundant sharp-turn-flag recomputation) — already triaged by a prior implementation review; residual cost explicitly accepted as "cheap in absolute terms." Rejected from this ranking.
- **C3** (`RouteMetadataValidation.cs` location) — current placement is itself the result of a deliberate prior move to match this repo's folder-per-namespace convention; zero actual coupling problem. Rejected.
- **C5** (Kudu CI/deploy migration hardening) — real debt, but already stabilized through five reactive fixes with no new incident since, and is CI/ops script rather than application code. Rejected for now.
- **C6** (`Program.cs` monolithic composition root) — written into `copilot-instructions.md` at project inception and held without deviation across ~20+ commits; no incident ever tied to its size. Rejected.
- **C8** (`fetchRoutePreview()` / broken dev-only preview page) — confirmed broken but deliberately deferred as "harmless" 3.5 months ago; dev-only, zero user-facing impact. Left as a future opportunistic cleanup, not bundled here to keep this plan's blast radius to exactly the two items above.
- **Zod / runtime response validation** — considered as an alternative or complement to codegen; not pursued in this plan. Codegen removes the dual-source-of-truth problem structurally; runtime validation remains a separate, independent future option.
- **A generated HTTP client** (only generated *types* are in scope) — `routingApi.ts`'s hand-written `fetch` calls are kept; only the `RouteResult` shape becomes generated.

## Implementation Approach

Guard-first, cheapest-and-most-independent-first ordering:

1. Delete the dead overload (C7) — zero dependencies, zero risk, lands as its own reversible commit.
2. Add a characterization test that pins the *current* `/routes/loop` JSON response shape — before anything about the contract changes, per the "guard, not rebuild until you've guarded" principle. This test must still pass unchanged after the codegen cutover.
3. Expose the OpenAPI spec outside Development and introduce codegen tooling.
4. Cut over the frontend to the generated type at the 3 call sites; re-run the characterization test as the regression check.

Each phase is an independently committable, reversible step.

## Phase 1: Delete the dead 2-coordinate `GetDirectionsAsync` overload

### Overview

Removes the unused overload flagged as C7 — a backward-compatibility wrapper kept when the list-based overload was introduced (`5091af8`), never called by production or test code since. Independent of every other phase; done first because it is the cheapest, safest step.

### Changes Required

#### 1. Interface declaration

**File**: `src/backend/VeloRoute/Routing/IOpenRouteServiceClient.cs`

**Intent**: Remove the 2-coordinate `GetDirectionsAsync(RouteCoordinate start, RouteCoordinate end, ...)` signature (lines 5-8); keep the list-based overload and `GetRoundTripDirectionsAsync` unchanged.

**Contract**: Interface shrinks from 3 methods to 2.

#### 2. Real implementation

**File**: `src/backend/VeloRoute/Routing/OpenRouteServiceClient.cs`

**Intent**: Remove the delegating implementation (lines 19-23) that forwarded the 2-coordinate call to the list-based overload.

**Contract**: `OpenRouteServiceClient` implements only the 2 remaining interface members.

#### 3. Test fake

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: Remove `FakeOpenRouteServiceClient`'s matching delegating implementation (lines 77-81).

**Contract**: `FakeOpenRouteServiceClient` implements only the 2 remaining interface members; its 13 existing consumer test files are unaffected since none call the removed overload.

### Success Criteria

#### Automated Verification

- Backend builds cleanly: `dotnet build` (from `src/backend/`)
- All backend tests pass: `dotnet test` (from `src/backend/`)
- No remaining references: `grep -rn "GetDirectionsAsync(.*RouteCoordinate start, RouteCoordinate end" src/backend` returns nothing

---

## Phase 2: Characterization test — pin the current `RouteResult` JSON shape

### Overview

Adds a backend integration test that locks in the full 8-field JSON response shape of `/routes/loop` *before* Phase 3/4 touch anything about how that shape is produced or consumed. This is the guard-before-you-touch step: if the codegen cutover in Phase 4 silently changes a field name or drops a field, this test catches it — closing exactly the gap research identified (4 of 8 fields checked by nothing in CI today).

### Changes Required

#### 1. New characterization test

**File**: `src/backend/VeloRoute.Tests/Routing/RouteResultContractTests.cs` (new file)

**Intent**: Assert that a successful `/routes/loop` response's JSON body contains all 8 `RouteResult` fields (`geometry`, `distanceMeters`, `segments`, `pavedRatio`, `smoothnessScore`, `overlapRatio`, `qualityWarning`, `maxConsecutiveSharpTurns`) with the expected `JsonValueKind` for each, using the same `VeloRouteWebApplicationFactory` + `FakeOpenRouteServiceClient.Results.Enqueue(...)` harness `LoopRouteIntegrationTests.cs` already uses.

**Contract**: Follows the existing `JsonDocument.Parse(body)` + `doc.RootElement.GetProperty("fieldName").ValueKind` assertion style used in `RouteQualityTests.cs`/`RouteLibraryTests.cs` — one `GetProperty` + `ValueKind` assertion per field, all 8 fields in a single `[Fact]`. This test must pass unmodified after Phase 4's cutover; a passing re-run there is the regression check.

### Success Criteria

#### Automated Verification

- New test passes: `dotnet test --filter RouteResultContractTests` (from `src/backend/`)
- Full backend suite still passes: `dotnet test` (from `src/backend/`)

**Implementation Note**: Pause here for confirmation before Phase 3 — this test is the safety net the rest of the plan depends on.

---

## Phase 3: Expose the OpenAPI spec and generate frontend types

### Overview

Un-gates `/openapi/v1.json` outside Development and introduces `openapi-typescript` to generate a TypeScript type for `RouteResult` (and its neighboring schemas) directly from the backend's own contract. No backend business logic changes; Swagger UI and the `/auth/probe` diagnostic endpoint remain dev-only.

### Changes Required

#### 1. Un-gate the OpenAPI document endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Move `app.MapOpenApi();` (currently line 125, inside the `if (app.Environment.IsDevelopment())` block spanning lines 123-132) out to run unconditionally, so `/openapi/v1.json` is reachable in every environment. This exposes the API schema unauthenticated in production — accepted, since the app's core API surface (`/routes/loop`, `/routes/gpx`) is already anonymous and public by design.

**Contract**: `app.UseSwaggerUI(...)`, `app.UseHttpsRedirection()`, and the `/auth/probe` `MapGet` stay inside the `IsDevelopment()` block exactly as today — only the `MapOpenApi()` call moves. No new configuration, no new package.

#### 2. Add codegen tooling

**File**: `src/frontend/package.json`

**Intent**: Add `openapi-typescript` as a devDependency and a new script to (re)generate types from the running backend's spec.

**Contract**: New script `"gen:route-types": "openapi-typescript http://localhost:5098/openapi/v1.json -o src/types/generated/route-api.ts"`. Generated output is committed to the repo (like `package-lock.json`) and regenerated on demand when the backend contract changes — it is not part of `npm run build` or CI, keeping this phase's blast radius to exactly the frontend + `Program.cs`'s OpenAPI registration, as research's blast-radius analysis specified.

#### 3. Automated regression guard for non-Development reachability

**File**: `src/backend/VeloRoute.Tests/Routing/OpenApiExposureTests.cs` (new file)

**Intent**: Prove `/openapi/v1.json` is reachable outside Development — the actual fix this phase makes — as a durable, CI-enforced test, not only a manual curl check. Without this, a future accidental re-gating regresses silently.

**Contract**: `Program.cs:105-112`'s config-validation block runs whenever `!builder.Environment.IsDevelopment()`, requiring `Clerk:Authority`, `Clerk:AllowedAzp`, `Clerk:SecretKey` to be set or the host throws before it can even build — so this test must supply those via in-memory config, not just flip the environment:

```csharp
await using var factory = new VeloRouteWebApplicationFactory()
    .WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Production");
        b.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Clerk:Authority"] = "https://test.clerk.accounts.dev",
            ["Clerk:AllowedAzp"] = "test-client",
            ["Clerk:SecretKey"] = "test-secret",
        }));
    });
var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

#### 4. Generate the types file

**File**: `src/frontend/src/types/generated/route-api.ts` (new, generated — not hand-edited)

**Intent**: Run `npm run gen:route-types` against a locally running backend (`dotnet run`, per the existing dev workflow) to produce the initial generated file, then commit it.

**Contract**: Exports an OpenAPI `components` type map; `RouteResult` is accessed as `components['schemas']['RouteResult']`.

### Success Criteria

#### Automated Verification

- Backend builds and serves the spec: `dotnet build` (from `src/backend/`); with `dotnet run` running, `curl -f http://localhost:5098/openapi/v1.json` succeeds
- Non-Development reachability is CI-enforced: `dotnet test --filter OpenApiExposureTests` (from `src/backend/`)
- Generated file compiles: `npx tsc --noEmit` (from `src/frontend/`)
- Full frontend suite still passes: `npm test` (from `src/frontend/`)
- Lint passes: `npm run lint` (from `src/frontend/`)

#### Manual Verification

- `curl http://localhost:5098/openapi/v1.json` against a `dotnet run` instance with `ASPNETCORE_ENVIRONMENT=Production` confirms the spec is reachable outside Development (the actual bug this phase fixes)
- Swagger UI at `/swagger` and `GET /auth/probe` both still 404/return-nothing-useful outside Development (unchanged dev-only gating)

---

## Phase 4: Cut over to the generated `RouteResult` type

### Overview

Replaces the hand-written `RouteResult` interface in `types/route.ts` with a re-export of the generated type, so the 3 existing call sites need no changes beyond the import continuing to resolve. Closes the drift structurally: the backend's own contract is now the single source of truth.

### Changes Required

#### 1. Re-export the generated type

**File**: `src/frontend/src/types/route.ts`

**Intent**: Replace the hand-written `export interface RouteResult { ... }` (lines 17-26) with a type alias sourced from the generated file, so every existing `import type { RouteResult } from '@/types/route'` continues to work unchanged.

**Contract**:
```ts
import type { components } from './generated/route-api';
export type RouteResult = components['schemas']['RouteResult'];
```
All other interfaces in `route.ts` (`RouteCoordinate`, `RouteWaySegment`, `RouteGeometry`, etc.) are untouched — only `RouteResult` moves to being derived.

#### 2. No changes needed at the 3 cast sites

**Files**: `src/frontend/src/lib/routingApi.ts:13,43`, `src/frontend/src/components/RouteApp.tsx:46`

**Intent**: These sites already write `res.json() as RouteResult` / `as Promise<RouteResult>` — since `RouteResult` now resolves to the generated type via the same import path, no edit is required here. Confirm at review time that the generated type's field names/optionality match what these call sites already assume (e.g. `qualityWarning: boolean`, not `boolean | undefined`, given the backend never omits it).

#### 3. Known blast-radius sites — check, not edit

**Files**: `src/frontend/e2e/fixtures/route.ts:18-38` (`loopRouteResponse: RouteResult = {...}`), `src/frontend/src/components/RouteInfoPanel.test.tsx:11-23` (`makeRoute()` builder)

**Intent**: Both hand-construct a full `RouteResult`-shaped object literal, not just consume the type — if the generated type's shape differs even slightly from the hand-written interface it replaces (optional vs. required field, readonly array), one or both will fail `tsc --noEmit` or `npm test`. This is expected-and-handled by this phase's own success criteria, not a surprise — if either fails, adjust the literal to match the generated shape; do not change the generated type to match the literal.

**Contract**: No change expected under the happy path (generated shape == hand-written shape); treat any failure here as a normal part of Phase 4's verification, not a blocker requiring plan revision.

### Success Criteria

#### Automated Verification

- Frontend typechecks against the new type: `npx tsc --noEmit` (from `src/frontend/`)
- Full frontend suite passes: `npm test` (from `src/frontend/`)
- Frontend build succeeds: `npm run build` (from `src/frontend/`)
- Lint passes: `npm run lint` (from `src/frontend/`)
- Backend characterization test from Phase 2 still passes, unmodified: `dotnet test --filter RouteResultContractTests` (from `src/backend/`)
- Full e2e suite passes: `npm run e2e` (from `src/frontend/`)

#### Manual Verification

- Generate a route in the running app (`npm run dev` + backend running); confirm the map renders, distance/paved-ratio/smoothness display correctly, and no console errors appear from a type mismatch
- Diff the generated `RouteResult` type against the deleted hand-written interface once, by eye, to confirm no field was silently dropped or renamed in the generated version

**Implementation Note**: After this phase's automated verification passes, pause for manual confirmation before considering the change complete.

---

## Testing Strategy

### Unit Tests

- Phase 1: existing backend suite (no new unit tests needed — deletion only)
- Phase 2: new `RouteResultContractTests.cs`, backend
- Phase 4: existing frontend `RouteInfoPanel.test.tsx` and related component tests continue to run against the generated type unchanged

### Integration Tests

- Phase 2's characterization test is itself an integration test (via `VeloRouteWebApplicationFactory`), and doubles as Phase 4's regression guard

### Manual Testing Steps

1. Run the full anonymous flow (start point → generate → map renders → GPX export) locally after Phase 4, confirming no regression
2. Confirm `/openapi/v1.json` is reachable with `ASPNETCORE_ENVIRONMENT=Production` set locally, and that Swagger UI/`/auth/probe` remain unreachable in that same configuration

## Performance Considerations

None — this plan changes type-generation and test coverage only; no runtime code path changes.

## Migration Notes

Not applicable — no data migration. The generated types file is a new, committed source file; no existing data or deployed state changes.

## References

- Related research: `context/changes/refactor-opportunities/research.md` (candidates C1, C7; ranking rationale; rejected candidates C2-C6, C8-C9)
- Prior analysis: `context/changes/loop-route-generation-analysis/research.md`
- Existing integration-test precedent: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`
- Existing JSON-shape assertion precedent: `src/backend/VeloRoute.Tests/Routing/RouteQualityTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Delete the dead 2-coordinate `GetDirectionsAsync` overload

#### Automated

- [ ] 1.1 Backend builds cleanly: `dotnet build`
- [ ] 1.2 All backend tests pass: `dotnet test`
- [ ] 1.3 No remaining references to the removed overload

### Phase 2: Characterization test — pin the current `RouteResult` JSON shape

#### Automated

- [ ] 2.1 New test passes: `dotnet test --filter RouteResultContractTests`
- [ ] 2.2 Full backend suite still passes: `dotnet test`

### Phase 3: Expose the OpenAPI spec and generate frontend types

#### Automated

- [ ] 3.1 Backend builds and serves the spec (`dotnet build` + `curl` against running instance)
- [ ] 3.2 Non-Development reachability is CI-enforced: `dotnet test --filter OpenApiExposureTests`
- [ ] 3.3 Generated file compiles: `npx tsc --noEmit`
- [ ] 3.4 Full frontend suite still passes: `npm test`
- [ ] 3.5 Lint passes: `npm run lint`

#### Manual

- [ ] 3.6 `/openapi/v1.json` reachable outside Development
- [ ] 3.7 Swagger UI and `/auth/probe` remain dev-only

### Phase 4: Cut over to the generated `RouteResult` type

#### Automated

- [ ] 4.1 Frontend typechecks: `npx tsc --noEmit`
- [ ] 4.2 Full frontend suite passes: `npm test`
- [ ] 4.3 Frontend build succeeds: `npm run build`
- [ ] 4.4 Lint passes: `npm run lint`
- [ ] 4.5 Backend characterization test still passes unmodified: `dotnet test --filter RouteResultContractTests`
- [ ] 4.6 Full e2e suite passes: `npm run e2e`

#### Manual

- [ ] 4.7 Live app: generate a route, confirm map/distance/paved-ratio/smoothness render correctly, no console errors
- [ ] 4.8 Eye-diff generated type against deleted hand-written interface for silently dropped/renamed fields
