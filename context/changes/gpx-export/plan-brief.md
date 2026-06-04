# GPX Export — Plan Brief

> Full plan: `context/changes/gpx-export/plan.md`

## What & Why

VeloRoute's primary success criterion (PRD) is that a user can download the generated route
as a valid GPX file importable to Strava, Garmin, and Komoot — without creating an account.
The loop route generation is in place (S-01); this change adds the final export step (FR-006)
to complete the v1 feature set.

## Starting Point

`RouteResult` with `geometry.coordinates` (lon/lat pairs) is already held in `RouteApp`
React state after a successful generation. `RouteInfoPanel` displays only the total distance.
No GPX endpoint or download trigger exists yet.

## Desired End State

After generating a route, the user sees a "Download GPX" button in the info panel. One click
POSTs coordinates to the backend, receives GPX 1.1 XML, and downloads it as
`veloroute-{timestamp}.gpx`. The file imports cleanly into Strava, Garmin, and Komoot.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
|---|---|---|
| Generation location | Backend `POST /routes/gpx` | Single canonical implementation — web, future mobile app, and any other client reuse the same endpoint without duplicating GPX logic |
| Elevation data | Omit for v1 | `RouteCoordinate` has lon/lat only; adding elevation requires ORS backend changes deferred to v2 |
| Filename | `veloroute-{timestamp}.gpx` | Unique per download, set by the browser at download time — backend doesn't need to know the clock |
| Button placement | Inside `RouteInfoPanel`, below distance | Natural location — user has just seen the distance and wants to act on the result |
| GPX format | GPX 1.1 `<trk>/<trkseg>/<trkpt>` | Track format is the most universally accepted by Strava, Garmin, and Komoot |

## Scope

**In scope:**
- `src/backend/Routing/GpxSerializer.cs` — static serialiser, `Serialize(coordinates): string`
- `Program.cs` — `POST /routes/gpx` endpoint + `GpxRequest` record
- `src/frontend/src/app/api/routes/gpx/route.ts` — Next.js proxy route
- `RouteInfoPanel.tsx` — refactored to accept `route: RouteResult`, adds Download GPX button
- `RouteApp.tsx` — passes `routeResult` to `RouteInfoPanel` instead of `distanceMeters`

**Out of scope:**
- Elevation data in GPX (v2)
- Client-side GPX generation
- Direct Strava/Komoot import URL (v2 consideration)
- Multiple route export or batch download

## Architecture / Approach

```
Browser (click) → POST /api/routes/gpx (Next.js proxy)
                      → POST /routes/gpx (.NET backend)
                            → GpxSerializer.Serialize(coordinates)
                            ← application/gpx+xml
                      ← GPX text
Browser: Blob → <a download="veloroute-{ts}.gpx"> → save file
```

`GpxSerializer` is a static internal class in the `Routing/` folder, keeping GPX knowledge
co-located with other routing domain code. The proxy follows the identical pattern as
`/api/routes/loop`. `RouteInfoPanel` gains `"use client"` for the click handler.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Backend GPX Endpoint | `GpxSerializer` + `POST /routes/gpx` | Incorrect namespace or `InvariantCulture` omission breaks GPX on non-English servers |
| 2. Next.js Proxy Route | `/api/routes/gpx` forwarding layer | Must pass through `application/gpx+xml` content-type, not re-wrap as JSON |
| 3. Frontend Download Button | Button in `RouteInfoPanel`, `RouteApp` prop update | `RouteInfoPanel` needs `"use client"` directive added |

**Prerequisites:** S-01 complete (`loop-route-generation`) — route generation must work
end-to-end before export can be manually tested.

**Estimated effort:** ~1 focused session across 3 small phases.

## Open Risks & Assumptions

- Strava's GPX importer accepts `<type>cycling</type>` as the activity type hint — if not,
  import still succeeds but defaults to the user's configured sport (acceptable for v1).
- `URL.createObjectURL` + `<a download>` is supported in all target browsers (Chrome,
  Firefox, Safari, Edge latest two majors) — safe assumption.

## Success Criteria (Summary)

- `curl POST /routes/gpx` with 3 coordinates returns valid GPX 1.1 XML that validates at j-berkemeier.de.
- Clicking "Download GPX" in the UI downloads `veloroute-{timestamp}.gpx` with no JS errors.
- The file imports to Strava as a cycling activity with the correct route shape and distance.
