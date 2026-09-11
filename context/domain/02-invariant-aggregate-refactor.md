---
title: "VeloRoute — Invariant-Aggregate Refactor Plan: Loop-Route Overlap Ceiling"
created: 2026-09-11
type: refactor-plan
---

# VeloRoute — Invariant-Aggregate Refactor Plan

Scope: this document is a **refactor plan**, not an implementation. No production code is modified as part of producing it.

## Step 0 — Context

Builds on `context/domain/01-domain-distillation.md` (same session's prior artifact), which already surveyed `context/foundation/prd-v2.md`, `context/foundation/roadmap.md`, `.github/copilot-instructions.md`, and the backend under `src/backend/VeloRoute/`. This document re-verifies the specific code citations used below (re-read at the paths and line numbers given, not carried over from memory) and extends the analysis with the frontend layer and a formal three-axis classification, per KROK2's requirement.

Stack and layer map (unchanged from the distillation): Next.js 15 / React 19 frontend, ASP.NET Core .NET 10 minimal-API backend (no controllers, no service layer — endpoint bodies in `Program.cs` call directly into `Routing/` and `Data/`), Postgres via EF Core, OpenRouteService (ORS) for route computation. The business logic under review here lives entirely in `src/backend/VeloRoute/Routing/` (generation-time, not persisted) plus its two client-facing echoes: the wire contract in `RouteResult.cs` and the frontend display in `RouteInfoPanel.tsx`.

Also checked: `context/changes/refactor-opportunities/plan.md` (a separate, already-reviewed plan, status: planned but not yet implemented) which targets the `RouteResult` / `route.ts` wire-contract drift (candidate "C1") via OpenAPI codegen. This matters because `RouteResult` is the exact type this plan's diagnosis is about — see the sequencing note in Step 5.

## Step 1 — Business invariants identified

Re-derived from `prd-v2.md` and code (source citations inline; this list narrows the distillation's Step 3 table to invariants relevant to route-generation quality, since Step 2 below shows this is where the highest-value drift lives):

1. **I1 — Overlap ceiling.** "A generated loop route retraces at most 10% of itself." Source: "with at most 10% segment repetition" — `prd-v2.md:160`, restated as the **unmodified v1 rule** carried into v2, and as "≤10% segment-overlap constraint" in the v1 system overview — `prd-v2.md:20`.
2. **I2 — Paved-surface preference.** "Route generation prefers paved/low-traffic roads." Source: "on paved or low-traffic roads" — `prd-v2.md:160, 20`.
3. **I3 — Distance-range compliance.** "A generated route's distance falls within the caller's `[min, max]` km range." Source: "generate a loop route (start point + km range)" — `prd-v2.md:50`.
4. **I4 — Scenic/POI preference (v2).** "Route generation prefers OSM-tagged scenic/low-traffic segments and proximity to cyclist POIs, without violating I3." Source: `prd-v2.md:126-131, 162`. Already known absent from the codebase (verified again in Step 3) and already tracked as parked (`roadmap.md:34`, `S-07`) — excluded from Step 2's ranking on that basis, consistent with the prior distillation.
5. **I5 — Route ownership.** "Every saved `Route` belongs to exactly one `User`; only the owner may act on it." Source: `prd-v2.md:103, 174`. Included for contrast in Step 2 as the strongest-enforced invariant in the codebase.

## Step 2 — Classification and selection

Each invariant scored on three independent axes, as required: (a) how core to the product's value proposition, (b) how spread across files/layers the rule's *implementation* is, (c) whether it is genuinely enforced, merely declared, or violable in practice.

| Invariant | (a) Core-ness | (b) Spread across layers | (c) Enforcement status |
|---|---|---|---|
| **I1 — overlap ≤ 10%** | **High.** Named as one of exactly two problems v2 exists to solve is I4 (routing quality), and I1 is the pre-existing half of that same "routing quality" story — the PRD frames it as a hard, already-shipped guarantee, not a preference. | **High, and inconsistent.** The "one rule" is implemented as **two different numeric thresholds in two different files**: `PrimaryOverlapThreshold = 0.10` in `LoopRouteGenerator.cs:9` and `Ceiling = 0.40` in `OverlapDetector.cs:12`. A third file (`RouteResult.cs:11`) derives a boolean from the second threshold only. A fourth and fifth location (`route.ts:24`, `RouteInfoPanel.tsx:104-110`) render that boolean to the end user with no visibility into the actual ratio or the documented 10% figure. | **Declared, not enforced.** See Step 3 — no path rejects a candidate for exceeding 10%; the 40% ceiling only logs. |
| **I2 — paved preference** | Medium — named as part of the same "current rule" sentence as I1, but the PRD's own language ("preference"/"applying a... preference") already signals a soft rule, unlike I1's "constraint" framing. | Low — lives in one file (`PavedRatioCalculator.cs`), consumed as a single `OrderByDescending` key in one other file (`LoopRouteGenerator.cs:102, 112`). | Consistently a soft ranking tiebreaker everywhere it appears — **declared as soft, and code matches the declaration.** No drift. |
| **I3 — distance range** | High — the primary user input contract. | Low — validated once at the API boundary (`Program.cs:382-383`) and once as a candidate filter (`LoopRouteGenerator.cs:97`); both agree on the same bounds. | **Enforced, consistently.** |
| **I5 — ownership** | High. | High by file count (repeated `r.UserId == sub` filter across most route endpoints, e.g. `Program.cs:229, 249, 289`) but **not inconsistent** — every occurrence applies the identical rule from the identical source (`GetSub()`), and a foreign-key constraint (`AppDbContext.cs:32-35`) backs it at the data layer too. | **Enforced, redundantly and consistently** (defense in depth, not drift). |

**Selection: I1 — the overlap ceiling.** It is the only invariant in this codebase that is simultaneously (a) high core-value, (b) spread thinly and *inconsistently* across layers (two disagreeing numeric constants for one documented rule, reaching the client as a stripped-down boolean), and (c) declared but not actually enforced. I5 is spread across just as many files but is not weak — it is the textbook example of a *well*-guarded invariant, included above precisely to show the axes are independent: high spread alone does not make a good refactor target; high spread **combined with** weak/inconsistent enforcement does. I2 and I3 are single-axis outliers (soft-by-design, or narrow-but-solid, respectively) and are excluded for the same reason. I4 is excluded because, unlike I1, its gap is already tracked (`S-07`, parked) rather than a silent contract violation — the team already has visibility into it, so a refactor plan adds little there beyond what the roadmap already states.

## Step 3 — Diagnosis of I1 (today's code)

Where the rule lives today, layer by layer, and exactly how it fails to hold:

- **Constant duplication, disagreeing values** — `LoopRouteGenerator.cs:9`: `PrimaryOverlapThreshold = 0.10` (the PRD's actual number). `OverlapDetector.cs:12`: `Ceiling = 0.40` (a different, undocumented number, doc-commented only as "above which a route is flagged as a quality warning to the caller" — no citation to any requirement). Nothing in the codebase asserts these two constants are related; a future edit to either can silently widen or narrow the gap between them.
- **Selection logic never rejects, only ranks** — `LoopRouteGenerator.cs:90-138` (`SelectBestRoute`). Candidates are first filtered by distance only (`:93-98`). A `strict` tier is computed by filtering `OverlapRatio <= PrimaryOverlapThreshold` (`:100-106`); if that tier is non-empty its best member is returned (`:108-109`) — this is the only branch where the 10% figure has any effect at all. If the `strict` tier is **empty**, execution falls through to `:111-126`, which re-ranks **all** distance-valid candidates with no overlap filter whatsoever and returns the top one regardless of its overlap ratio — even if every single candidate retraces 90%+ of itself.
- **Error swallowed, not stopped** — `LoopRouteGenerator.cs:120-123`: the only consequence of the fallback candidate exceeding even the *looser* 40% figure is `_logger.LogWarning(...)`. The operation is not halted, no error is returned, and the (potentially very poor) route is still shipped as `RoutingResult<T>.Success` (`:125`). This is precisely the "swallowed error" pattern KROK3/OGRANICZENIA calls out: a documented hard constraint degrades to a background log line.
- **Silent band, no signal at all** — for any candidate with `0.10 < OverlapRatio <= 0.40`, none of the above branches fire: it clears the fallback path (`:111-126`) without ever exceeding `OverlapDetector.Ceiling`, so not even the warning log executes. The caller receives a route that already violates the documented "at most 10%" rule with zero indication anywhere in the response.
- **Contract only carries the coarsened signal** — `RouteResult.cs:11`: `QualityWarning => OverlapRatio > OverlapDetector.Ceiling` exposes only the 40%-threshold boolean over the wire; the actual `OverlapRatio` value is present on the record (`:10`) but the 10% figure the PRD actually promises is not represented anywhere in the API response or its consumers.
- **API layer is a pure pass-through** — `Program.cs:389` (`POST /routes/loop`) calls `gen.GenerateAsync` and returns `result.Value` unmodified on success (`:404`); it has no knowledge of overlap at all, so it cannot be the fix point — the rule must be enforced before the result reaches this layer.
- **Frontend renders, does not guard** — `RouteInfoPanel.tsx:106-110` shows a fixed English sentence ("This route has more overlap/backtracking than usual for the area...") purely as a function of the `qualityWarning` boolean (`route.ts:24`); there is no client-side threshold logic to find or remove — the frontend is a faithful renderer of whatever the backend decided, confirming the defect is entirely server-side.
- **Document says "current, unmodified" — code says "soft, inconsistently thresholded."** `prd-v2.md:20, 160` state the 10% figure in the present tense as an already-shipped guarantee inherited unchanged from v1. Nothing in the code enforces that number as a hard ceiling today.

## Step 4 — Aggregate-guardian design

### Decision: this is a computed business-rule invariant, not a persistence/atomicity invariant

Unlike `Share`'s one-live-share-per-`Route` rule (backed by a DB unique index) or the account-deletion cascade (backed by FK `OnDelete(Cascade)`), I1 has no persisted state and no concurrency dimension — it is evaluated once, in-memory, per request, over a small in-memory candidate set. There is therefore **no repository and no transaction** in this design: the guard is a pure, side-effect-free domain service invoked synchronously inside the existing request. This is a deliberate scope boundary, not an omission — repository/transaction plumbing would be over-engineering for a rule with no persisted state to protect.

### Domain error

```csharp
namespace VeloRoute.Routing;

/// <summary>
/// Thrown when no generated candidate satisfies the route-quality invariant
/// (overlap ceiling). Distinct from provider/network failures — this is a
/// legitimate "the road network here doesn't support a compliant loop" outcome.
/// </summary>
public sealed class RouteQualityExceededException(double bestOverlapRatio, double maxOverlapRatio)
    : Exception($"Best candidate overlap ratio {bestOverlapRatio:P0} exceeds the {maxOverlapRatio:P0} limit")
{
    public double BestOverlapRatio { get; } = bestOverlapRatio;
    public double MaxOverlapRatio { get; } = maxOverlapRatio;
}
```

### Guardian ("aggregate root" for this invariant)

A pure policy object — the single place `MaxOverlapRatio` is declared, and the single place a candidate is accepted or rejected. Replaces the two disagreeing constants (`PrimaryOverlapThreshold`, `OverlapDetector.Ceiling`) with one.

```csharp
namespace VeloRoute.Routing;

internal sealed class LoopRouteQualityPolicy
{
    /// <summary>Single source of truth for the "≤10% segment-overlap" rule (prd-v2.md:20,160).</summary>
    public const double MaxOverlapRatio = 0.10;

    /// <summary>
    /// Precondition: candidates is non-empty and already distance-filtered.
    /// Postcondition: the returned route's OverlapRatio is always &lt;= MaxOverlapRatio.
    /// Illegal state (no qualifying candidate) throws RouteQualityExceededException —
    /// it does not fall back to a non-qualifying "best available" route.
    /// </summary>
    public RouteResult SelectQualifying(IReadOnlyList<CandidateMetrics> candidates, double targetMidMeters)
    {
        var qualifying = candidates.Where(c => c.OverlapRatio <= MaxOverlapRatio).ToList();

        if (qualifying.Count == 0)
        {
            var bestOverlap = candidates.Count == 0 ? 1.0 : candidates.Min(c => c.OverlapRatio);
            throw new RouteQualityExceededException(bestOverlap, MaxOverlapRatio);
        }

        return qualifying
            .OrderByDescending(c => c.PavedRatio)
            .ThenByDescending(c => c.SmoothnessScore)
            .ThenBy(c => c.MaxConsecutiveSharpTurns)
            .ThenBy(c => Math.Abs(c.Distance - targetMidMeters))
            .First()
            .Route;
    }
}
```

### Thin caller

`LoopRouteGenerator.SelectBestRoute` (`LoopRouteGenerator.cs:90-138`) shrinks to: distance-filter → delegate to `LoopRouteQualityPolicy.SelectQualifying` → catch the domain exception at the one point that already maps provider errors to `RoutingResult<T>.Failure` (mirroring the existing `"NO_VALID_RESULT"` pattern at `:136-137`, so the shape of the change is consistent with what is already there):

```csharp
try
{
    var route = _qualityPolicy.SelectQualifying(candidates, targetMidMeters);
    return RoutingResult<RouteResult>.Success(route);
}
catch (RouteQualityExceededException ex)
{
    return RoutingResult<RouteResult>.Failure(
        new RoutingError("ROUTE_QUALITY_EXCEEDED",
            $"No candidate met the {ex.MaxOverlapRatio:P0} overlap limit (best available: {ex.BestOverlapRatio:P0})"));
}
```

`Program.cs`'s `POST /routes/loop` switch expression (`:394-399`) gets one new arm — `"ROUTE_QUALITY_EXCEEDED" => (422, "ROUTE_QUALITY_EXCEEDED")` — placed alongside the existing `"NO_VALID_RESULT"` arm. No other line in `Program.cs` changes; the endpoint remains a thin pass-through, per the file's existing style.

### Consequence for the wire contract — flagged, not resolved here

Once no route with `OverlapRatio > 0.10` can ever be returned, `RouteResult.QualityWarning` (`RouteResult.cs:11`, gated at 0.40) becomes dead code — unreachable, since the policy now guarantees `OverlapRatio <= 0.10 < 0.40` for every successful response. `OverlapDetector.Ceiling` becomes dead alongside it. Removing these is a wire-contract change to the exact type (`RouteResult`) that `context/changes/refactor-opportunities/plan.md` ("C1") is already queued to restructure via OpenAPI codegen. **Recommendation: sequence this plan after C1 lands**, and fold the `QualityWarning` field removal into C1's contract regeneration rather than hand-editing `route.ts` twice. If C1 has not landed by the time this plan is implemented, Phase 3 below must include the manual `route.ts`/`RouteInfoPanel.tsx` edit as an explicit extra step.

## Step 5 — Before/after, phased plan, tests

### Before / after

| Location | Before | After |
|---|---|---|
| `LoopRouteGenerator.cs:9` | `PrimaryOverlapThreshold = 0.10` (soft tier boundary) | Removed — superseded by `LoopRouteQualityPolicy.MaxOverlapRatio` |
| `OverlapDetector.cs:12` | `Ceiling = 0.40` (unrelated, undocumented second threshold) | Removed |
| `LoopRouteGenerator.cs:90-138` | Falls back to best-available candidate regardless of overlap; logs only | Delegates to `LoopRouteQualityPolicy`; no fallback past 10% |
| `RouteResult.cs:11` | `QualityWarning` (0.40-gated boolean) | Removed (see C1 sequencing note above) |
| `Program.cs:394-399` | No arm for a quality-exceeded outcome | New `"ROUTE_QUALITY_EXCEEDED"` → 422 arm |
| Caller-visible behavior | A route retracing up to 100% of itself can be returned silently | Every returned route has `OverlapRatio <= 0.10`; otherwise the request fails with `422 ROUTE_QUALITY_EXCEEDED` |

### Phased plan (test-first — this project runs xUnit backend tests, `dotnet test` from `src/backend/`)

1. **Characterization test, before any change.** Pin today's actual fallback behavior (a candidate set with no member ≤10% overlap currently returns 200 with the worst-overlap candidate) so the "before" state is captured in a passing test before it is deleted. Guards against silently changing behavior nobody signed off on being changed.
2. **Test-first: `LoopRouteQualityPolicy` in isolation.** Write the test cases below against the new class before it exists (red), then implement `LoopRouteQualityPolicy` + `RouteQualityExceededException` to go green. No `LoopRouteGenerator` changes yet.
3. **Wire the policy into `LoopRouteGenerator`.** Replace `SelectBestRoute`'s body per Step 4; delete `PrimaryOverlapThreshold` and `OverlapDetector.Ceiling`; update the characterization test from step 1 to assert the new (fail-fast) outcome instead of the old (silent-degrade) one — this is the moment the behavior actually changes, isolated to one commit.
4. **API error mapping.** Add the `"ROUTE_QUALITY_EXCEEDED"` arm in `Program.cs`; add an integration test hitting `POST /routes/loop` with a mocked ORS client returning only high-overlap candidates, asserting `422` + that error code.
5. **Contract cleanup.** Remove `RouteResult.QualityWarning`; update `route.ts` and `RouteInfoPanel.tsx` (delete the now-impossible warning banner and its test in `RouteInfoPanel.test.tsx:31-33`) — done as part of C1 if C1 has landed by this point, otherwise as a standalone commit here.
6. **Docs.** Update `prd-v2.md` if it still frames the rule ambiguously, and `context/foundation/roadmap.md` if `S-07`'s "parked" note references the same overlap language, per this repo's "keep docs accurate" convention (`.github/copilot-instructions.md`).

### Test cases for the invariant (legal / illegal transitions)

| # | Input | Expected |
|---|---|---|
| T1 | One candidate at `OverlapRatio = 0.05` | Selected — legal, well inside limit |
| T2 | One candidate at exactly `OverlapRatio = 0.10` | Selected — boundary is inclusive (`<=`) |
| T3 | One candidate at `OverlapRatio = 0.1000001` | Rejected — `RouteQualityExceededException`, boundary is exclusive past the limit |
| T4 | Mixed set: one candidate at `0.35` overlap with the best paved ratio, one at `0.08` overlap with a worse paved ratio | The `0.08` candidate is selected — the invariant filters *before* the paved-ratio tiebreaker runs, never after |
| T5 | All candidates `> 0.10` overlap | `RouteQualityExceededException` thrown with the true minimum overlap among them (not a hardcoded value) |
| T6 | Empty candidate list (all ORS calls failed upstream) | `RouteQualityExceededException` thrown with `BestOverlapRatio = 1.0` sentinel — verifies the guard doesn't crash on an empty sequence via `Min()` |
| T7 (API-level) | `POST /routes/loop` with a mocked client producing only `T5`-shaped candidates | `422` with `code: "ROUTE_QUALITY_EXCEEDED"` |
| T8 (regression) | Existing `LoopRouteIntegrationTests` / `RouteQualityTests` suites | Still pass, or are updated deliberately where they asserted the old fallback behavior — audit `src/backend/VeloRoute.Tests/Routing/RouteQualityTests.cs` for any test currently asserting a >10%-overlap success case |

### New load-bearing names

This project has no formal contracts registry; recording here for the commit/PR description and for whoever implements this plan to grep for:

- `LoopRouteQualityPolicy` (class) — `src/backend/VeloRoute/Routing/`
- `LoopRouteQualityPolicy.MaxOverlapRatio` (const, value `0.10`) — replaces `LoopRouteGenerator.PrimaryOverlapThreshold` and `OverlapDetector.Ceiling`
- `RouteQualityExceededException` (class)
- `"ROUTE_QUALITY_EXCEEDED"` (API error code string, used in both `RoutingError.Code` and the `Program.cs` status-mapping switch)

## Summary

The overlap-ceiling invariant ("a generated loop route retraces at most 10% of itself," `prd-v2.md:20,160`) is the strongest refactor candidate in VeloRoute's domain because it is simultaneously high core-value, spread inconsistently across five files, and not actually enforced despite being declared. Diagnosis traced the defect to two disagreeing numeric constants — `LoopRouteGenerator.cs:9`'s documented `0.10` and `OverlapDetector.cs:12`'s undocumented `0.40` — where the generator's fallback path (`LoopRouteGenerator.cs:111-126`) silently accepts any candidate regardless of overlap and only logs a warning past the *wrong* threshold, leaving a 10–40% band with no signal to the caller at all. The proposed guardian is a pure, stateless `LoopRouteQualityPolicy` that becomes the sole place the 10% figure is declared and the sole gate a candidate must pass, throwing a named `RouteQualityExceededException` — mapped to a new `422 ROUTE_QUALITY_EXCEEDED` API response — instead of degrading silently, consistent with this plan's fail-fast constraint. Because the invariant has no persisted state, the design deliberately omits a repository or transaction, unlike the `Share`/account-deletion invariants elsewhere in the codebase. The plan is staged test-first behind a characterization test that pins today's behavior before it is intentionally changed, and flags one cross-cutting consequence: removing the now-dead `RouteResult.QualityWarning` field touches the same wire contract that the already-reviewed C1 refactor (`context/changes/refactor-opportunities/plan.md`) is queued to regenerate via OpenAPI codegen, so this plan recommends sequencing after C1 rather than editing `route.ts` twice.
