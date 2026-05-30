# Loop Route Generation — Implementation Plan

## Overview

Implement S-01 end-to-end: a backend loop-route generation service using parallel ORS calls and
a frontend with geocoding search, MapLibre map display, and re-roll UX. Covers FR-001 through
FR-005 from the PRD. GPX export (FR-006, S-02) is explicitly out of scope.

## Current State Analysis

The F-01 foundation (routing-api-wiring) is complete:
- `IOpenRouteServiceClient` / `OpenRouteServiceClient` handle `POST /v2/directions/cycling-road/geojson`
  with resilience (2 retries, 5s attempt timeout, circuit breaker)
- `RouteResult`, `RouteGeometry`, `RouteCoordinate`, `RouteWaySegment` domain types exist
- `SurfaceType` and `RoadClass` enums exist but `SurfaceType` values are wrong (ORS code 3 =
  Asphalt; current enum 3 = Gravel)
- `OpenRouteServiceOptions` (BaseUrl + ApiKey) configured via `appsettings.json`
- Frontend has `RouteResult` TypeScript type and a `fetchRoutePreview()` stub; the main page is
  still the Next.js scaffold default

## Desired End State

After this plan:
- A user opens VeloRoute, types a start address, sees suggestions, picks one (map pin appears),
  enters a km range, clicks Generate, and within ~5 seconds sees a loop route drawn on a MapLibre
  map with total distance shown
- A Re-roll button generates a different route shape for the same inputs without page reload
- Inline error messages guide users when no route is found, inputs are invalid, or ORS is
  rate-limited
- The layout is usable on mobile (stacked) and desktop (side-by-side)

To verify: run both projects, navigate to http://localhost:3000, complete the full flow for a
known cycling area (e.g., Vienna, 40–60 km), confirm the route displays and distance is within
range.

### Key Discoveries

- `OpenRouteServiceClient.cs:130–140` — `OrsDirectionsRequest` is `file`-scoped; new fields
  (`options`, `profile_params`) can be added without touching the public API surface
- `OpenRouteServiceClient.cs:120` — surface code cast has the bug: `(SurfaceType)surfaceCode`
  maps code 3 (Asphalt) to `Gravel` — must fix before this plan can emit correct surface data
- `Program.cs:34–44` — resilience handler has a 5s **per-attempt** timeout; wrapping all
  parallel calls in one `CancellationTokenSource(4.5s)` enforces the end-to-end NFR
- `src/frontend/src/types/route.ts:9-10` — `surface` and `roadClass` are `string` fields, so
  renaming enum values doesn't break the frontend contract
- MapLibre GL JS requires a browser environment; the map component must use `"use client"` and
  must not render during SSR

## What We're NOT Doing

- GPX export — S-02, separate change
- User accounts, saved routes, history — v2
- Multiple route proposals per request — v2
- Imperial units — v2
- Steepness preference UI — fixed at Moderate (`steepness_difficulty=1`) for v1
- URL query param persistence — React state only; page refresh loses result
- Self-hosted tiles or MapTiler — OpenFreeMap tiles (no API key)

## Implementation Approach

**Parallel ORS calls for quality + speed:** fire 3 ORS calls concurrently, each with a different
bearing seed (60° / 180° / 300° offset by optional user seed), all sharing one 4.5s deadline.
Pick the result with distance closest to `targetMid` km that passes the ≤10% overlap check. This
gives route shape variety without sequential latency.

**Overlap detection:** NTS `STRtree` + buffer-intersect, 15 m tolerance. ~45 lines in a static
`OverlapDetector` class; added alongside `LoopRouteGenerator` in Phase 2.

**Geocoding:** ORS `/geocode/autocomplete` proxied through a Next.js Route Handler to keep the
API key server-side. 300 ms debounce on the frontend; minimum 2 characters before firing.

**Map:** MapLibre GL JS via `@vis.gl/react-maplibre`, OpenFreeMap style, `"use client"` client
component loaded in a `RouteApp` client root under `page.tsx`.

## Critical Implementation Details

