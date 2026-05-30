# Routing API Wiring — Plan Brief

> Full plan: `context/changes/routing-api-wiring/plan.md`

## What & Why

VeloRoute generates loop routes from road-network data. Before any routing algorithm can be built (S-01), the backend needs a reliable, typed connection to a road-network data provider and a well-defined internal data contract for what a route looks like. F-01 delivers both: an OpenRouteService HTTP client and a normalised `RouteResult` type that S-01 can build on without knowing anything about ORS internals.

## Starting Point

The .NET backend is a 55-line minimal API scaffold with `/health` only — no HTTP clients, no services, one NuGet package. The Next.js frontend is equally bare, with no API utilities or type definitions.

## Desired End State

A typed `IOpenRouteServiceClient` is registered in .NET DI, callable from any endpoint. It returns a normalised `RouteResult` with geometry, distance, and surface/road-class data as domain enums. A dev-only `GET /routes/preview` endpoint and a Next.js `src/app/dev/page.tsx` together prove the full data path — from ORS through the backend to the browser — before S-01 begins.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Road-network provider | OpenRouteService (ORS) | Cycling-specific routing profiles + GeoJSON output; indicated candidate in roadmap | Plan |
| HTTP client pattern | Typed client (`IOpenRouteServiceClient` + concrete) | DI-friendly, testable, follows .NET conventions | Plan |
| Data contract shape | Normalised domain types (`SurfaceType`/`RoadClass` enums) | ORS integer codes must not leak to S-01; enums make the contract explicit and maintainable | Plan |
| Error handling | `RoutingResult<T>` (no exceptions for expected failures) | Caller decides how to handle ORS errors without try/catch at every call site | Plan |
| Config | `appsettings.json` + `ORS__ApiKey` env var override | Standard .NET layered config; no secrets in source | Plan |
| Resilience | 2 retries + 5s timeout + circuit breaker (exclude 401/403/429) | Aligns with PRD 5s budget; prevents quota exhaustion on auth/rate-limit errors | Plan |
| Verification | Dev-only `GET /routes/preview` with fixture coordinates | Fastest live-wire proof without user-data concerns or test infrastructure overhead | Plan |
| Frontend scope | Dedicated `src/app/dev/page.tsx`, server-side `VELO_API_URL` | Validates contract end-to-end without touching the homepage or browser-side code | Plan |

## Scope

**In scope:**
- `Microsoft.Extensions.Http.Resilience` NuGet package
- `OpenRouteServiceOptions`, `RoutingResult<T>`, `RouteResult`, `SurfaceType`, `RoadClass`, `IOpenRouteServiceClient`, `OpenRouteServiceClient`
- `appsettings.json` `ORS` config section
- `GET /routes/preview` dev-only endpoint
- `.env.example`, `src/types/route.ts`, `src/lib/routingApi.ts`, `src/app/dev/page.tsx`

**Out of scope:**
- Loop-route generation algorithm (S-01)
- Geocoding / start-point search (S-01)
- Route input form or interactive map (S-01)
- GPX export (S-02)
- OSM Overpass API integration
- Production secrets management beyond env var

## Architecture / Approach

The `OpenRouteServiceClient` is the sole owner of ORS knowledge: it builds the POST request body, handles the GeoJSON response, iterates the surface/waytype span tuples, and maps integer codes to `SurfaceType`/`RoadClass` enums. Everything above the client sees only the internal domain types. The resilience pipeline (timeout → retry → circuit breaker) is layered onto the `IHttpClientBuilder` registration in `Program.cs`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. ORS HTTP client + data contract | NuGet, all types, typed client, `Program.cs` registration | ORS extras span-tuple mapping is non-trivial; mapper bugs produce all-`Unknown` surface/road-class values |
| 2. Dev smoke-test endpoint | `GET /routes/preview` proves live wire against real ORS API | Needs a valid ORS API key — discovery of free-tier quota limits could slow verification |
| 3. Frontend fetch validation | `src/app/dev/page.tsx` renders `RouteResult` JSON from backend | JSON field name casing mismatch (.NET PascalCase vs camelCase) between backend and TS types |

**Prerequisites:** A valid OpenRouteService free-tier API key (register at https://openrouteservice.org/)
**Estimated effort:** ~2 focused sessions across 3 phases

## Open Risks & Assumptions

- ORS free tier may rate-limit or have data gaps for some regions — acceptable for F-01 wiring; re-evaluate at S-01 if route quality is poor
- `surface` + `waytypes` extra_info is assumed sufficient for S-01 route scoring; Phase 2 manual check (inspect live response) is the validation gate before S-01 planning begins
- ORS `extras` span-tuple mapping must be verified against a real response — a captured payload in a unit test is the safest guard

## Success Criteria (Summary)

- `dotnet build` and `npm run build` both pass cleanly after all three phases
- `curl http://localhost:5098/routes/preview` returns a `RouteResult` with non-`Unknown` `surface` and `roadClass` values for the Vienna fixture route
- `http://localhost:3000/dev` renders the same route data in the browser without touching the homepage
