# GPX Export Implementation Plan

## Overview

Implement FR-006: a GPX export button that lets the user download the generated loop route
as a valid GPX 1.1 file importable to Strava, Garmin, and Komoot without modification.
GPX serialisation lives in the .NET backend (single canonical implementation reusable by
any future client), exposed via `POST /routes/gpx`. The Next.js frontend proxies that
endpoint following the existing `/api/routes/loop` pattern.

## Current State Analysis

`RouteResult` with `geometry.coordinates` (lon/lat pairs) is already held in `RouteApp`
state after a successful generation. `RouteInfoPanel` displays only the total distance.
No GPX endpoint or download wiring exists yet.

The `RouteCoordinate` type has `longitude` and `latitude` only — no elevation field. This
is intentional for v1; elevation is deferred.

### Key Discoveries

- `Program.cs:63` — `POST /routes/loop` is the pattern to follow for the new endpoint.
- `src/app/api/routes/loop/route.ts` — the Next.js proxy pattern to mirror for `/api/routes/gpx`.
- `RouteApp.tsx:78` — `RouteInfoPanel` receives only `distanceMeters`; needs refactoring to
  accept the full `RouteResult` so the download button can access coordinates.
- `RouteCoordinate` (`Routing/RouteResult.cs:10`) — `double Longitude, double Latitude` — reused as the GPX input DTO.
- `bootstrap_scaffold` namespace is used across all backend files.

## Desired End State

When a route has been generated, the info panel shows the distance and a "Download GPX"
button. Clicking it POSTs the route coordinates to `/api/routes/gpx` (Next.js proxy to
backend), receives GPX 1.1 XML, and triggers a `veloroute-{timestamp}.gpx` download.
The downloaded file imports cleanly into Strava, Garmin, and Komoot.

To verify: generate a route, click Download GPX, validate the file at
https://www.j-berkemeier.de/ShowGPX.html — no errors; import to Strava and confirm the
activity is created as a cycling route with correct distance and shape.

## What We're NOT Doing

- No elevation data (ORS `elevation: true` + schema changes deferred to v2).
- No client-side GPX generation — the backend is the single source of GPX truth.
- No `Content-Disposition` filename from the backend — the frontend sets the timestamped name.
- No compression or streaming for the GPX response (payload is small, ~50–200 KB).

## Implementation Approach

Three changes across two services, following existing patterns throughout:

1. **Backend** — `GpxSerializer` utility class + `POST /routes/gpx` minimal API endpoint
   that accepts a coordinate list and returns `application/gpx+xml`.
2. **Next.js proxy** — `src/app/api/routes/gpx/route.ts` following the same proxy pattern
   as `src/app/api/routes/loop/route.ts`.
3. **Frontend** — `RouteInfoPanel` refactored to accept `route: RouteResult`, adds the
   download button; `RouteApp` passes `routeResult` instead of `distanceMeters`.

## Critical Implementation Details

**GPX format contract** — Strava, Garmin, and Komoot require the `<trk>/<trkseg>/<trkpt>`
track format (not `<rte>/<rtept>`), GPX 1.1 namespace declaration, and `lat`/`lon` as
attributes on `<trkpt>`. The `xsi:schemaLocation` header is needed for strict validators.
See the Contract block in Phase 1 for the exact XML skeleton.

**Invariant culture for doubles** — `GpxSerializer` must format `double` coordinate values
with `InvariantCulture` to avoid locale-specific decimal separators (e.g. comma in Polish
or German locales) that would produce invalid GPX XML.

**Filename is set in the browser** — the backend returns `Content-Type: application/gpx+xml`
only (no `Content-Disposition`). The frontend sets `veloroute-{timestamp}.gpx` when
creating the download anchor, keeping the timestamp accurate to the moment of download.

**Blob URL cleanup** — `URL.revokeObjectURL` must be called after the synthetic `<a>` click
to avoid memory leaks on repeated downloads. Revocation immediately after `a.click()` is
safe in all modern browsers.

---

## Phase 1: Backend GPX Endpoint

### Overview

Add a `GpxSerializer` static class in `src/backend/Routing/` and a `POST /routes/gpx`
minimal API endpoint in `Program.cs` that accepts a list of coordinates and returns a
GPX 1.1 XML response.

### Changes Required

#### 1. New file: `src/backend/Routing/GpxSerializer.cs`

**File**: `src/backend/Routing/GpxSerializer.cs`

**Intent**: Serialise a list of `RouteCoordinate` values to a valid GPX 1.1 XML string.
This is the canonical GPX implementation shared by all clients.

