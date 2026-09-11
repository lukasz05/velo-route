# Refactor Opportunities — Plan Brief

> Full plan: `context/changes/refactor-opportunities/plan.md`
> Research: `context/changes/refactor-opportunities/research.md`

## What & Why

Close the `RouteResult` / `route.ts` contract drift (candidate C1): the frontend hand-mirrors the backend's 8-field response type with zero codegen and zero runtime validation, and a rename of 4 of those 8 fields is caught by nothing in CI today. Research's three-lens investigation (current shape, git-archaeology intentionality, migration feasibility) ranked this the top candidate — it's the only one of nine audited debt items that turned out to be genuine accidental complexity rather than a deliberate, already-triaged trade-off. Bundled with one independent, zero-risk quick-win: deleting a dead, zero-caller method overload (C7).

## Starting Point

`RouteResult` (backend record, 8 fields) is hand-mirrored as a TypeScript `interface` in `route.ts`, kept in sync only by a manual "Touched: ..." commit checklist. Three call sites cast `res.json()` to that interface with no runtime check. The backend already emits an OpenAPI spec (`/openapi/v1.json`) but only in Development — unreachable against the deployed backend.

## Desired End State

The frontend `RouteResult` type is generated from the backend's own OpenAPI spec, not hand-typed — the backend becomes the single source of truth. A new backend test pins the full response shape before the cutover and still passes after it. A confirmed-dead code path is gone.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Plan scope | C1 + C7 only, not all ranked candidates | Lesson material explicitly instructs "one option (plus possibly a cheap quick gain)," not one phase per candidate | Plan |
| C1 target shape | OpenAPI codegen (types only, not a generated client) | Single source of truth beats a second hand-authored schema; user's explicit choice over the cheaper zod-pilot alternative | Plan |
| C4 / C9 | Deferred entirely | Research ranked them #2/#3 with weaker (C4: narrower; C9: speculative) evidence than C1's proven drift | Research |
| C7 bundling | Included as Phase 1 (cheapest, most independent, do first) | Zero-risk, zero-dependency deletion — "fine as an opportunistic quick-win" per research | Research |
| C2/C3/C5/C6/C8 | Excluded, one-line reasons in "What We're NOT Doing" | Research already rejected each with evidence (deliberate decisions, no incident, or deferred-as-harmless) | Research |
| Characterization test | New backend integration test, `JsonDocument`-based, matches existing test style | Guard-before-you-touch: pins the currently-unguarded 4-of-8-field gap before the cutover touches it | Plan |

## Scope

**In scope:**
- Delete the dead 2-coordinate `GetDirectionsAsync` overload (interface + 2 implementations)
- Add a characterization test pinning the current `RouteResult` JSON shape
- Un-gate `/openapi/v1.json` outside Development (Swagger UI/`/auth/probe` stay dev-only)
- Add `openapi-typescript` codegen, generate and commit the types file
- CI job that fails when the committed generated types no longer match the backend's spec
- Replace the hand-written `RouteResult` interface with a re-export of the generated type

**Out of scope:**
- C4 (resilience-untested ORS test double), C9 (`RouteApp.tsx` decomposition), C2/C3/C6 (rejected — deliberate/no-incident), C5 (Kudu CI hardening — stabilized), C8 (broken dev-preview page — deferred, zero user impact)
- A generated HTTP client (types only)
- Runtime validation (zod) as an alternative to codegen

## Architecture / Approach

Backend already exposes an OpenAPI document; the fix is exposure (un-gate one line) plus a one-time codegen step whose output is committed like a lockfile — not regenerated on every build, but checked for staleness by a CI job. Frontend's `route.ts` re-exports the generated `RouteResult` type so no import path across the codebase changes. A backend characterization test brackets the change: written before touching anything, verified unmodified afterward.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Delete dead overload (C7) | Interface + implementations shrink by one confirmed-zero-caller method | None — zero risk, confirmed by research |
| 2. Characterization test | Backend test pinning all 8 `RouteResult` JSON fields | Test itself could be too loose to catch real drift — mitigated by asserting every field's `ValueKind`, not just presence |
| 3. Expose spec + generate types | `/openapi/v1.json` reachable outside dev; `route-api.ts` generated; CI fails on stale types | Naming-policy mismatch between generated schema and hand-written interface (expected none — both default to camelCase) |
| 4. Cut over | `route.ts` re-exports generated type; 3 call sites need no edits | Generated type's optionality could differ subtly from what call sites assume — caught by `tsc --noEmit` + manual eye-diff |

**Prerequisites:** Local backend (`dotnet run`) reachable to generate types against; no other change in flight touching `Program.cs`'s Development gate or `route.ts`.
**Estimated effort:** ~1 session across 4 phases; each phase is one reversible commit.

## Open Risks & Assumptions

- Assumes ASP.NET Core's default camelCase JSON policy applies to `/openapi/v1.json`'s schema output the same way it applies to actual responses — not independently verified before this plan, confirmed at Phase 3 review.
- Generated types are committed, not regenerated on every build — a future backend field change still requires a manual `npm run gen:route-types` + commit, but the `API Contract Freshness` CI job regenerates against the running backend and fails on any diff, so a forgotten regeneration blocks the deploy instead of letting the type go stale silently.

## Success Criteria (Summary)

- `/routes/loop` response shape is enforced by a passing backend test before and after the cutover
- Frontend's `RouteResult` type is generated, not hand-written; `dotnet test`, `npm test`, `npm run build`, `npm run lint`, `npm run e2e` all green
- Dead 2-coordinate overload no longer exists anywhere in the codebase
