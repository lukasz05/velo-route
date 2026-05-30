# Routing API Wiring Implementation Plan

## Overview

Wire OpenRouteService (ORS) as the road-network data provider for VeloRoute. This delivers a typed HTTP client registered in the .NET backend, an internal data contract (`RouteResult`) that normalises ORS GeoJSON into domain types the routing algorithm (S-01) can consume, basic resilience, and end-to-end validation via a dev-only backend endpoint + a matching frontend fetch page.

## Current State Analysis

The backend is a minimal .NET 10 scaffold with only `/health` and `/weatherforecast`. No HTTP clients, no services, no typed clients exist — `Program.cs` is 55 lines. The only installed NuGet package is `Microsoft.AspNetCore.OpenApi`. The frontend is an equally bare Next.js 15 / React 19 scaffold with no API utilities, no types, and no environment variable wiring.

## Desired End State

- `IOpenRouteServiceClient` is registered in .NET DI and callable from any endpoint or service
- The client calls ORS, maps the GeoJSON response to an internal `RouteResult` that exposes normalised `SurfaceType` and `RoadClass` enums — no ORS-specific codes leak to callers
- `GET /routes/preview` (dev-only, hardcoded fixture coordinates) returns a `RouteResult` JSON confirming the wire is alive
- The Next.js app has a `src/app/dev/page.tsx` that fetches `/routes/preview` and renders the contract — proving the full backend-to-frontend data path works
- A real ORS response with `surface` + `waytypes` extras has been visually inspected and confirmed sufficient for S-01 route scoring

### Key Discoveries

- `Program.cs` — only services registered: `AddOpenApi()` + `AddCors()`. No `AddHttpClient` or `AddOptions`. All additions go here (`src/backend/Program.cs:1-15`)
- `bootstrap-scaffold.csproj` — single package reference (`Microsoft.AspNetCore.OpenApi`). Resilience package must be added (`src/backend/bootstrap-scaffold.csproj:10-12`)
- `src/frontend/src/app/` — only default scaffold files (`layout.tsx`, `page.tsx`, `globals.css`). New `dev/page.tsx` goes here without touching the homepage
- No `.env.example` exists in `src/frontend/` — must be created

## What We're NOT Doing

- No loop-route generation algorithm (S-01)
- No geocoding / start-point search (S-01)
- No route input UI or form (S-01)
- No GPX export (S-02)
- No interactive map display (S-01)
- No OSM Overpass API integration — ORS is the selected provider
- No secrets manager or Azure Key Vault setup — `ORS__ApiKey` env var is sufficient for v1

## Implementation Approach

Three phases in dependency order:

1. Backend wiring: NuGet + options + data contract + typed client + registration
2. Backend smoke endpoint: `GET /routes/preview` inside `IsDevelopment()` using fixture coordinates
3. Frontend validation: server-side fetch page + TypeScript contract types

The client owns all ORS knowledge. Above `OpenRouteServiceClient`, no file knows about ORS JSON shapes, endpoint paths, or numeric codes.

## Critical Implementation Details

**ORS wire contract** — three non-obvious facts for the `OpenRouteServiceClient` implementer:
1. The GeoJSON endpoint is `POST /v2/directions/cycling-road/geojson` — not a GET, and the `/geojson` suffix changes the response shape to a standard `FeatureCollection`.
2. The `Authorization` header is the raw API key — no `Bearer` prefix. Example: `Authorization: your-api-key`.
3. ORS `extras` (`surface`, `waytypes`) in the response are span tuples — each entry is `[fromIndex, toIndex, code]` where `fromIndex`/`toIndex` are indices into `geometry.coordinates`. The client must iterate these spans to build `RouteWaySegment` entries; they are not pre-sliced per segment. Note the asymmetry: the request `extra_info` array uses `"waytype"` (no trailing `s`), while the response key is `"waytypes"` (with `s`).

**Retry policy must exclude client errors** — the resilience handler must NOT retry on `401`, `403`, or `429` responses. Retrying auth failures burns quota and makes rate-limit situations worse. Configure `ShouldHandle` to trigger only on network errors, `408`, and `5xx`.

---

## Phase 1: ORS HTTP client and data contract