**Contract**: `namespace bootstrap_scaffold.Routing`. One `internal static` class with one
method `Serialize(IReadOnlyList<RouteCoordinate> coordinates): string`. The GPX structure
that must be produced (skeleton — implementer fills coordinate list):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<gpx version="1.1" creator="VeloRoute"
     xmlns="http://www.topografix.com/GPX/1/1"
     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
     xsi:schemaLocation="http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd">
  <metadata>
    <name>VeloRoute Loop</name>
    <time>{DateTime.UtcNow:O}</time>
  </metadata>
  <trk>
    <name>VeloRoute Loop</name>
    <type>cycling</type>
    <trkseg>
      <trkpt lat="{coord.Latitude.ToString(InvariantCulture)}" lon="{coord.Longitude.ToString(InvariantCulture)}"></trkpt>
      ...
    </trkseg>
  </trk>
</gpx>
```

#### 2. New endpoint in `Program.cs`

**File**: `src/backend/Program.cs`

**Intent**: Expose `POST /routes/gpx` that accepts a JSON body with a coordinates array,
delegates to `GpxSerializer.Serialize`, and returns the result as `application/gpx+xml`.

**Contract**: Request record `GpxRequest(IReadOnlyList<RouteCoordinate> Coordinates)`
added at the bottom of `Program.cs` alongside the existing `LoopRouteRequest` record.
The endpoint returns `Results.Text(gpxXml, "application/gpx+xml")`. Validates that
`Coordinates` is non-empty (return 400 `INVALID_INPUT` otherwise). No DI registration
needed — `GpxSerializer` is a static class called inline.

### Success Criteria

#### Automated Verification

- `dotnet build` passes in `src/backend/`

#### Manual Verification

- `curl -s -X POST http://localhost:5098/routes/gpx -H "Content-Type: application/json" -d '{"coordinates":[{"longitude":16.3719,"latitude":48.2082},{"longitude":16.4,"latitude":48.22},{"longitude":16.3719,"latitude":48.2082}]}'`
  returns GPX XML with `Content-Type: application/gpx+xml` and three `<trkpt>` entries.
- Paste the response into https://www.j-berkemeier.de/ShowGPX.html — no errors, route renders.

**Implementation Note**: After completing this phase and all automated verification passes,
pause here for manual confirmation from the human that the manual testing was successful
before proceeding to the next phase. Phase blocks use plain bullets — the corresponding
`- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the
plan.

---

## Phase 2: Next.js API Proxy Route

### Overview

Add `src/frontend/src/app/api/routes/gpx/route.ts` — a Next.js POST handler that forwards
the request body to the backend `POST /routes/gpx` and returns the GPX response to the
browser, following the same pattern as `src/app/api/routes/loop/route.ts`.

### Changes Required

#### 1. New file: `src/app/api/routes/gpx/route.ts`

**File**: `src/frontend/src/app/api/routes/gpx/route.ts`

**Intent**: Proxy `POST /api/routes/gpx` requests to the backend, preserving the
`application/gpx+xml` content-type so the browser component can create a Blob from the
response text.

**Contract**: POST handler reads the JSON body, POSTs it to
`${process.env.VELO_API_URL ?? 'http://localhost:5098'}/routes/gpx`, and returns the
response body as a `new Response(gpxText, { headers: { 'Content-Type': 'application/gpx+xml' } })`.
On non-OK backend response, return `Response.json({ error, code }, { status })` mirroring
the error shape used by the loop proxy.

### Success Criteria

#### Automated Verification

- `npm run build` passes in `src/frontend/`
- `npm run lint` passes in `src/frontend/`

#### Manual Verification

- With both services running, `curl -s -X POST http://localhost:3000/api/routes/gpx`
  with a valid coordinates body returns GPX XML matching the backend response.

