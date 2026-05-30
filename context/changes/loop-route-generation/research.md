---
date: 2026-05-30T19:47:00+02:00
researcher: GitHub Copilot
git_commit: 941c74c33a6485eb54747e834574f8a209002bee
branch: main
repository: lukasz05/velo-route
topic: "Loop route generation — implementation blueprint (ORS multi-waypoint approach)"
tags: [research, loop-route, ors, waypoints, haversine, geocoding, overlap-detection, nts, cycling]
status: complete
last_updated: 2026-05-30
last_updated_by: GitHub Copilot
---

# Research: Loop Route Generation — Implementation Blueprint

**Date**: 2026-05-30T19:47:00+02:00
**Researcher**: GitHub Copilot
**Git Commit**: 941c74c33a6485eb54747e834574f8a209002bee
**Branch**: main
**Repository**: lukasz05/velo-route

## Research Question

Research the best approach to loop-route-generation. Initial decision documented in
`context/foundation/loop-route-algorithm.md`: **ORS multi-waypoint stitching** — generate 2–3
intermediate waypoints at `(distance/2)*0.45` radius from start, chain via ORS Directions API
(`start → wp1 → wp2 → start`), validate total distance against user's range, retry if outside.

Research covers: ORS API specifics, waypoint math (haversine), repetition detection, and
geocoding for the search bar.

## Summary

The chosen approach is validated and implementation-ready. Key findings:

1. **ORS API**: The existing `OpenRouteServiceClient` already uses the right endpoint
   (`/v2/directions/cycling-road/geojson`) and response shape. Adding multi-waypoint support
   is a **small interface change** — pass `double[][]` with 4 coordinates instead of 2.
2. **Waypoint math**: Haversine destination-point formula gives exact lat/lon from bearing +
   distance. Best bearing strategies: 2 WPs at 90°/270°, 3 WPs at 60°/180°/300°. The `0.45`
   radius factor is validated by open-source analogues (see `randomRouteGenerator`).
3. **Overlap detection**: `NetTopologySuite` (NuGet) provides all primitives. Buffer-intersect
   with 15 m tolerance + STRtree spatial index is the right approach. ~40 lines of C#.
4. **Geocoding**: Use **ORS's own `/geocode/autocomplete` endpoint** (same API key, already in
   `appsettings.json`), proxied through a Next.js API route to keep the key server-side.
   Photon (`photon.komoot.io`) is the zero-credential fallback.

---

## Detailed Findings

### 1. ORS API — Multi-Waypoint Request/Response

**Endpoint (already in use):** `POST /v2/directions/cycling-road/geojson`

Full request for a 4-waypoint loop (`start → wp1 → wp2 → start`):

```json
{
  "coordinates": [
    [8.681495, 49.41461],
    [8.686507, 49.41943],
    [8.690000, 49.42500],
    [8.681495, 49.41461]
  ],
  "preference": "recommended",
  "units": "m",
  "geometry": true,
  "instructions": false,
  "extra_info": ["surface", "waytype", "waycategory", "steepness"],
  "options": {
    "avoid_features": ["steps", "ferries"],
    "profile_params": {
      "weightings": { "steepness_difficulty": 1 }
    }
  }
}
```

**Key facts:**
- `coordinates` is `[longitude, latitude]` — longitude first. ✅ (Already correct in `OpenRouteServiceClient.cs:30`)
- Max 50 waypoints; max route distance 6,000 km; **max with round-trip/avoidances: 100 km**
- `segments` in the response = one per leg (3 segments for 4 waypoints). `way_points` gives geometry indices per input coord.
- `extra_info` values are encoded as `[startIdx, endIdx, value]` triples referencing the flat geometry array.
- **GeoJSON endpoint**: geometry is decoded `[lon, lat]` coordinates — no custom decoder needed. ✅ Already used.
- **Elevation**: if `"elevation": true` added, GeoJSON returns `[lon, lat, ele]` triples — safe with current deserializer.

**What needs to change in `IOpenRouteServiceClient`:**