**5s NFR enforcement:** The `HttpClient` resilience handler retries ORS errors, which can
compound latency. All three parallel `GetDirectionsAsync` calls must share a single outer
`CancellationToken` with a 4.5s timeout (leaving 500ms for processing). Pass this token into
the ORS client; the existing resilience handler will respect it via `OperationCanceledException`.

**SurfaceType enum fix ordering:** Phase 1 fixes the enum *before* adding the loop endpoint.
The existing `/routes/preview` dev endpoint will start returning correct surface labels after
the fix — verify this manually as Phase 1's smoke test.

**MapLibre SSR:** `maplibre-gl` accesses `window` at import time. The `RouteMap` component must
carry `"use client"` and must NOT be server-rendered. Wrap the import with Next.js `dynamic()`
if there are any hydration errors; if the whole page is already a client component tree under
`RouteApp`, a plain `"use client"` on `RouteMap` is sufficient.

---

## Phase 1: Backend Foundation — SurfaceType Fix + Multi-Waypoint Client

### Overview

Fix the pre-existing `SurfaceType` enum bug and extend `IOpenRouteServiceClient` to accept N
waypoints with full ORS options. This is the foundation Phase 2's `LoopRouteGenerator` depends
on.

### Changes Required

#### 1. Fix `SurfaceType` enum

**File:** `src/backend/Routing/SurfaceType.cs`

**Intent:** Rename enum members so integer values match ORS surface codes exactly. ORS's surface
extra-info codes 0–18 must round-trip correctly through the cast in `OpenRouteServiceClient.cs:120`.

**Contract:** Enum integer values must match ORS surface codes verbatim:

```csharp
Unknown = 0, Paved = 1, Unpaved = 2, Asphalt = 3, Concrete = 4,
Cobblestone = 5, Metal = 6, Wood = 7, CompactedGravel = 8,
FineGravel = 9, Gravel = 10, Dirt = 11, Ground = 12, Ice = 13,
PavingStones = 14, Sand = 15, Woodchips = 16, Grass = 17, GrassPaver = 18
```

Remove `Rock = 6` and `Salt = 14` (not ORS codes); add `GrassPaver = 18`. The
`[JsonConverter(typeof(JsonStringEnumConverter))]` attribute stays — JSON uses the name string,
so renaming changes API output (intentional: current names were wrong).

#### 2. Extend `IOpenRouteServiceClient` with multi-waypoint overload

**File:** `src/backend/Routing/IOpenRouteServiceClient.cs`

**Intent:** Add a second method accepting an ordered list of waypoints and ORS-level options.
The existing `GetDirectionsAsync(start, end, ct)` method stays unchanged so the dev preview
endpoint (`/routes/preview`) continues to work.

**Contract:** New method signature:
```csharp
Task<RoutingResult<RouteResult>> GetDirectionsAsync(
    IReadOnlyList<RouteCoordinate> waypoints,
    OrsDirectionOptions? options = null,
    CancellationToken cancellationToken = default);
```

#### 3. Add `OrsDirectionOptions` public DTO

**File:** `src/backend/Routing/OrsDirectionOptions.cs` (new file)

**Intent:** Carry the `avoid_features` list and `steepness_difficulty` value into the ORS
request body without leaking file-scoped DTOs through the interface boundary.

**Contract:** Simple record:
```csharp
public sealed record OrsDirectionOptions(
    IReadOnlyList<string>? AvoidFeatures = null,
    int? SteepnessDifficulty = null);
```

#### 4. Implement multi-waypoint method in `OpenRouteServiceClient`

**File:** `src/backend/Routing/OpenRouteServiceClient.cs`

**Intent:** Add the implementation of the new interface method. It builds `OrsDirectionsRequest`
from the waypoint list, maps `OrsDirectionOptions` onto the file-scoped `OrsDirectionsRequest`
DTOs, and reuses the existing response-parsing path (`MapToRouteResult`).

**Contract:** Extend the file-scoped `OrsDirectionsRequest` with `Options` (a nested
`OrsOptions` file-scoped DTO holding `avoid_features` and `profile_params.weightings.steepness_difficulty`).
The existing `GetDirectionsAsync(start, end, ct)` method delegates to the new overload passing
`[start, end]` with `options: null`.