**Implementation Note**: After completing this phase and all automated verification passes,
pause here for manual confirmation from the human that the manual testing was successful
before proceeding to the next phase. Phase blocks use plain bullets — the corresponding
`- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the
plan.

---

## Phase 3: Frontend Download Button

### Overview

Refactor `RouteInfoPanel` to accept a full `RouteResult` prop, add the "Download GPX"
button that POSTs to `/api/routes/gpx` and triggers a browser download, and update
`RouteApp` to pass `routeResult` directly.

### Changes Required

#### 1. Update `RouteInfoPanel.tsx`

**File**: `src/frontend/src/components/RouteInfoPanel.tsx`

**Intent**: Replace the `{ distanceMeters: number }` prop with `{ route: RouteResult }`,
derive `distanceMeters` from `route.distanceMeters`, and add a "Download GPX" button that
POSTs to `/api/routes/gpx` and triggers a Blob download with a timestamped filename.

**Contract**: The component gains a `"use client"` directive (it handles a click event and
uses `fetch`). The download handler: (1) sets loading state to disable double-clicks,
(2) POSTs `{ coordinates: route.geometry.coordinates }` to `/api/routes/gpx`, (3) reads
the response as `text()`, (4) creates a `Blob([gpxText], { type: 'application/gpx+xml' })`,
(5) creates an `<a>` with `href = URL.createObjectURL(blob)` and
`download = 'veloroute-{timestamp}.gpx'` (timestamp formatted as `YYYYMMDDTHHMMSS` from
`new Date()`), (6) clicks it, (7) calls `URL.revokeObjectURL` immediately after.

#### 2. Update `RouteApp.tsx`

**File**: `src/frontend/src/components/RouteApp.tsx`

**Intent**: Pass `routeResult` (the full object) to `RouteInfoPanel` instead of
`routeResult.distanceMeters`.

**Contract**: Change line 78 from
`<RouteInfoPanel distanceMeters={routeResult.distanceMeters} />` to
`<RouteInfoPanel route={routeResult} />`. No other changes to `RouteApp`.

### Success Criteria

#### Automated Verification

- `npm run build` passes in `src/frontend/`
- `npm run lint` passes in `src/frontend/`

#### Manual Verification

- Generate a route; confirm the info panel shows the distance and a "Download GPX" button.
- Click the button; confirm it enters a loading/disabled state, then a file named
  `veloroute-{timestamp}.gpx` is downloaded.
- Open the file in a text editor; confirm GPX 1.1 structure with `<trkpt>` entries.
- Import the file to Strava (or validate at j-berkemeier.de); confirm it imports cleanly
  as a cycling activity with correct distance and route shape.
- Click "Download GPX" a second time — confirm no JS errors in the console.
- On a 375px viewport (Chrome DevTools), confirm the button is fully visible and tappable.

**Implementation Note**: After completing this phase and all automated verification passes,
pause here for manual confirmation from the human that the manual testing was successful
before proceeding to the next phase. Phase blocks use plain bullets — the corresponding
`- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the
plan.

---

## Testing Strategy

### Manual Testing Steps

1. Generate a loop route (e.g. Vienna, 40–60 km).
2. Click "Download GPX" — confirm button shows loading state, then file downloads as
   `veloroute-YYYYMMDDTHHMMSS.gpx`.
3. Open the file in a text editor — confirm GPX 1.1 namespace, `<trk>/<trkseg>/<trkpt>`
   structure, and that the first and last `<trkpt>` match (loop closes).
4. Validate at https://www.j-berkemeier.de/ShowGPX.html — no errors, route renders on map.
5. Import to Strava — confirm activity is created as a cycling route with correct distance.
6. Click "Download GPX" a second time — no JS errors.

## Performance Considerations

A 40–60 km route at ORS default density has ~500–2000 coordinate points, producing a GPX
file of ~50–200 KB. Both serialisation (.NET string building) and network transfer are
negligible — no streaming or chunking needed.

## References

- PRD: FR-006 GPX export, US-01 acceptance criteria — `context/foundation/prd.md`
- Backend pattern: `src/backend/Program.cs:63` (`POST /routes/loop`)
- Frontend proxy pattern: `src/frontend/src/app/api/routes/loop/route.ts`
- Frontend types: `src/frontend/src/types/route.ts`
- Component call site: `src/frontend/src/components/RouteApp.tsx:78`
- GPX 1.1 schema: https://www.topografix.com/GPX/1/1/gpx.xsd

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend GPX Endpoint

#### Automated

- [x] 1.1 dotnet build passes — b109958

#### Manual

- [x] 1.2 curl to POST /routes/gpx returns valid GPX XML — b109958
- [x] 1.3 GPX output validates at j-berkemeier.de/ShowGPX.html — b109958

### Phase 2: Next.js API Proxy Route

#### Automated

- [x] 2.1 npm run build passes — f1171aa
- [x] 2.2 npm run lint passes — f1171aa

#### Manual

- [x] 2.3 curl to POST /api/routes/gpx returns same GPX as backend — f1171aa

### Phase 3: Frontend Download Button

#### Automated

- [x] 3.1 npm run build passes
- [x] 3.2 npm run lint passes

#### Manual

- [x] 3.3 Download button triggers veloroute-{timestamp}.gpx download with loading state
- [x] 3.4 Downloaded file imports cleanly to Strava as a cycling route
- [x] 3.5 No JS errors on repeated clicks
- [x] 3.6 Button fully visible and tappable on 375px viewport
