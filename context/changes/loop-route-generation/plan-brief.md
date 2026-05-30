# Loop Route Generation — Plan Brief

> Full plan: `context/changes/loop-route-generation/plan.md`
> Research: `context/changes/loop-route-generation/research.md`

## What & Why

VeloRoute's core value proposition — enter a start point and distance range, get a road-bike
loop route — does not yet exist. S-01 builds the entire feature end-to-end: the backend
algorithm that generates the route, and the frontend UI that lets the user find a start point,
trigger generation, and see the result on an interactive map.

## Starting Point

The ORS HTTP client (`OpenRouteServiceClient`), resilience handler, and domain types
(`RouteResult`, `RouteCoordinate`, etc.) are already in place from F-01. The frontend is still
the Next.js scaffold default page. The `SurfaceType` enum has a pre-existing bug (values
misaligned with ORS codes) that must be fixed first.

## Desired End State

A user opens VeloRoute, searches for a start address, picks it from autocomplete suggestions
(a pin appears on the map), enters a min/max km range, clicks Generate, and within ~5 seconds
sees a loop route drawn on the map with total distance shown. A Re-roll button lets them
regenerate a different-shaped route for the same inputs without reloading.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Loop generation algorithm | ORS multi-waypoint stitching: `[start→wp1→wp2→start]` | Gives full control over route shape and overlap, unlike ORS `round_trip` built-in which deviates ±30% | Research |
| ORS call strategy | 3 parallel calls (bearings 60°/180°/300°), best-pick within 4.5s | Parallel = no extra latency; multiple candidates improve quality; 4.5s CTS enforces 5s NFR | Plan |
| Waypoint placement | Haversine destination-point formula, radius = `(targetMid/2) × 0.45` | Validated by open-source analogues; 0.45 factor falls in the 0.35–0.5 empirically effective range | Research |
| Overlap detection | NTS `STRtree` + 15m buffer-intersect | Correct tool for measuring overlap length (not Hausdorff/Fréchet which measure similarity) | Research |
| SurfaceType fix | Rename enum values to match ORS codes exactly | Current values are wrong; renaming fixes silent data corruption with no external consumers yet | Plan |
| Map library | MapLibre GL JS + `@vis.gl/react-maplibre` | WebGL-rendered, smooth on mobile, excellent React integration | Plan |
| Tile source | OpenFreeMap | Free, no API key, OSM data, built on MapLibre | Plan |
| Geocoding | ORS `/geocode/autocomplete` proxied via Next.js API route | Same API key already configured; proxy keeps key server-side; 300ms debounce prevents quota exhaustion | Research + Plan |
| Steepness preference | Fixed Moderate (`steepness_difficulty=1`), no UI | Fewer inputs for v1; trivially adjustable in v2 | Plan |
| Re-roll UX | Re-roll button fires new request with random seed | Better UX than re-entering inputs; parallel calls already return the "best" of 3 so re-roll meaningfully differs | Plan |
| Frontend state | React state only (no URL params) | Location inputs must not persist per PRD privacy NFR; page refresh is acceptable for v1 | Plan |
| Error UX | Inline error with code-specific actionable message | ORS 2009/2010 require different guidance than rate limits or timeouts | Plan |
| Mobile layout | Form stacked above map on mobile; side-by-side on desktop | Follows Komoot/Google Maps pattern; map remains accessible without hiding inputs | Plan |

## Scope

**In scope:**
- Fix pre-existing `SurfaceType` enum bug
- Backend: `WaypointCalculator`, `OverlapDetector`, `LoopRouteGenerator`, `POST /routes/loop`
- Backend: extend `IOpenRouteServiceClient` with multi-waypoint overload + ORS options
- Frontend: geocoding proxy API route, `SearchBar`, `RouteMap` (MapLibre), `RouteForm`, `RouteInfoPanel`, `ErrorMessage`
- Frontend: responsive layout (mobile stacked / desktop side-by-side)
- Re-roll button

**Out of scope:**
- GPX export (S-02, separate change)
- User accounts, saved routes (v2)
- Multiple route proposals (v2)
- Imperial units (v2)
- Steepness preference UI (v2)
- URL-param persistence (privacy + complexity risk)

## Architecture / Approach

```
Browser → RouteForm → POST /routes/loop (Next.js passes to .NET backend)
                         ↓
                  LoopRouteGenerator
                   ├─ WaypointCalculator (haversine, 3 bearing sets)
                   ├─ IOpenRouteServiceClient × 3 (parallel Task.WhenAll, 4.5s CTS)
                   └─ OverlapDetector (NTS STRtree, 15m buffer-intersect)
                         ↓
                  RouteResult → RouteMap (MapLibre) + RouteInfoPanel

Browser → SearchBar → GET /api/geocode (Next.js proxy)
                         ↓
                  ORS /geocode/autocomplete (same API key)
```

Key constraint: the 4.5s `CancellationTokenSource` wrapping all parallel ORS calls is the
sole enforcement of the 5s NFR — it must be created per-request in the endpoint handler and
linked to the HTTP request `CancellationToken`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Backend Foundation | SurfaceType fix + multi-waypoint ORS client | Enum rename changes JSON output — verify with existing dev endpoint |
| 2. Backend Loop Generation | `POST /routes/loop` returning a validated loop route | ORS data gaps in the test area may cause 2009 errors; use Vienna as baseline |
| 3. Frontend API Layer | Geocoding proxy + typed API functions | `ORS_API_KEY` must be configured in `.env.local` before manual test |
| 4. Frontend UI | Complete user-facing app (search, map, form, errors) | MapLibre SSR — must not render server-side; `"use client"` required on `RouteMap` |

**Prerequisites:** F-01 complete (routing-api-wiring, status: `impl_reviewed`). ORS API key
in `appsettings.json` (backend) and `.env.local` (frontend, as `ORS_API_KEY`).

**Estimated effort:** ~3–4 after-hours sessions across 4 phases (aligned with 3-week MVP timeline).

## Open Risks & Assumptions

- **ORS free tier quota**: 3 parallel calls per generation = ~666 route generations/day on
  the 2,000/day limit. Acceptable for MVP; if traffic grows, switch to self-hosted ORS or
  reduce to 2 parallel calls.
- **OpenFreeMap reliability**: community-run, no SLA. If tiles are down, the map renders blank
  (app is still functional — route coordinates are preserved). Acceptable for MVP.
- **OSM data gaps**: `cycling-road` profile may return 2009 (no route) for rural/Eastern
  European areas with poor OSM coverage. The "No road route found" error message is the
  user-facing mitigation.
- **Overlap detection tolerance**: 15m buffer is calibrated for European latitudes (45–55°N).
  Routes in other regions may have false positives/negatives — acceptable for v1 given the
  European-first target audience.

## Success Criteria (Summary)

- `POST /routes/loop` with Vienna + 40–60 km returns a route within the distance range in <5s
- The frontend draws the route on the map; distance is displayed; Re-roll produces a visibly different shape
- Mobile layout is usable on a 375px viewport without horizontal scroll