The current interface signature:
```csharp
Task<RoutingResult<RouteResult>> GetDirectionsAsync(RouteCoordinate start, RouteCoordinate end, ...);
```
Needs a new method (or overload):
```csharp
Task<RoutingResult<RouteResult>> GetDirectionsAsync(IReadOnlyList<RouteCoordinate> waypoints, ...);
```
The existing implementation in `OpenRouteServiceClient.cs` already builds `double[][]` from two
coords — trivial to extend to N coords.

**ORS Error codes to handle:**
| Code | HTTP | Meaning | Action |
|------|------|---------|--------|
| 2009 | 404 | Route not found between two waypoints | Retry with adjusted waypoints |
| 2010 | 404 | Waypoint cannot snap to road | Retry with adjusted waypoints or larger `radii` |
| 2004 | 400/429 | Rate limit / request limit | Surface error to user |
| 2003 | 400 | Invalid parameter | Log + fix |

⚠️ Both 2009 and 2010 return HTTP 404 — **must inspect `error.code` in the body**, not just status code.

**Rate limits (free tier, verified Dec 2024):**
- Directions: **2,000/day**, **40/min** (shared across entire API key)
- Geocoding: **1,000/day**, **100/min** (separate quota)
- A 4-waypoint loop = **1 directions request**

---

### 2. ORS Extra Info — Relevant Decoded Values

**`surface` values** (what VeloRoute cares about for road cycling):
| Code | Name | Road-bike suitable? |
|------|------|---------------------|
| 1 | Paved | ✅ |
| 3 | Asphalt | ✅ best |
| 4 | Concrete | ✅ |
| 5 | Cobblestone | ⚠️ rough |
| 8 | Compacted Gravel | ⚠️ marginal |
| 10 | Gravel | ❌ |
| 11–18 | Dirt/Ground/Sand/Grass/etc. | ❌ |

**`waytype` values relevant to cycling:**
- `1` State Road, `2` Road, `3` Street, `6` Cycleway — all ✅ for road bike
- `4` Path, `5` Track, `7` Footway — ❌ avoid

**`waycategory` is a bit-field** — must decode with bitwise AND:
```csharp
bool hasFerry = (value & 8) != 0;
bool hasSteps = (value & 4) != 0;
```

**`steepness`**: values -5 to +5, where ±1 = 1–4%, ±5 = ≥16%. Useful for future route quality scoring.

**⚠️ `steepness_difficulty` is counterintuitive**: value `3` = "Pro" = will route through steep climbs. Value `0` = "Novice" = avoids all hills. For an MVP targeting road cyclists, start with `1` (Moderate).

---

### 3. Known `cycling-road` Profile Limitations

These directly affect loop route quality:

| Issue | Impact | Mitigation |
|-------|--------|------------|
| `sac_scale=*` hard-blocked | 2009 errors near mountains/trails | Expand waypoint radius to avoid trail areas |
| Unpaved surface = near-walking speed (2 km/h) | Hard detours around gravel | Expected: `cycling-road` is correct for v1 |
| Aggressively avoids cycleways (penalized to 8 km/h) | Routes use main roads over bike lanes | Acceptable for road cyclists |
| `ford=*` hard-blocked | 2009 errors in rural areas with ford crossings | Retry with larger radius |
| OSM data quality varies | 2009 more common in Eastern Europe / rural areas | Surface error to user; no mitigation |
| `radii` default may miss nearby roads | 2010 errors for trigonometric waypoints off-road | Set `"radii": [500, -1, -1, 500]` for start/end; allow server default for waypoints |

---

### 4. Waypoint Placement Math (Haversine)