### Overview

Add the NuGet resilience package, define the internal data contract types, create the typed HTTP client and interface, configure `appsettings.json`, and register everything in `Program.cs`.

### Changes Required

#### 1. Add NuGet package

**File**: `src/backend/bootstrap-scaffold.csproj`

**Intent**: Add `Microsoft.Extensions.Http.Resilience` so the typed HTTP client can be wired with a retry + circuit-breaker pipeline.

**Contract**: Add a `<PackageReference>` for `Microsoft.Extensions.Http.Resilience` (latest stable for .NET 10, ≥ 9.0.0).

---

#### 2. Create `OpenRouteServiceOptions`

**File**: `src/backend/Routing/OpenRouteServiceOptions.cs`

**Intent**: Bind the `ORS` config section to a strongly-typed options object injected into the HTTP client.

**Contract**: `public sealed class OpenRouteServiceOptions` with `string BaseUrl { get; set; }` defaulting to `"https://api.openrouteservice.org"` and `string ApiKey { get; set; }` defaulting to `string.Empty`.

---

#### 3. Create internal data contract types

**File**: `src/backend/Routing/RouteResult.cs`

**Intent**: Define the normalised internal types that represent a route result — these are the types S-01 will consume. No ORS-specific shapes or integer codes appear here.

**Contract**: Define in this file:
- `sealed record RouteResult(RouteGeometry Geometry, double DistanceMeters, IReadOnlyList<RouteWaySegment> Segments)`
- `sealed record RouteGeometry(IReadOnlyList<RouteCoordinate> Coordinates)`
- `sealed record RouteCoordinate(double Longitude, double Latitude)`
- `sealed record RouteWaySegment(int FromIndex, int ToIndex, SurfaceType Surface, RoadClass RoadClass)`

**File**: `src/backend/Routing/SurfaceType.cs`

**Intent**: Enum mapping ORS surface codes to human-readable values.

**Contract**: `public enum SurfaceType` with at minimum: `Unknown = 0`, `Paved = 1`, `Unpaved = 2`, `Gravel = 3`, `Ground = 4`, `Dirt = 5`, `Rock = 6`. Values must match ORS surface code integers.

**File**: `src/backend/Routing/RoadClass.cs`

**Intent**: Enum mapping ORS waytype codes to road classification values used for route scoring in S-01.

**Contract**: `public enum RoadClass` with at minimum: `Unknown = 0`, `StateRoad = 1`, `Road = 2`, `Street = 3`, `Path = 4`, `Track = 5`, `Cycleway = 6`, `FootPath = 7`, `Steps = 8`. Values must match ORS waytype code integers.

---

#### 4. Create `RoutingResult<T>`

**File**: `src/backend/Routing/RoutingResult.cs`

**Intent**: A lightweight discriminated-union result type that lets the HTTP client return success or a typed error without throwing exceptions for expected failures (provider errors, rate limits, network timeouts).

**Contract**: `public sealed class RoutingResult<T>` with private constructor; `Value T?` and `Error RoutingError?` properties; `bool IsSuccess => Error is null`; static `Success(T value)` and `Failure(RoutingError error)` factory methods. Paired with `public sealed record RoutingError(string Code, string Message)`.

---

#### 5. Create `IOpenRouteServiceClient`

**File**: `src/backend/Routing/IOpenRouteServiceClient.cs`

**Intent**: Define the interface the rest of the backend uses to request route directions. One method for now — expanded in S-01.

**Contract**: `Task<RoutingResult<RouteResult>> GetDirectionsAsync(RouteCoordinate start, RouteCoordinate end, CancellationToken cancellationToken = default)`

---

#### 6. Create `OpenRouteServiceClient`

**File**: `src/backend/Routing/OpenRouteServiceClient.cs`

**Intent**: Implement the ORS HTTP client. This file owns all ORS-specific knowledge: endpoint path, request body shape, response deserialization, and mapping of ORS extras span tuples to typed `RouteWaySegment` entries. Nothing outside this file should know the ORS JSON schema.

