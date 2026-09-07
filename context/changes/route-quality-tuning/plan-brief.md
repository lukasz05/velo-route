# Route Quality Tuning — Plan Brief

> Full plan: `context/changes/route-quality-tuning/plan.md`
> Frame brief: `context/changes/route-quality-tuning/frame.md`
> Research: `context/changes/route-quality-tuning/research.md`

## What & Why

Loop routes feel "spiky" — sharp out-of-place detours breaking an otherwise
loop-shaped route. This is the user's top-priority symptom and, critically,
**not a regression**: it predates the (separately reverted) OSM/Overpass
work and traces to v1's own waypoint-placement and shape-scoring machinery,
flagged as an unresolved risk in the original v1 design doc and never
revisited. Research went further and found a second, more severe bug: the
scoring path that's supposed to prefer paved roads is inactive for most real
requests today, because the common "fallback" selection path ignores paved/
smoothness entirely.

## Starting Point

`LoopRouteGenerator` fires 3 bearing-based DIY candidates per request, no
awareness of the real road network. Live testing this session (23 ORS calls,
3 Polish cities) found only 2 of 10 DIY candidates clear the app's 10%
overlap bar, meaning most requests fall through to a fallback path that
orders purely by overlap ratio — the advertised "paved, then smooth"
preference never fires. A documented 0.40 overlap ceiling exists only as a
log message; nothing enforces it. The existing smoothness metric averages
sharp turns globally, diluting one severe local spike across hundreds of
coordinates — it structurally cannot catch the symptom the user cares about
most.

## Desired End State

Every request fires one parallel batch of 3 ORS-native `round_trip`
candidates alongside the existing 3 DIY sectors (6 calls, same latency
profile as today per live measurement). Selection uses one consistent
paved → smooth → spike-free → distance ordering in all cases. Responses
carry a real `qualityWarning` flag when overlap exceeds the ceiling, shown
to the user as a non-blocking notice instead of silently logged
server-side. A new locality-aware spike metric exists specifically to catch
"one bad out-and-back" — closing the measurement gap that made this
symptom unverifiable by automated means until now.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Generation strategy | Single parallel batch: 3 round_trip + 3 DIY, no retries | Live-measured retry-until-in-range risks ORS rate-limiting (429 reproduced); a same-wave batch captures round_trip's much lower overlap when it lands, with DIY as a same-wave safety net | Plan (live measurement this session) |
| Overlap-ceiling breach | Response-level quality flag, not hard reject | Preserves today's always-succeeds behavior; ships a known-bad route only with an honest signal instead of silently | Plan (user decision) |
| Batch composition | 3 round_trip + 3 DIY (6 calls) | Matches batch sizes measured live; doubles call volume but stays well within the 4.5s budget | Plan (user decision) |
| Test coverage | Add HttpMessageHandler-capture tests for outbound request shape | Closes a gap research explicitly flagged: zero existing tests inspect what's sent to ORS | Plan (user decision) |
| Spike metric scope | Include locality-aware metric in this change, not deferred | Without it, the user's top-priority symptom has no automated success measure | Plan (user decision) |
| Round_trip vs. DIY-only vs. hybrid | Hybrid (this change) over DIY-only tuning | round_trip's overlap is an order of magnitude better; DIY-only would leave symptom #1/#2-adjacent overlap problems uncorrected | Research |
| OSM/Overpass scenic+POI work | Out of scope | Separately parked (Overpass reliability); frame confirmed it doesn't explain the spike symptom | Frame |

## Scope

**In scope:**
- ORS `round_trip` client support (new interface method, DTOs, request-shape tests)
- Combined round_trip + DIY candidate batch, single parallel wave, no retries
- Fixed selection ordering (paved/smoothness active in both overlap buckets)
- Real overlap-ceiling quality flag on the response
- Locality-aware spike metric (`maxConsecutiveSharpTurns`)
- Frontend surfacing of the quality flag
- Docs sync (`loop-route-algorithm.md`, `roadmap.md`)

**Out of scope:**
- OSM/Overpass scenic + POI work (parked separately, roadmap S-07)
- Sequential retry-until-in-range for round_trip (rate-limit risk)
- Multi-ORS-instance/API-key load distribution (parked per user, later decision)
- Increasing DIY `BearingCount` beyond 3
- Hard-rejecting on overlap-ceiling breach
- Post-hoc POI-directed reroute-through insertion (idea #8)
- Rate-limit load testing under concurrent multi-user traffic

## Architecture / Approach

`LoopRouteGenerator.FetchCandidatesAsync` fires 6 parallel ORS calls (3
`round_trip` seeds + 3 DIY sectors) via one `Task.WhenAll`, unchanged
concurrency pattern from today just wider. `SelectBestRoute` scores all 6
candidates through one unified ordering function regardless of source,
tries a strict-overlap bucket first then falls back to all in-range
candidates with the *same* ordering (fixing today's fallback-ignores-quality
bug). `RouteResult` gains three computed properties (`OverlapRatio`,
`QualityWarning`, `MaxConsecutiveSharpTurns`) following the exact pattern
`PavedRatio`/`SmoothnessScore` already use — since the endpoint returns
`RouteResult` with no wrapper DTO, this is the only plumbing needed to reach
the frontend.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. ORS round_trip client support | New client method + DTOs + request-shape tests | Getting the nested `round_trip` JSON shape exactly right — mitigated by capture-based tests |
| 2. Combined batch + fixed selection + ceiling | 6-candidate batch, unified ordering, real qualityWarning flag | Pre-compensation constant (0.70) may need live tuning; total-failure aggregation must preserve HTTP status routing |
| 3. Locality-aware spike metric | `SpikeDetector`, wired into scoring and response | Extracting shared per-index logic from `SmoothnessCalculator` without breaking its existing average |
| 4. Surfacing + docs sync | Frontend banner, type updates, docs, final live re-validation | Live re-validation may reveal Phase 2/3 constants still need adjustment |

**Prerequisites:** ORS API key configured (already is, via `dotnet user-secrets`); no new external dependencies.
**Estimated effort:** ~3-4 implementation sessions across 4 phases, each phase independently mergeable.

## Open Risks & Assumptions

- ORS rate limits under real concurrent multi-user load are unmeasured; doubling call volume (3→6) increases exposure. No mitigation in this change beyond staying single-batch/no-retry.
- The 0.70 pre-compensation factor and `points=5` are starting defaults, explicitly re-measured and possibly adjusted during Phase 2's manual verification.
- Coastal/edge-of-network locations (e.g. Gdynia) may still see more frequent `qualityWarning: true` responses than inland locations — this change reduces but doesn't eliminate quality variance there.

## Success Criteria (Summary)

- Paved/smoothness preference is active for every request, not just the ~20% that clear the strict overlap bar today.
- The overlap ceiling is a real, response-visible signal instead of a log-only warning.
- A locality-aware spike metric exists and measurably distinguishes smooth loops from spiky ones.
- Live re-validation against this session's 3-city baseline shows improvement with no regression.