### Success Criteria

#### Automated Verification

- `dotnet build` in `src/backend/` exits 0 with no warnings
- `dotnet run` starts without exception; `GET /health` returns 200

#### Manual Verification

- `GET /routes/preview` (dev) returns a route where `segments[*].surface` values are now
  correct ORS names (e.g., `"Asphalt"` not `"Gravel"` for Vienna roads)

**Implementation Note:** Pause after manual verification passes before starting Phase 2.

---

## Phase 2: Backend — Loop Route Generation Service + Endpoint

### Overview

Implement the loop generation algorithm: haversine waypoint placement, parallel ORS calls,
distance + overlap validation, best-pick selection, and the `POST /routes/loop` endpoint.

### Changes Required

#### 1. Add NuGet packages

**File:** `src/backend/backend.csproj`

**Intent:** Add `NetTopologySuite` (geometry primitives for overlap detection) and
`NetTopologySuite.IO.GeoJSON4STJ` (optional: direct GeoJSON → NTS bridge using STJ). NTS
targets `netstandard2.0` — compatible with .NET 10.

**Contract:**
```xml
<PackageReference Include="NetTopologySuite" Version="2.5.0" />
<PackageReference Include="NetTopologySuite.IO.GeoJSON4STJ" Version="4.0.0" />
```

#### 2. Implement `WaypointCalculator`

**File:** `src/backend/Routing/WaypointCalculator.cs` (new file)

**Intent:** Pure-static helper that converts a start coordinate + bearing + distance into a
destination coordinate using the haversine destination-point formula. Used by
`LoopRouteGenerator` to place the intermediate waypoints.

**Contract:** One public static method:
```csharp
public static RouteCoordinate DestinationPoint(
    RouteCoordinate start, double bearingDeg, double distanceMeters)
```

Uses Earth radius R = 6,371,000 m. The formula (from movable-type.co.uk):
```
φ₂ = asin( sin φ₁·cos(d/R) + cos φ₁·sin(d/R)·cos θ )
λ₂ = λ₁ + atan2( sin θ·sin(d/R)·cos φ₁,  cos(d/R) − sin φ₁·sin φ₂ )
λ₂ = ((λ₂ + 3π) mod 2π) − π   // normalise to −180…+180
```
All angles in radians; input/output in degrees.

#### 3. Implement `OverlapDetector`

**File:** `src/backend/Routing/OverlapDetector.cs` (new file)

**Intent:** Static helper that computes the fraction of a route that overlaps itself (i.e., the
route retraces the same road). Used by `LoopRouteGenerator` to filter or rank candidates against
the PRD's ≤10% repetition guardrail.

**Contract:** One public static method:
```csharp
public static double ComputeOverlapRatio(IReadOnlyList<RouteCoordinate> coordinates)
```

Returns a value in [0.0, 1.0]. Returns 0.0 for routes with fewer than 4 coordinates (cannot
overlap). Implementation: NTS `STRtree<LineSegment>` + 15 m buffer-intersect with directional
dot-product check (see research doc §5 for full algorithm). Tolerance constant:
`0.000135` degrees (valid for European latitudes 45–55°N).

#### 4. Implement `LoopRouteGenerator`

**File:** `src/backend/Routing/LoopRouteGenerator.cs` (new file)

**Intent:** The core business-logic service. Given a start coordinate, distance range, and
optional seed, fires three ORS calls in parallel with different bearing seeds, then picks the
best result within the user's distance range and overlap budget.

**Contract:** Constructor takes `IOpenRouteServiceClient` + `ILogger<LoopRouteGenerator>`.

Main method:
```csharp
public async Task<RoutingResult<RouteResult>> GenerateAsync(
    RouteCoordinate start,
    double minKm, double maxKm,
    int? seed,
    CancellationToken cancellationToken)
```

Algorithm:
1. Compute `targetMid = (minKm + maxKm) / 2.0 * 1000.0` (metres)
2. Compute waypoint radius: `radius = (targetMid / 2.0) * 0.45`
3. Base bearing: `seed % 360` if seed provided, else `0`
4. Three bearing sets (offset from base): `base+60°`, `base+180°`, `base+300°` (triangular)
5. For each bearing, compute 2 intermediate waypoints at `bearing` and `bearing+180°`
   using `WaypointCalculator.DestinationPoint`; form waypoint list `[start, wp1, wp2, start]`
