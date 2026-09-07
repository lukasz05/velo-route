<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Route Quality Tuning

- **Plan**: context/changes/route-quality-tuning/plan.md
- **Scope**: Full plan (Phases 1-4)
- **Date**: 2026-08-06
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Redundant recomputation of RouteResult's computed metrics

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (performance)
- **Location**: src/backend/VeloRoute/Routing/RouteResult.cs:8-12
- **Detail**: `OverlapRatio`, `QualityWarning`, `MaxConsecutiveSharpTurns`, `SmoothnessScore`, `PavedRatio` are all get-only properties that recompute from scratch on every access. `SelectBestRoute`'s LINQ `.Select(...).Where(...)` evaluates all four metrics for all 6 raw candidates before the distance filter drops out-of-range ones, and `Program.cs:350`'s `Results.Ok(result.Value)` re-triggers every getter again during JSON serialization. For the winning route, `OverlapRatio`'s STRtree pass runs ~3x and `ComputeSharpTurnFlags` runs ~4x. The plan's "shared data" intent (extracting `ComputeSharpTurnFlags` so `SpikeDetector` doesn't duplicate trig) is satisfied at the code-sharing level but not at the runtime level — the shared loop still re-runs per property access. Not critical at current route sizes, but stacks on the already-accepted 3→6 ORS call-volume increase as an easily-avoidable regression.
- **Fix A ⭐ Recommended**: Memoize each computed property behind a private nullable backing field on `RouteResult`, computed on first access.
  - Strength: Minimal, localized change — the public API (property getters) is unchanged, so `LoopRouteGenerator`/`Program.cs`/tests/frontend need zero changes.
  - Tradeoff: `RouteResult` is a `record`; adding mutable backing fields is safe for equality (record equality only covers primary-constructor members) but is a small deviation from "pure" record immutability — needs a one-line comment explaining why, or reviewers will flag it as a nit later.
  - Confidence: HIGH — standard C# lazy-property pattern, no framework interaction risk.
  - Blind spot: Concurrent access isn't a concern here (each `RouteResult` is single-request-scoped, never shared across threads), but that assumption isn't documented anywhere.
- **Fix B**: Compute all metrics once per candidate into a separate `CandidateMetrics` struct inside `SelectBestRoute`, before the `Where`/`OrderBy` chain, and pass the already-computed values through instead of relying on `RouteResult`'s properties at all.
  - Strength: Keeps `RouteResult` a pure, simple record with no caching state; computation happens exactly once by construction, in one obvious place.
  - Tradeoff: Larger diff — touches `SelectBestRoute`'s LINQ chain and doesn't fix the *second* redundant pass at JSON-serialization time (the winning route's properties still get re-read once each by `System.Text.Json`, though that's now just 1x instead of 3-4x since it's the final read).
  - Confidence: MEDIUM — larger surface area increases the chance of subtly changing selection-order behavior if the extraction isn't done carefully.
  - Blind spot: Haven't measured actual wall-clock cost of the redundant recomputation at realistic route sizes (hundreds of coordinates) — this finding is a code-quality/scaling concern, not a measured bottleneck.
- **Decision**: FIXED (Fix B — `CandidateMetrics` struct extracted in `LoopRouteGenerator.SelectBestRoute`, computed once per candidate via a shared sharp-turn-flags array; `SmoothnessCalculator.ComputeFromFlags`/`SpikeDetector.ComputeFromFlags` added so the flags array isn't recomputed independently for smoothness vs. spike. Note: the JSON-serialization-time re-read of the winning `RouteResult`'s properties is unchanged, as documented in Fix B's tradeoff — full elimination would need Fix A's memoization.)

## Success Criteria Verification

**Automated** (re-run this session, current branch state):
- `dotnet build` (src/backend) — clean, 0 warnings, 0 errors
- `dotnet test` (src/backend, `DOCKER_API_VERSION=1.41` workaround for Testcontainers) — 107 passed, 3 skipped (live-smoke tests, skip-by-design), 0 failed
- `npm run build` (src/frontend) — compiled successfully, type-checks clean
- `npm run lint` (src/frontend) — clean
- `npm test` (src/frontend) — 40 passed (8 files), including new RouteInfoPanel.test.tsx

**Manual** (all Progress rows show `[x]`, evidence checked):
- 1.4, 2.5, 2.6, 3.4 — confirmed complete in prior phase gates (SHAs 0bf1fa0, efe514f, 854bf3b)
- 4.4, 4.5, 4.6 — confirmed complete by user this session (SHA 530ab5a); 4.6's automated-checkable portion (README/AGENTS.md staleness) independently verified via grep — no stale round_trip/candidate-count references found outside the addendum that was added

## Notes

- Plan-drift sub-agent found zero DRIFT/MISSING/EXTRA across all 4 phases, Critical Implementation Details (0.70 pre-compensation, seed rule, error aggregation, shared spike-detection data), and the "What We're NOT Doing" boundary list.
- Safety/pattern sub-agent found no security, reliability, data-safety, or pattern-consistency issues. Cancellation-token propagation, HttpResponseMessage disposal, and total-failure error aggregation were specifically verified correct.