**Contract**: `internal sealed class OpenRouteServiceClient : IOpenRouteServiceClient`. Constructor accepts `HttpClient httpClient`. Implements `GetDirectionsAsync` by:
- POSTing to `/v2/directions/cycling-road/geojson` with JSON body `{ "coordinates": [[lon,lat],[lon,lat]], "extra_info": ["surface","waytype"], "instructions": false }`
- Deserialising the GeoJSON FeatureCollection response
- Mapping `features[0].geometry.coordinates` → `RouteCoordinate[]` (note: ORS coordinates are `[lon, lat]` order)
- Mapping `features[0].properties.summary.distance` → `DistanceMeters`
- Merging `extras.surface.values` and `extras.waytypes.values` span tuples into `RouteWaySegment[]` (iterate all unique span boundaries, cast integer codes to `SurfaceType` / `RoadClass` enums)
- Returning `RoutingResult<RouteResult>.Success(...)` on 200, `RoutingResult<RouteResult>.Failure(...)` on all errors (4xx, 5xx, network). Include ORS `error.code` and `error.message` in the `RoutingError` for 4xx/5xx

---

#### 7. Update `appsettings.json`

**File**: `src/backend/appsettings.json`

**Intent**: Document the `ORS` configuration section so operators know what env vars to set. The API key is left empty — it is always supplied via `ORS__ApiKey` environment variable in real environments.

**Contract**: Add top-level `"ORS"` object with `"BaseUrl": "https://api.openrouteservice.org"` and `"ApiKey": ""`. The double-underscore env var `ORS__ApiKey` overrides `ApiKey` at runtime via .NET configuration layering.

---

#### 8. Register in `Program.cs`

**File**: `src/backend/Program.cs`

**Intent**: Register `OpenRouteServiceOptions`, the typed HTTP client, and the resilience pipeline so the DI container can satisfy `IOpenRouteServiceClient` anywhere.

**Contract**: Before `var app = builder.Build()`:
1. `builder.Services.Configure<OpenRouteServiceOptions>(builder.Configuration.GetSection("ORS"))`
2. `builder.Services.AddHttpClient<IOpenRouteServiceClient, OpenRouteServiceClient>(...)` — configure `BaseAddress` from options; set `Authorization` header from `ApiKey`
3. Chain `.AddResilienceHandler(...)` on the `IHttpClientBuilder` — configure `AddTimeout(5s)` + `AddRetry` (2 attempts, exponential backoff, `ShouldHandle` on network errors / 408 / 5xx only — explicitly exclude 401, 403, 429) + `AddCircuitBreaker` (open after 50% failure rate over 3+ requests in a 10s window; break for 30s)

### Success Criteria

#### Automated Verification