6. Fire three `client.GetDirectionsAsync(waypoints, options, ct)` calls via `Task.WhenAll`,
   all sharing the passed-in `cancellationToken`
7. Collect successful results; for each, compute `overlapRatio = OverlapDetector.ComputeOverlapRatio`
8. **Selection**: prefer results where distance ∈ [minKm·1000, maxKm·1000] AND overlap ≤ 0.10;
   from those, pick the one closest to `targetMid`. If none pass both filters, relax overlap
   check and pick the distance-closest result. If all calls failed, return the first error.
9. Log warning if the returned result has overlap > 0.10 (so the relaxed-filter path is visible)

ORS options to pass:
```csharp
new OrsDirectionOptions(
    AvoidFeatures: ["steps", "ferries"],
    SteepnessDifficulty: 1)
```

#### 5. Define request/response DTOs and register endpoint

**File:** `src/backend/Program.cs`

**Intent:** Add `LoopRouteGenerator` to DI, define the `LoopRouteRequest` record, add input
validation, and map `POST /routes/loop`. Remove the dev-only `/routes/preview` endpoint (it was
scaffolding; the loop endpoint supersedes it).

**Contract:**

Request record:
```csharp
record LoopRouteRequest(
    double StartLon, double StartLat,
    double MinKm,    double MaxKm,
    int?   Seed);
```

Validation: `MinKm >= 5`, `MaxKm <= 300`, `MinKm < MaxKm`.

Endpoint: `app.MapPost("/routes/loop", async (LoopRouteRequest req, LoopRouteGenerator gen, CancellationToken ct) => { ... })` — wraps the request in a 4.5s `CancellationTokenSource` linked to the request `ct`, calls `gen.GenerateAsync`, maps errors to HTTP 400/502/504 with a structured `{ error, code }` body.

Error-to-HTTP mapping:
| ORS code | HTTP | Body `code` |
|----------|------|-------------|
| 2009 / 2010 (no route) | 422 | `"NO_ROUTE"` |
| 2004 (rate limit) | 429 | `"RATE_LIMITED"` |
| timeout | 504 | `"TIMEOUT"` |
| other | 502 | `"PROVIDER_ERROR"` |

Register: `builder.Services.AddScoped<LoopRouteGenerator>()` before `var app = builder.Build()`.

### Success Criteria

#### Automated Verification

- `dotnet build` exits 0
- `dotnet run` starts; `GET /health` returns 200
- `POST /routes/loop` with valid Vienna body (e.g., `StartLon:16.3725, StartLat:48.2085, MinKm:40, MaxKm:60, Seed:null`) returns 200 with a `RouteResult` JSON (verify via Swagger UI or curl)
- `POST /routes/loop` with invalid body (`MinKm:300, MaxKm:10`) returns 400

#### Manual Verification

- Swagger UI at `http://localhost:5098/swagger` shows `POST /routes/loop`
- Generated route distance is within [40, 60] km for the Vienna test
- Route geometry coordinates plausibly form a loop (first ≈ last coordinate)
- No obvious surface label errors (spot-check a few `segments[*].surface` values)

**Implementation Note:** Pause for manual verification before starting Phase 3.

---

## Phase 3: Frontend API Layer

### Overview

Wire the frontend to the backend loop endpoint and add the ORS geocoding proxy. No UI yet.

### Changes Required

#### 1. Add geocoding proxy Route Handler

**File:** `src/frontend/src/app/api/geocode/route.ts` (new file)

**Intent:** Server-side proxy that forwards queries to ORS `/geocode/autocomplete`, keeping the
API key out of the browser. Returns an empty `{ features: [] }` on error rather than
propagating ORS errors to the client (degraded-but-functional).

**Contract:** `export async function GET(request: Request)`. Reads `q` from `searchParams`;
returns empty features if `q.length < 2`. Reads `process.env.ORS_API_KEY` (server-side env var).
Forwards to:
```
https://api.openrouteservice.org/geocode/autocomplete?text={q}&api_key={key}&size=5
```
Response: pass-through of the ORS GeoJSON FeatureCollection JSON.