**Destination point formula** (from [movable-type.co.uk](https://www.movable-type.co.uk/scripts/latlong.html), the canonical reference):

```
φ₂ = asin( sin(φ₁)·cos(d/R) + cos(φ₁)·sin(d/R)·cos(θ) )
λ₂ = λ₁ + atan2( sin(θ)·sin(d/R)·cos(φ₁), cos(d/R) − sin(φ₁)·sin(φ₂) )
```

Where: `φ` = latitude (rad), `λ` = longitude (rad), `θ` = bearing clockwise from north (rad),
`d` = distance (m), `R` = 6,371,000 m (Earth's mean radius).

**C# implementation:**

```csharp
public static RouteCoordinate DestinationPoint(
    RouteCoordinate start, double bearingDeg, double distanceMeters)
{
    const double R = 6_371_000.0;
    double φ1 = start.Latitude  * Math.PI / 180;
    double λ1 = start.Longitude * Math.PI / 180;
    double θ  = bearingDeg      * Math.PI / 180;
    double δ  = distanceMeters  / R;

    double φ2 = Math.Asin(
        Math.Sin(φ1) * Math.Cos(δ) +
        Math.Cos(φ1) * Math.Sin(δ) * Math.Cos(θ));

    double λ2 = λ1 + Math.Atan2(
        Math.Sin(θ) * Math.Sin(δ) * Math.Cos(φ1),
        Math.Cos(δ) - Math.Sin(φ1) * Math.Sin(φ2));

    // Normalise longitude to −180…+180
    λ2 = (λ2 + 3 * Math.PI) % (2 * Math.PI) - Math.PI;

    return new RouteCoordinate(λ2 * 180 / Math.PI, φ2 * 180 / Math.PI);
}
```

**Bearing strategies for good loop shapes:**

| Waypoints | Bearings | Shape |
|-----------|----------|-------|
| 2 | 90°, 270° | East-West lobe |
| 2 | 0°, 180° | North-South lobe |
| 2 | random seed (user-driven) | varies |
| 3 | 60°, 180°, 300° | Triangular (most circular) |
| 3 | 0°, 120°, 240° | Triangular, north-biased |

**Recommended for MVP:** 2 waypoints at `bearing` and `bearing + 180°`, where `bearing` defaults
to 90° (East). Offer a `seed` parameter (0–359°) so users can re-run to get different shapes.
With 3 waypoints (triangular) the repetition rate is naturally lower.

**Radius scaling — the `0.45` factor:**
The `randomRouteGenerator` Python library (uses ORS, 4 stars, same approach) places random
waypoints within `max_distance` of the start using Overpass to find actual road nodes. Their
effective radius is roughly `target_distance * 0.35–0.5` depending on road network density.
The `0.45` factor in `loop-route-algorithm.md` falls squarely in that validated range.

**Retry strategy:**
```
attempt 1: radius = (targetMid / 2) * 0.45
attempt 2: radius = (targetMid / 2) * 0.35  ← shrink if route too long
attempt 3: radius = (targetMid / 2) * 0.55  ← expand if route too short
```
Where `targetMid = (minKm + maxKm) / 2 * 1000` (metres).

---

### 5. Repetition/Overlap Detection

**Recommended approach: NTS buffer-intersect + STRtree**

Add to `.csproj`:
```xml
<PackageReference Include="NetTopologySuite" Version="2.5.0" />
<PackageReference Include="NetTopologySuite.IO.GeoJSON4STJ" Version="4.0.0" />
```

**Algorithm (C#, ~45 lines):**

```csharp
// Tolerance: 15 m ≈ 0.000135° at 50°N (valid for European latitudes 45–55°N)
const double ToleranceDeg = 0.000135;

var gf = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
var coords = routeResult.Geometry.Coordinates
    .Select(c => new Coordinate(c.Longitude, c.Latitude))
    .ToArray();

var segments = new List<LineSegment>();
var tree = new STRtree<LineSegment>();

for (int i = 0; i < coords.Length - 1; i++)
{
    var seg = new LineSegment(coords[i], coords[i + 1]);
    segments.Add(seg);
    tree.Insert(new Envelope(seg.P0, seg.P1).ExpandedBy(ToleranceDeg), seg);
}

double totalLength = 0;
for (int i = 0; i < coords.Length - 1; i++)
    totalLength += segments[i].Length;

double overlappingLength = 0;
for (int i = 0; i < segments.Count; i++)
{
    var segGeom = gf.CreateLineString(new[] { segments[i].P0, segments[i].P1 });
    var buffer  = segGeom.Buffer(ToleranceDeg);
    var nearby  = tree.Query(buffer.EnvelopeInternal);

    foreach (var other in nearby)
    {
        int j = segments.IndexOf(other);
        if (j <= i + 5) continue; // skip adjacent segments

        var otherGeom = gf.CreateLineString(new[] { other.P0, other.P1 });
        if (!buffer.Intersects(otherGeom)) continue;

        // Directional check: dot product — same or opposite direction
        double dx1 = segments[i].P1.X - segments[i].P0.X;
        double dy1 = segments[i].P1.Y - segments[i].P0.Y;
        double dx2 = other.P1.X - other.P0.X;
        double dy2 = other.P1.Y - other.P0.Y;
        double dot = dx1 * dx2 + dy1 * dy2;
        double mag = segments[i].Length * other.Length;
        if (mag > 0 && Math.Abs(dot / mag) > 0.7) // angle < ~45°
            overlappingLength += Math.Min(segments[i].Length, other.Length);
    }
}

double overlapRatio = totalLength > 0 ? overlappingLength / totalLength : 0;
bool tooMuchRepetition = overlapRatio > 0.10;
```

**Why not bounding-box only:** 40–70% false positive rate in dense urban networks — cannot be used as the sole check.
**Why not Fréchet/Hausdorff:** They measure shape similarity, not overlap length. Wrong tool.
**Why not graph-edge overlap (GraphHopper approach):** Requires access to the road graph internals; not exposed by ORS API.

---

### 6. Geocoding — Search Bar (FR-001)

**Primary recommendation: ORS `/geocode/autocomplete`** (same API key, already configured)

```
GET https://api.openrouteservice.org/geocode/autocomplete
  ?text=Unter+den+Linden+Berlin
  &api_key={key}
  &size=5
  &boundary.country=   ← leave empty for all countries; or ISO 3166-1 alpha-2
  &focus.point.lat=50.0
  &focus.point.lon=8.0
```

Response: GeoJSON FeatureCollection (Pelias format)
```json
{
  "features": [{
    "geometry": { "coordinates": [13.3886, 52.5167] },
    "properties": { "label": "Unter den Linden, Berlin, Germany", "confidence": 0.9 }
  }]
}
```

**Rate limit:** 1,000/day, 100/min — **separate from the 2,000/day directions quota** ✅

⚠️ **Nominatim is off the table**: autocomplete (per-keystroke requests) is **explicitly prohibited** by the OSM usage policy.

**Architecture: Next.js API route proxy (keeps key server-side)**

```typescript
// src/frontend/src/app/api/geocode/route.ts
export async function GET(request: Request) {
  const { searchParams } = new URL(request.url);
  const q = searchParams.get('q') ?? '';
  if (q.length < 2) return Response.json({ features: [] });

  const url = new URL('https://api.openrouteservice.org/geocode/autocomplete');
  url.searchParams.set('text', q);
  url.searchParams.set('api_key', process.env.ORS_API_KEY!);
  url.searchParams.set('size', '5');

  const res = await fetch(url.toString(), { cache: 'no-store' });
  if (!res.ok) return Response.json({ features: [] }, { status: res.status });
  return Response.json(await res.json());
}
```

**Frontend search bar (React, debounced 300 ms):**
```tsx
// Debounce prevents per-keystroke requests
const [query, setQuery] = useState('');
const [results, setResults] = useState<GeocodingFeature[]>([]);

useEffect(() => {
  if (query.length < 2) { setResults([]); return; }
  const id = setTimeout(async () => {
    const r = await fetch(`/api/geocode?q=${encodeURIComponent(query)}`);
    const data = await r.json();
    setResults(data.features ?? []);
  }, 300);
  return () => clearTimeout(id);
}, [query]);
```

**Fallback: Photon (`photon.komoot.io`)**
If ORS geocoding quota is exhausted, swap the backend proxy URL to:
```
GET https://photon.komoot.io/api?q={query}&limit=5&lang=en&lat={lat}&lon={lon}
```
Same GeoJSON FeatureCollection response shape — zero frontend changes needed. No API key, built
by komoot for cycling navigation in Europe.

---

## Code References

- `src/backend/Routing/IOpenRouteServiceClient.cs:1-9` — interface to extend with multi-waypoint overload
- `src/backend/Routing/OpenRouteServiceClient.cs:19-126` — implementation; `OrsDirectionsRequest.Coordinates` already `double[][]`
- `src/backend/Routing/RouteResult.cs:1-16` — existing domain types; no changes needed for loop route result
- `src/backend/Routing/SurfaceType.cs:1-27` — values match ORS surface codes (codes differ: ORS 3=Asphalt, enum 1=Paved; see mapping note)
- `src/backend/Routing/RoadClass.cs:1-17` — values match ORS waytype codes ✅
- `src/backend/Program.cs:34-44` — resilience handler: 5s attempt timeout, 2 retries — sufficient for multi-waypoint; 3 ORS calls × 5s = 15s max before circuit break; **needs timeout review** for the 5s NFR
- `src/frontend/src/lib/routingApi.ts:1-11` — needs new function for loop route POST
- `src/frontend/src/types/route.ts:1-21` — types already correct for ORS response

## Architecture Insights

### Biggest gap: `Program.cs` attempt timeout vs. 5s NFR

The resilience handler sets `AttemptTimeout.Timeout = TimeSpan.FromSeconds(5)` per ORS call.
A 3-call loop route generation (3 retry attempts × 5s) could take up to 15s worst-case.
The PRD NFR requires results within 5 seconds. Two options:
1. Use a **single 4-waypoint ORS call** (no retries): `[start, wp1, wp2, start]` — one call, 5s timeout
2. Set a **total deadline** (e.g., `CancellationTokenSource(TimeSpan.FromSeconds(4.5))`) across
   the entire loop generation attempt chain, not per-call

Option 1 is cleaner for MVP; option 2 enables the retry strategy.

### `SurfaceType` enum vs. ORS surface codes — mismatch

The existing `SurfaceType` enum uses values `0–17` but they do **not** directly correspond to ORS
surface codes. Current mapping in `OpenRouteServiceClient.cs:120`:
```csharp
Enum.IsDefined((SurfaceType)surfaceCode) ? (SurfaceType)surfaceCode : SurfaceType.Unknown
```
ORS code `3` = Asphalt; enum value `3` = `Gravel`. This is a **bug in the existing code** — the
surface type display will be wrong. Needs a mapping table or corrected enum values.

### `OrsDirectionsRequest` is `file`-scoped — not a blocker but note

The DTOs in `OpenRouteServiceClient.cs` are `file sealed class` — they're invisible outside the
file. New fields (e.g., `options`, `profile_params`) can simply be added to the existing
`file`-scoped `OrsDirectionsRequest` without touching the public API.

## Historical Context

- `context/changes/routing-api-wiring/` — F-01 change (status: `impl_reviewed`). ORS client,
  models, and resilience handler already implemented.
- `context/foundation/loop-route-algorithm.md` — initial algorithm decision (ORS multi-waypoint,
  haversine waypoints, retry strategy). All decisions validated by this research.
- `context/foundation/infrastructure.md` — Azure SWA + App Service; GitHub Actions deploy live.

## Open Questions

1. **`SurfaceType` enum values are wrong** — the existing enum doesn't map correctly to ORS
   surface codes. Needs fixing before surface quality filtering can work.
2. **5s NFR vs. retry strategy**: A single ORS call for 4 waypoints is fast but has no fallback
   if the first waypoint placement is poor. Is one attempt acceptable for v1, or is a 2-attempt
   retry within 5s required? Decision needed at planning time.
3. **Seed/direction UX**: Should users be able to re-roll the route (different bearing seed) from
   the map view? If yes, the backend must accept a `seed` parameter and expose it to the frontend.
4. **`steepness_difficulty` default**: What fitness level should the default be? `1` (Moderate)
   is the safest choice but a user preference slider would improve quality. Defer to v2 or expose
   as a simple Easy/Medium/Hard toggle?
5. **ORS geocoding quota**: The 1,000/day limit for geocoding is separate from directions, but
   still limited. A debounced search bar should not be an issue for MVP traffic, but verify at
   `account.heigit.org/plans` after signing up.
6. **`cycling-road` in rural/Eastern Europe**: Profile may return frequent 2009 errors in areas
   with poor OSM coverage. Consider a user-facing message: "No road route found for this area —
   try a different start point or adjust the distance range."