- `dotnet build` compiles with zero errors and zero warnings from new files
- `dotnet run` starts and `/health` returns `{"status":"ok"}` (confirms DI registration doesn't break startup)

#### Manual Verification

- `appsettings.json` has the `ORS` section with `BaseUrl` and empty `ApiKey`

**Implementation Note**: After this phase, pause for manual confirmation before proceeding.

---

## Phase 2: Dev-only smoke-test endpoint

### Overview

Add `GET /routes/preview` behind `IsDevelopment()` guard. It calls `IOpenRouteServiceClient` with hardcoded Vienna fixture coordinates, returns the mapped `RouteResult` as JSON, and proves the full client-to-ORS wire works with a real API key.

### Changes Required

#### 1. Add `/routes/preview` endpoint

**File**: `src/backend/Program.cs`

**Intent**: Expose a dev-only endpoint that calls ORS with fixed test coordinates and returns the normalised `RouteResult`, making the wire verifiable with a single `curl`.

**Contract**: Inside the existing `if (app.Environment.IsDevelopment())` block, add:
```csharp
app.MapGet("/routes/preview", async (IOpenRouteServiceClient client, CancellationToken ct) =>
{
    var start = new RouteCoordinate(16.3725, 48.2085); // Vienna
    var end   = new RouteCoordinate(16.3900, 48.2200);
    var result = await client.GetDirectionsAsync(start, end, ct);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Problem(result.Error!.Message, statusCode: 502);
});
```

### Success Criteria

#### Automated Verification

- `dotnet build` compiles with zero errors

#### Manual Verification

- With `ORS__ApiKey` set in the environment: `curl http://localhost:5098/routes/preview` returns HTTP 200 with a JSON body containing `distanceMeters`, `geometry.coordinates` array, and `segments` array with `surface` and `roadClass` fields
- Inspect the returned `segments` array: confirm `surface` and `roadClass` values are present and non-`Unknown` for a real Vienna route — this validates that ORS `extra_info` span mapping is working and that the data is rich enough for S-01 route scoring
- Confirm the endpoint is NOT accessible when `ASPNETCORE_ENVIRONMENT` is not `Development`

**Implementation Note**: After this phase, visually inspect the raw response. If `surface` or `roadClass` fields are all `Unknown`, the ORS extras mapping is broken — fix before proceeding to Phase 3.

---

## Phase 3: Frontend fetch validation

### Overview

Add a server-side `src/app/dev/page.tsx` in the Next.js frontend that fetches `/routes/preview` and renders the `RouteResult` as formatted JSON. This proves the TypeScript contract matches the backend shape and that the `VELO_API_URL` env var wiring works. The dev page is not linked from the main UI and does not affect `page.tsx`.

### Changes Required

#### 1. Create `.env.example`

**File**: `src/frontend/.env.example`

**Intent**: Document the environment variables the frontend needs so developers know what to set in `.env.local`.

**Contract**: Single entry: `VELO_API_URL=http://localhost:5098`. This is a server-side variable (no `NEXT_PUBLIC_` prefix) because the API URL is only used in server components.

---

#### 1b. Create `.env.local`

**File**: `src/frontend/.env.local`

**Intent**: Set `VELO_API_URL` for local development so the dev page can reach the backend. This file is gitignored and must be created manually by each developer.

**Contract**: Copy `.env.example` to `.env.local` and confirm `VELO_API_URL=http://localhost:5098` is set. No other changes needed.

---

#### 2. Create TypeScript `RouteResult` types

**File**: `src/frontend/src/types/route.ts`

**Intent**: Define the TypeScript interface that mirrors the .NET `RouteResult` data contract, giving the frontend type-safety against the backend shape.

**Contract**:
```ts
export interface RouteCoordinate { longitude: number; latitude: number; }
export interface RouteWaySegment { fromIndex: number; toIndex: number; surface: string; roadClass: string; }
export interface RouteGeometry { coordinates: RouteCoordinate[]; }
export interface RouteResult { geometry: RouteGeometry; distanceMeters: number; segments: RouteWaySegment[]; }
```
Field names must match the JSON serialisation of the .NET records (camelCase by default in .NET).

---

#### 3. Create fetch utility

**File**: `src/frontend/src/lib/routingApi.ts`

**Intent**: Typed fetch call to the backend `/routes/preview` endpoint. Centralises the API URL resolution and response typing.

**Contract**: Export `async function fetchRoutePreview(): Promise<RouteResult>` that fetches `${process.env.VELO_API_URL}/routes/preview` with `{ cache: 'no-store' }` and returns the parsed JSON typed as `RouteResult`. Throws on non-200 responses.

---

#### 4. Create `src/app/dev/page.tsx`

**File**: `src/frontend/src/app/dev/page.tsx`

**Intent**: A server component that calls `fetchRoutePreview()` and renders the result as formatted JSON — proving end-to-end data flow from ORS → .NET backend → Next.js frontend without any browser-side code.

**Contract**: Server component (`async function DevPage()`). Calls `fetchRoutePreview()`, wraps output in `<pre>{JSON.stringify(result, null, 2)}</pre>`. Handles fetch failure with a visible error message (no crash). Does not link from the root layout or `page.tsx`.

### Success Criteria

#### Automated Verification

- `npm run build` (from `src/frontend/`) completes with zero TypeScript errors
- `npm run lint` passes with no new errors

#### Manual Verification

- With both backend (`dotnet run`) and frontend (`npm run dev`) running, and `VELO_API_URL=http://localhost:5098` set in `src/frontend/.env.local`: visiting `http://localhost:3000/dev` renders a JSON object with `distanceMeters`, `geometry`, and `segments` fields
- `page.tsx` (homepage at `http://localhost:3000`) is unchanged
- `src/frontend/.env.local` exists and contains `VELO_API_URL=http://localhost:5098`

**Implementation Note**: After this phase, confirm the `segments[].surface` and `segments[].roadClass` values rendered in the browser match what was seen in Phase 2's `curl` output. This validates the JSON serialisation contract is consistent.

---

## Testing Strategy

### Manual Testing Steps

1. Set `ORS__ApiKey` to a valid free-tier ORS API key (register at `https://openrouteservice.org/`)
2. Run `dotnet run` from `src/backend/`, run `npm run dev` from `src/frontend/`
3. `curl http://localhost:5098/routes/preview` — inspect `distanceMeters`, `geometry.coordinates` count, `segments` array
4. Visit `http://localhost:3000/dev` — confirm same data renders in the browser
5. Inspect `segments` entries: verify at least some entries have non-`Unknown` `surface` and `roadClass` values
6. Remove `ORS__ApiKey` env var and restart backend — confirm `/routes/preview` returns a non-500 error (should return 502 with an error message about missing API key)

## Performance Considerations

This foundation slice does not handle user-initiated requests — it is a wiring exercise. The resilience policy (5s per-attempt timeout, 2 retries, circuit breaker) establishes the outbound budget that S-01 will inherit.

## Migration Notes

None. This is greenfield wiring on an otherwise empty backend.

## Forced Adaptations (unplanned, recorded post-implementation)

These changes were not in the original plan but were required adaptations discovered during implementation:

- **`src/frontend/src/app/layout.tsx`** — Google Fonts (`next/font/google`) is blocked by corporate SSL certificate at build time. Swapped to the `geist` npm package (`geist/font/sans`, `geist/font/mono`) which bundles fonts locally.
- **`src/frontend/package.json` / `package-lock.json`** — Added `geist@^1.7.1` to support the font swap above.
- **`src/frontend/eslint.config.mjs`** — Scaffold used legacy CJS-style `eslint-config-next` in an ESM flat config context, causing lint to fail. Rewrote using `FlatCompat` from `@eslint/eslintrc` to bridge the legacy config; added `.next/**` and `next-env.d.ts` to ignores.
- **`src/frontend/tsconfig.json`** — `jsx` mode updated from `react-jsx` to `preserve` by Next.js build tooling automatically.

## References

- Roadmap item: `context/foundation/roadmap.md` (F-01)
- PRD business logic: `context/foundation/prd.md` §Business Logic
- ORS directions v2 docs: `https://openrouteservice.org/dev/#/api-docs/v2/directions/{profile}/geojson/post`
- ORS extra info codes: `https://giscience.github.io/openrouteservice/documentation/routing-attributes/Extra-Info`

---

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: ORS HTTP client and data contract

#### Automated

- [x] 1.1 `dotnet build` compiles with zero errors and warnings from new files — b79408e
- [x] 1.2 `dotnet run` starts and `/health` returns `{"status":"ok"}` — b79408e

#### Manual

- [x] 1.3 `appsettings.json` has `ORS` section with `BaseUrl` and empty `ApiKey` — b79408e

### Phase 2: Dev-only smoke-test endpoint

#### Automated

- [x] 2.1 `dotnet build` compiles with zero errors — 5460d2b

#### Manual

- [x] 2.2 `curl http://localhost:5098/routes/preview` returns HTTP 200 with `RouteResult` JSON — 5460d2b
- [x] 2.3 `segments` array contains non-`Unknown` `surface` and `roadClass` values for Vienna fixture route — 5460d2b
- [x] 2.4 `/routes/preview` is inaccessible outside `Development` environment — 5460d2b

### Phase 3: Frontend fetch validation

#### Automated

- [x] 3.1 `npm run build` completes with zero TypeScript errors — 52f2dcb
- [x] 3.2 `npm run lint` passes with no new errors — 52f2dcb

#### Manual

- [x] 3.3 `http://localhost:3000/dev` renders `RouteResult` JSON with `distanceMeters`, `geometry`, and `segments` — 52f2dcb
- [x] 3.4 `http://localhost:3000` (homepage) is unchanged — 52f2dcb
- [x] 3.5 `src/frontend/.env.local` exists with `VELO_API_URL` set — 52f2dcb