#### 2. Add `ORS_API_KEY` to frontend environment config

**File:** `src/frontend/.env.example` (new file, document required vars)

**Intent:** Document all required server-side env vars so developers know what to configure.
`ORS_API_KEY` must NOT be `NEXT_PUBLIC_` prefixed — it must stay server-side only.

**Contract:** Entries: `ORS_API_KEY=`, `VELO_API_URL=http://localhost:5098`

#### 3. Extend `routingApi.ts` with loop route function

**File:** `src/frontend/src/lib/routingApi.ts`

**Intent:** Add a `generateLoopRoute(params)` function that `POST`s to the backend
`/routes/loop` endpoint. Keeps the existing `fetchRoutePreview()` for now (it's harmless).

**Contract:** New export:
```typescript
export async function generateLoopRoute(params: {
  startLon: number; startLat: number;
  minKm: number;   maxKm: number;
  seed?: number;
}): Promise<RouteResult>
```
Throws a typed `RouteGenerationError` (new export from this file) on non-2xx, carrying the
backend's `{ error, code }` body. The `code` string is used by the UI to show specific messages.

#### 4. Add frontend types for the loop flow

**File:** `src/frontend/src/types/route.ts`

**Intent:** Add `LoopRouteRequest`, `RouteGenerationError`, and `GeocodingFeature` types
alongside the existing `RouteResult`. These are consumed by the API function and UI components.

**Contract:**
```typescript
export interface GeocodingFeature {
  geometry: { coordinates: [number, number] };
  properties: { label: string };
}

export class RouteGenerationError extends Error {
  constructor(public readonly code: string, message: string) { super(message); }
}
```

### Success Criteria

#### Automated Verification

- `npm run build` in `src/frontend/` exits 0
- `npm run lint` exits 0

#### Manual Verification

- `GET http://localhost:3000/api/geocode?q=Vienna` returns a GeoJSON FeatureCollection with
  ≥1 feature (requires frontend dev server running and `ORS_API_KEY` in `.env.local`)
- `generateLoopRoute` function visible in browser console when imported (no runtime errors on
  import)

**Implementation Note:** Pause for manual verification before starting Phase 4.

---

## Phase 4: Frontend UI

### Overview

Build the complete user-facing UI: geocoding search bar, distance range inputs, generate +
re-roll buttons, MapLibre route map, distance display, and error messages. Replace the scaffold
`page.tsx` with the real app.

### Changes Required

#### 1. Install frontend packages

**File:** `src/frontend/package.json` (via npm install)

**Intent:** Add MapLibre GL JS and its React wrapper.

**Contract:** Run in `src/frontend/`:
```bash
npm install maplibre-gl @vis.gl/react-maplibre
```

#### 2. Create `RouteApp` client root component

**File:** `src/frontend/src/components/RouteApp.tsx` (new file)

**Intent:** Top-level `"use client"` component that owns all interactive state: `startPoint`,
`minKm`, `maxKm`, `routeResult`, `isLoading`, `error`. Composes all sub-components. Allows
`page.tsx` to remain a server component.

**Contract:** Props: none. Renders the two-column desktop / stacked mobile layout using
Tailwind: `<div className="flex flex-col md:flex-row h-screen">` — left panel (form + result
info) and right panel (map, fills remaining space, `min-h-[50vh] md:min-h-0 md:flex-1`).

#### 3. Create `SearchBar` component

**File:** `src/frontend/src/components/SearchBar.tsx` (new file)

**Intent:** Text input that debounces keystrokes (300 ms), calls `/api/geocode`, shows a
dropdown of up to 5 suggestions, and calls an `onSelect(feature)` callback when the user picks
one.

**Contract:** Props:
```typescript
interface SearchBarProps {
  onSelect: (feature: GeocodingFeature) => void;
  placeholder?: string;
}
```
State: `query` (string), `suggestions` (GeocodingFeature[]), `isOpen` (bool).
Keyboard: arrow keys navigate suggestions; Enter picks the focused one; Escape closes dropdown.
Accessibility: `role="combobox"`, `aria-expanded`, `aria-activedescendant`.

#### 4. Create `RouteMap` component

**File:** `src/frontend/src/components/RouteMap.tsx` (new file)

**Intent:** `"use client"` MapLibre GL JS map. When `routeCoordinates` prop is non-null, adds a
GeoJSON `LineString` source + a line layer in the VeloRoute brand colour. Fits the map bounds to
the route on initial display. Shows a pin at the start coordinate when `startPoint` is set.

**Contract:** Props:
```typescript
interface RouteMapProps {
  startPoint: { lon: number; lat: number } | null;
  routeCoordinates: Array<{ longitude: number; latitude: number }> | null;
}
```
Initial map center: `[16.37, 48.21]` (Vienna) if no start point. Zoom: 10.
Tile style URL: `"https://tiles.openfreemap.org/styles/liberty"`.
Route layer: line colour `#2563eb` (Tailwind blue-600), width 4px.
Start pin: MapLibre `Marker` at `startPoint`.

#### 5. Create `RouteForm` component

**File:** `src/frontend/src/components/RouteForm.tsx` (new file)

**Intent:** Form containing `SearchBar`, min/max km `<input type="number">` fields, a Generate
button, and a Re-roll button (only shown after a result exists). Calls `onGenerate` with the
current form state when Generate or Re-roll is clicked (Re-roll passes a random `seed`).

**Contract:** Props:
```typescript
interface RouteFormProps {
  onGenerate: (params: { startLon: number; startLat: number; minKm: number; maxKm: number; seed?: number }) => void;
  isLoading: boolean;
  hasResult: boolean;
}
```
Validation: Generate button disabled if no start point selected or `minKm >= maxKm` or
`minKm < 5`. Min km default: 30; max km default: 60.

#### 6. Create `RouteInfoPanel` component

**File:** `src/frontend/src/components/RouteInfoPanel.tsx` (new file)

**Intent:** Shown below the form once a route is generated. Displays the route's total distance
in km (rounded to 1 decimal). Will later host the GPX download button (S-02).

**Contract:** Props:
```typescript
interface RouteInfoPanelProps {
  distanceMeters: number;
}
```

#### 7. Create `ErrorMessage` component

**File:** `src/frontend/src/components/ErrorMessage.tsx` (new file)

**Intent:** Inline error display under the Generate button. Maps `RouteGenerationError.code` to
a human-friendly message with a suggested action.

**Contract:** Props: `{ error: RouteGenerationError | null }`. Renders `null` when `error` is
null. Message map:
| code | message |
|------|---------|
| `NO_ROUTE` | "No road route found — try a different start point or adjust the distance range." |
| `RATE_LIMITED` | "Too many requests — please try again in a minute." |
| `TIMEOUT` | "Route generation timed out — please try again." |
| `NO_VALID_RESULT` | "Couldn't find a suitable loop — try a wider distance range." |
| _(other)_ | "Something went wrong — please try again." |

#### 8. Update `page.tsx` to render `RouteApp`

**File:** `src/frontend/src/app/page.tsx`

**Intent:** Replace the scaffold content with `<RouteApp />`. Keep `page.tsx` as a server
component (no `"use client"`).

**Contract:** Minimal:
```tsx
import RouteApp from '@/components/RouteApp';
export default function Page() { return <RouteApp />; }
```

### Success Criteria

#### Automated Verification

- `npm run build` exits 0 (no TypeScript errors, no missing imports)
- `npm run lint` exits 0

#### Manual Verification

- Desktop: form left, map right; form fields and labels readable; map fills the right panel
- Mobile (375 px viewport): form stacked above map; map is at least 50vh tall; all inputs tappable
- Typing "Vienna" in search bar shows ≥1 autocomplete suggestion within ~1s; selecting one
  places a pin on the map
- Entering `MinKm: 40, MaxKm: 60`, clicking Generate shows a spinner, then draws the route on
  the map with "41.3 km" (or similar) displayed
- Route polyline is visually closed (start ≈ end point visible)
- Re-roll button appears after generation; clicking it generates a different-shaped route
- With backend stopped: Generate shows "Something went wrong — please try again."
- With an invalid area (e.g., middle of the Atlantic): shows "No road route found" message

**Implementation Note:** Run both frontend and backend simultaneously during manual testing.

---

## Testing Strategy

### Automated Tests

No test runner is configured in either project. Do not add one in this change. Automated
verification uses `dotnet build` and `npm run build` as the primary gates.

### Manual Testing Steps

1. Start backend: `cd src/backend && dotnet run`
2. Start frontend: `cd src/frontend && npm run dev`
3. Set `ORS_API_KEY` and `VELO_API_URL` in `src/frontend/.env.local`
4. Open http://localhost:3000 on desktop and mobile viewport
5. Complete the full generation flow for Vienna (40–60 km)
6. Check distance label, route shape, and Re-roll behaviour
7. Verify error states by stopping the backend or entering an ocean coordinate

## Performance Considerations

The 5s NFR is enforced by the 4.5s `CancellationTokenSource` wrapping all three parallel ORS
calls in `LoopRouteGenerator.GenerateAsync`. The per-call resilience retry (2 retries × 5s) is
effectively bypassed when the outer token fires at 4.5s — this is intentional. The three
parallel calls should each complete in 1–3s under normal ORS load, keeping the typical
response well within 5s.

## Migration Notes

None — no existing data or persistent state. The only breaking change is `SurfaceType` enum
member renames; the only consumer is `OpenRouteServiceClient.cs` itself.

## References

- Research: `context/changes/loop-route-generation/research.md`
- Algorithm decision: `context/foundation/loop-route-algorithm.md`
- ORS API: `https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/`
- Haversine formula: `https://www.movable-type.co.uk/scripts/latlong.html`
- OpenFreeMap tiles: `https://openfreemap.org`

---

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend Foundation — SurfaceType Fix + Multi-Waypoint Client

#### Automated

- [x] 1.1 `dotnet build` exits 0 with no warnings — 5091af8
- [x] 1.2 `dotnet run` starts; `GET /health` returns 200 — 5091af8

#### Manual

- [x] 1.3 `GET /routes/preview` returns correct surface labels (e.g. `"Asphalt"` for Vienna roads) — 5091af8

### Phase 2: Backend — Loop Route Generation Service + Endpoint

#### Automated

- [x] 2.1 `dotnet build` exits 0 — 24fe250
- [x] 2.2 `GET /health` returns 200 after `dotnet run` — 24fe250
- [x] 2.3 `POST /routes/loop` with Vienna body returns 200 with RouteResult JSON — 24fe250
- [x] 2.4 `POST /routes/loop` with invalid body (`MinKm:300, MaxKm:10`) returns 400 — 24fe250

#### Manual

- [x] 2.5 Swagger UI shows `POST /routes/loop` — 24fe250
- [x] 2.6 Generated distance is within [40, 60] km for the Vienna test — 24fe250
- [x] 2.7 Route geometry plausibly forms a loop (first ≈ last coordinate) — 24fe250

### Phase 3: Frontend API Layer

#### Automated

- [x] 3.1 `npm run build` exits 0
- [x] 3.2 `npm run lint` exits 0

#### Manual

- [x] 3.3 `GET /api/geocode?q=Vienna` returns ≥1 GeoJSON feature

### Phase 4: Frontend UI

#### Automated

- [ ] 4.1 `npm run build` exits 0
- [ ] 4.2 `npm run lint` exits 0

#### Manual

- [ ] 4.3 Desktop layout: form left, map right; map fills right panel
- [ ] 4.4 Mobile layout: form stacked above map; map ≥50vh; all inputs tappable
- [ ] 4.5 Search bar autocomplete shows suggestions within ~1s; selecting places map pin
- [ ] 4.6 Full generation flow works: spinner → route on map → distance label displayed
- [ ] 4.7 Route polyline visually closed
- [ ] 4.8 Re-roll generates a different-shaped route
- [ ] 4.9 Backend stopped → "Something went wrong" error shown
- [ ] 4.10 Ocean coordinate → "No road route found" error shown
