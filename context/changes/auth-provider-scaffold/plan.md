# Auth Provider Scaffold — Implementation Plan

## Overview

Wire Clerk as the VeloRoute auth provider. No user-facing auth UI is built here — this is pure infrastructure: Clerk application config, `@clerk/nextjs` in Next.js, JWT Bearer middleware in .NET, and a test JWT factory that keeps existing tests green. The deliverable is a verifiable token chain from Clerk → frontend → backend, confirmed by a dev-only smoke endpoint.

**Provider change (2026-07-07):** originally planned against Microsoft Entra External ID CIAM. Blocked at tenant creation — the `Microsoft.AzureActiveDirectory/ciamDirectories` resource type only deploys to broad meta-regions (Global, United States, Europe, Asia Pacific, Australia, Japan), none of which intersect the Azure subscription's system-enforced region allowlist (a fraud-prevention policy tied to the "Azure for Students" offer, not customer-removable). Pivoted to Clerk — no Azure dependency, free tier (10k MAU), native email OTP/magic-link, JWKS-based JWT validation keeps the same backend architecture shape. Postgres (F-02) stays on Azure; this swap only affects F-01.

## Current State Analysis

Backend (`src/backend/VeloRoute/Program.cs`):
- Completely anonymous today; no auth middleware, no auth packages
- CORS at `Program.cs:10-19` uses `AllowAnyHeader()` — already permits `Authorization` header on cross-origin requests; no CORS changes needed
- Middleware sequence today: `UseCors` (line 58) → endpoint definitions; auth middleware inserts between them

Frontend (`src/frontend/`):
- No auth packages; only Entra-related reference was ORS API key header in `app/api/geocode/route.ts` (unrelated, unaffected by this pivot)
- `layout.tsx` is a clean server component
- `RouteApp.tsx` is already `"use client"` but wrapping auth context there would block future pages (My Routes, account settings) from accessing auth state

Test infrastructure (`src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`):
- `VeloRouteWebApplicationFactory` already uses `ConfigureWebHost`/`ConfigureServices` pattern — test JWT factory extends the same pattern
- No auth-aware tests today; adding JWT Bearer middleware would not break existing tests (endpoints are currently unauthenticated and stay that way)

## Desired End State

After this plan:
- A Clerk application exists for VeloRoute with email OTP (or email magic link) enabled as the sign-in method
- Frontend has `@clerk/nextjs` wired at the app root (`layout.tsx` via `<ClerkProvider>`), plus `middleware.ts` using `clerkMiddleware()`
- Backend validates Clerk-issued JWTs via JWKS, serves a dev-only `GET /auth/probe` that returns 401 without a token and 200 with a valid token
- `POST /routes/loop` and `POST /routes/gpx` remain accessible without a token
- All 43 existing backend tests pass; new auth middleware tests cover 401 rejection and 200 acceptance using a test JWT factory
- All frontend builds, lint, and existing Vitest tests pass

### Verification:
1. `dotnet test` — all tests green including new auth middleware tests
2. `npm run build && npm run lint` — clean
3. Manual: dev server running, `curl http://localhost:5098/auth/probe` → 401, with Bearer token from Clerk session → 200
4. Manual: `POST /routes/loop` without token → 200 (anonymous access preserved)
5. Manual: clicking sign-in triggers Clerk's email OTP sign-in flow

### Key Discoveries:

- `Program.cs:58` — `app.UseCors()` already called with `AllowAnyHeader()`; auth middleware goes after this line, before endpoint definitions
- `TestInfrastructure.cs:70` — `ConfigureWebHost`/`ConfigureServices` pattern is the extension point for test JWT override
- Clerk issues standard JWTs; each Clerk instance exposes a Frontend API domain (e.g. `https://<your-app>.clerk.accounts.dev` for dev instances) with a JWKS endpoint at `<frontend-api>/.well-known/jwks.json` and (per Clerk's OIDC support) a discovery document at `<frontend-api>/.well-known/openid-configuration`
- Clerk session tokens carry `azp` (authorized party — the origin that requested the token) rather than a conventional `aud` audience claim unless a custom JWT template is configured in the Clerk dashboard; backend validation must account for this (see Critical Implementation Details)
- No `staticwebapp.config.json` exists; SWA EasyAuth is off by default and stays off — Clerk owns all auth client-side
- Free tier covers 10,000 MAU/month; no Azure subscription or region dependency at all
- **Unverified, confirm during Phase 1/2 implementation:** exact shape of Clerk's OIDC discovery document and whether ASP.NET Core's standard `AddJwtBearer(Authority: ...)` auto-discovery works against it out of the box, or whether `MetadataAddress`/manual JWKS fetch is required. No official Microsoft.Identity-style Clerk package exists for .NET — this plan uses the generic `Microsoft.AspNetCore.Authentication.JwtBearer` package (already part of the ASP.NET Core shared framework).

## What We're NOT Doing

- No login/logout UI (deferred to S-01: `magic-link-auth`)
- No user row creation in the database (deferred to S-01, which requires F-02 as well)
- No auth-state props or hooks added to `RouteForm`, `RouteMap`, `RouteInfoPanel` (invisible until S-01)
- No SWA EasyAuth configuration (stays disabled; Clerk handles auth client-side)
- No production Clerk instance (dev instance only in this scaffold; production instance configured at deploy time)
- No token passing from frontend to backend except via the manual smoke test
- No Azure Entra External ID — superseded by this pivot; do not resume that path without re-opening the roadmap decision

## Implementation Approach

Three sequential phases matching three concerns: (1) external Clerk config first, because both frontend and backend config values flow from it; (2) backend middleware and test infrastructure next, so the JWT chain is verifiable independently; (3) frontend Clerk integration last, enabling the full round-trip smoke test.

Generic `Microsoft.AspNetCore.Authentication.JwtBearer` is used on the backend (not a Clerk-specific package — none exists for .NET) — `Authority` points at the Clerk Frontend API domain, and token validation is configured explicitly rather than relying on a provider-maintained helper.

The test JWT factory generates JWTs signed with a test RSA key and overrides the JWT Bearer scheme's `IssuerSigningKey` in `VeloRouteWebApplicationFactory.ConfigureWebHost` — auth middleware runs in tests but trusts the test key instead of Clerk's JWKS. This part of the design is provider-agnostic and needed no changes from the original Entra-based plan.

## Critical Implementation Details

**Clerk JWKS/discovery format** — Frontend API domain for a dev instance looks like `https://<slug>.clerk.accounts.dev`; JWKS lives at `<frontend-api>/.well-known/jwks.json`. Set `Authority` to the Frontend API domain. If ASP.NET Core's built-in OIDC discovery (`{Authority}/.well-known/openid-configuration`) doesn't resolve cleanly against Clerk's instance, fall back to setting `MetadataAddress` explicitly to the JWKS URL and skip discovery.

**Audience/`azp` validation** — Clerk tokens generally don't carry a conventional `aud` claim unless a custom JWT template is set up in the Clerk dashboard. Set `ValidateAudience = false` in `TokenValidationParameters`, and instead add a custom `OnTokenValidated` check that asserts the `azp` claim matches the expected frontend origin(s) (`http://localhost:3000` in dev). Skipping this check would let a JWT minted for a different application on the same Clerk instance be accepted by this backend.

**Middleware ordering** — In `Program.cs`, `app.UseAuthentication()` and `app.UseAuthorization()` must be inserted after `app.UseCors()` (line 58) and before the `app.MapGet`/`app.MapPost` endpoint definitions. This ensures CORS headers appear on 401 responses (CORS middleware runs first, adds headers to all responses including auth failures). Reversing this causes CORS preflight to pass but rejected auth calls to arrive at the client without CORS headers.

**`<ClerkProvider>` placement** — unlike MSAL, Clerk's Next.js SDK (`@clerk/nextjs`) is designed for the App Router and `<ClerkProvider>` can wrap the root `layout.tsx` directly without requiring a separate `"use client"` wrapper component — Clerk handles the server/client boundary internally. A `middleware.ts` file at the frontend project root (or `src/`, matching where Next.js resolves middleware) must call `clerkMiddleware()` from `@clerk/nextjs/server` for `auth()`/session helpers to work.

---

## Phase 1: Clerk Application Setup

### Overview

Create the Clerk application, enable email OTP (or magic link) as the sign-in method, and document all config values needed by Phases 2 and 3. This phase produces no runtime code — only configuration and updated `.env.example`/`appsettings.json` shape.

### Changes Required:

#### 1. Clerk application

**File**: Clerk dashboard (external configuration)

**Intent**: Create a dedicated Clerk application for VeloRoute (free tier; dev instance). Enable email one-time-passcode (or email magic link) as the sign-in strategy under User & Authentication → Email, Phone, Username.

**Contract**: Application produces a Frontend API domain (`<slug>.clerk.accounts.dev`), a Publishable Key (`pk_test_...`), and a Secret Key (`sk_test_...`). JWKS endpoint: `https://<slug>.clerk.accounts.dev/.well-known/jwks.json`.

#### 2. Frontend `.env.example` update

**File**: `src/frontend/.env.example`

**Intent**: Document all Clerk-required environment variables so any contributor can wire up their own Clerk application.

**Contract**: Replace the Entra placeholder block with:
```
NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY=     # pk_test_... from Clerk dashboard
CLERK_SECRET_KEY=                      # sk_test_... server-only, never NEXT_PUBLIC
NEXT_PUBLIC_CLERK_SIGN_IN_URL=/sign-in # placeholder route, no UI built yet
```

#### 3. Backend appsettings section documentation

**File**: `src/backend/VeloRoute/appsettings.json`

**Intent**: Add the `Clerk` configuration section shape with placeholder values so the backend knows what config keys to expect.

**Contract**: Replace the `EntraExternalId` section with:
```json
"Clerk": {
  "Authority": "",
  "FrontendApiDomain": "",
  "AllowedAzp": ""
}
```
Real values go in `appsettings.Development.json` (gitignored) or environment variables; the `appsettings.json` entry documents the shape. `AllowedAzp` is the expected authorized-party origin (`http://localhost:3000` in dev) used by the custom `OnTokenValidated` check.

### Success Criteria:

#### Automated Verification:

- Frontend `.env.example` updated: `git diff --name-only` shows `src/frontend/.env.example`
- Backend `appsettings.json` updated: `git diff --name-only` shows `src/backend/VeloRoute/appsettings.json`
- JWKS endpoint resolves: `curl https://<slug>.clerk.accounts.dev/.well-known/jwks.json` returns JSON

#### Manual Verification:

- Clerk application created; email OTP (or magic link) sign-in method enabled and visible in dashboard
- Publishable key + secret key + Frontend API domain recorded
- Config values recorded in local `src/frontend/.env.local` and `src/backend/appsettings.Development.json` (both gitignored)

**Implementation Note**: After completing this phase and verifying the JWKS endpoint resolves, pause for manual confirmation that config values are recorded locally before starting Phase 2.

---

## Phase 2: Backend JWT Middleware + Test Infrastructure

### Overview

Add JWT Bearer authentication to the .NET backend using the generic `Microsoft.AspNetCore.Authentication.JwtBearer` package, wire auth middleware into `Program.cs`, add a dev-only `/auth/probe` endpoint for smoke testing, and extend `VeloRouteWebApplicationFactory` with a test JWT factory so all 43 existing tests pass and new auth tests can verify token acceptance/rejection.

### Changes Required:

#### 1. Confirm JwtBearer package availability

**File**: `src/backend/VeloRoute/VeloRoute.csproj`

**Intent**: `Microsoft.AspNetCore.Authentication.JwtBearer` ships as part of the ASP.NET Core shared framework for web SDK projects — confirm no explicit `PackageReference` is needed (`Microsoft.NET.Sdk.Web` projects get it implicitly); add one explicitly only if the build fails to resolve the namespace.

**Contract**: No package change unless build proves otherwise.

#### 2. Register auth services in Program.cs

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Register JWT Bearer authentication against the Clerk Frontend API authority so the DI container can validate tokens on protected endpoints.

**Contract**: After `builder.Services.AddCors(...)` and before `var app = builder.Build()`, add:
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Clerk:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var azp = context.Principal?.FindFirst("azp")?.Value;
                var allowed = builder.Configuration["Clerk:AllowedAzp"];
                if (azp != allowed)
                {
                    context.Fail("azp claim did not match allowed origin");
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();
```
The `Clerk` config section must contain `Authority`, `FrontendApiDomain`, and `AllowedAzp` keys (documented in Phase 1). If ASP.NET Core discovery against `Authority` fails during implementation, set `options.MetadataAddress` to the JWKS URL directly (see Critical Implementation Details).

#### 3. Add auth middleware to pipeline

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Activate authentication and authorization middleware so the pipeline can validate JWTs on endpoints that require them.

**Contract**: After `app.UseCors()` (line 58) and before the first `app.MapGet`/`app.MapPost`, add:
```csharp
app.UseAuthentication();
app.UseAuthorization();
```
`POST /routes/loop` and `POST /routes/gpx` get no `.RequireAuthorization()` call — they stay anonymous. The `/health` endpoint also stays anonymous.

#### 4. Add dev-only /auth/probe endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Add a development-only endpoint that returns 401 for unauthenticated requests and 200 with the caller's subject claim for authenticated requests. This is the smoke-test target for the round-trip verification in Phase 3.

**Contract**: Inside the existing `if (app.Environment.IsDevelopment())` block (line 51), alongside the Swagger registration:
```csharp
app.MapGet("/auth/probe", (ClaimsPrincipal user) =>
    Results.Ok(new { sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value }))
    .RequireAuthorization();
```
Confirm during implementation whether Clerk's `sub` claim maps automatically to `ClaimTypes.NameIdentifier` (standard JwtBearer claim-type mapping behavior) or whether `user.FindFirst("sub")` is needed directly.

#### 5. Add test JWT factory

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: Add a `TestJwtFactory` helper that generates RSA-signed JWTs using a test key, and extend `VeloRouteWebApplicationFactory` to accept an option that replaces the JWKS-based key with the test RSA key — so existing tests run without a Clerk dependency and new auth tests can issue valid test tokens.

**Contract**: Add a static `TestJwtFactory` class with a method `CreateToken(string subject, string azp)` that returns a signed JWT string with a `sub` and `azp` claim. Extend `VeloRouteWebApplicationFactory` constructor to accept `bool useTestAuth = false`; when true, `ConfigureTestServices` overrides the JwtBearer `IssuerSigningKey` with the test RSA key, sets `ValidateIssuer = false`, and overrides `Clerk:AllowedAzp` config to match the test token's `azp` value.

#### 6. Add auth middleware test class

**File**: `src/backend/VeloRoute.Tests/Routing/AuthMiddlewareTests.cs` (new file)

**Intent**: Verify that the auth middleware correctly rejects requests without a token, accepts requests with a valid token, and does not block the anonymous endpoints.

**Contract**: Three xUnit facts:
- `GET /auth/probe` with no token → HTTP 401
- `GET /auth/probe` with `TestJwtFactory.CreateToken("test-user", "http://localhost:3000")` → HTTP 200
- `POST /routes/loop` with no token → HTTP 200 or 422 (not 401; anonymous access preserved)

#### 7. Add JWT test dependencies to test project

**File**: `src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`

**Intent**: `TestJwtFactory` needs `System.IdentityModel.Tokens.Jwt` to generate signed JWTs; add the NuGet reference to the test project.

**Contract**: Add `<PackageReference Include="Microsoft.IdentityModel.Tokens" Version="..." />` and `<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="..." />`.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/`
- All 43 existing tests pass: `dotnet test src/backend/`
- New auth tests pass (included in the 43+ count after this phase)
- `GET /auth/probe` without token → 401 (asserted in `AuthMiddlewareTests`)
- `GET /auth/probe` with test JWT → 200 (asserted in `AuthMiddlewareTests`)
- `POST /routes/loop` without token → not 401 (asserted in `AuthMiddlewareTests`)

#### Manual Verification:

- Backend running: `dotnet run` from `src/backend/VeloRoute/`
- `curl http://localhost:5098/auth/probe` → 401 JSON response
- `curl -H "Authorization: Bearer bad" http://localhost:5098/auth/probe` → 401 JSON response

**Implementation Note**: Pause here after all automated tests pass and manual curl returns 401 for unauthenticated probe. Phase 3 requires the backend probe to be working for the final round-trip smoke test.

---

## Phase 3: Frontend Clerk Integration + Round-Trip Smoke Test

### Overview

Install `@clerk/nextjs`, wrap `layout.tsx` with `<ClerkProvider>`, add `middleware.ts` with `clerkMiddleware()`, and document all required environment variables. Then perform the full round-trip smoke test: sign in via Clerk email OTP, acquire a session token, call `/auth/probe`, and verify anonymous route generation is unaffected.

### Changes Required:

#### 1. Install Clerk package

**File**: `src/frontend/package.json`

**Intent**: Add the Clerk Next.js SDK.

**Contract**: `npm install @clerk/nextjs` in `src/frontend/`. Add as a runtime dependency.

#### 2. Wrap layout.tsx with ClerkProvider

**File**: `src/frontend/src/app/layout.tsx`

**Intent**: Make Clerk auth context available to all pages and components in the app.

**Contract**: Import `ClerkProvider` from `@clerk/nextjs`. Wrap the `<html>`/`<body>` tree with `<ClerkProvider>`. Confirm during implementation whether `layout.tsx` needs `"use client"` — per Clerk's App Router docs it should not, but verify against the installed SDK version's current behavior (`AGENTS.md` warns this Next.js/React version has training-data-breaking changes; check `node_modules/next/dist/docs/` and `node_modules/@clerk/nextjs` docs before assuming).

#### 3. Add Clerk middleware

**File**: `src/frontend/middleware.ts` (new file, project root alongside `src/`, matching Next.js middleware resolution rules for this project)

**Intent**: Enable Clerk's `auth()`/session helpers across the app.

**Contract**: 
```ts
import { clerkMiddleware } from '@clerk/nextjs/server'
export default clerkMiddleware()
export const config = {
  matcher: ['/((?!_next|.*\\..*).*)'],
}
```
Confirm the matcher doesn't block the existing `/api/geocode` route unintentionally.

#### 4. Update frontend .env.local (local dev)

**File**: `src/frontend/.env.local` (gitignored; created by developer)

**Intent**: Supply actual Clerk config values from Phase 1 to the dev environment.

**Contract**: Developer populates `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and `CLERK_SECRET_KEY` from the Clerk application created in Phase 1. `.env.example` already documents the shape (added in Phase 1).

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` from `src/frontend/`
- Lint passes: `npm run lint` from `src/frontend/`
- Existing Vitest tests pass: `npm test` from `src/frontend/`
- TypeScript: no type errors in new files

#### Manual Verification:

- `npm run dev` starts without Clerk errors in browser console
- Anonymous route generation works end-to-end (form → generate → map display → GPX download) without logging in
- Triggering Clerk's sign-in flow (via a temporary test route or Clerk's hosted `<SignIn />` component mounted ad hoc) navigates to the email OTP entry step
- After completing OTP entry, a session is established
- Session token retrievable client-side via `useAuth().getToken()` (or server-side via `auth().getToken()`)
- `curl -H "Authorization: Bearer <token>" http://localhost:5098/auth/probe` → 200 with `{ "sub": "..." }`
- `curl http://localhost:5098/auth/probe` → 401
- `POST /routes/loop` without token → 200 (anonymous access preserved)

**Implementation Note**: After the full round-trip smoke test passes, F-01 is complete. Proceed to `/10x-plan data-layer-schema` (F-02) or begin S-07 (`routing-quality-osm`) in parallel.

---

## Testing Strategy

### Unit Tests:

- `AuthMiddlewareTests.cs` — three tests covering: 401 without token, 200 with valid test JWT, anonymous endpoint stays accessible

### Integration Tests:

- Full round-trip (manual): Clerk email OTP sign-in → session token → `/auth/probe` → 200
- Anonymous route generation unaffected (manual + existing `LoopRouteIntegrationTests.cs`)

### Manual Testing Steps:

1. Start backend: `dotnet run` from `src/backend/VeloRoute/`
2. Start frontend: `npm run dev` from `src/frontend/`
3. Mount a temporary `<SignIn />` component (or navigate to Clerk's hosted sign-in) and complete email OTP entry
4. In browser console (or a temporary debug button): retrieve the session token via Clerk's client-side `getToken()` API
5. `curl -H "Authorization: Bearer <token>" http://localhost:5098/auth/probe` → `{"sub":"..."}` 200
6. `curl http://localhost:5098/auth/probe` → 401
7. `curl -X POST http://localhost:5098/routes/loop -H "Content-Type: application/json" -d '{"startLon":0,"startLat":0,"minKm":20,"maxKm":40}' ` → not 401

## References

- Roadmap F-01: `context/foundation/roadmap.md` (lines 67–78)
- PRD v2 auth scope: `context/foundation/prd-v2.md` (lines 80–99)
- Existing backend bootstrap: `src/backend/VeloRoute/Program.cs`
- Test infrastructure: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`
- Frontend entry point: `src/frontend/src/app/layout.tsx`
- Clerk pivot rationale: this plan's Overview section, 2026-07-07 (superseded Entra External ID CIAM — blocked by Azure subscription region policy on the available subscription)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Clerk Application Setup

#### Automated

- [x] 1.1 Frontend `.env.example` updated with Clerk variables — de99e65
- [x] 1.2 Backend `appsettings.json` Clerk section added — de99e65
- [x] 1.3 JWKS endpoint resolves (curl returns JSON) — de99e65

#### Manual

- [x] 1.4 Clerk application created; email OTP (or magic link) sign-in enabled — de99e65
- [x] 1.5 Publishable key + secret key + Frontend API domain recorded — de99e65
- [x] 1.6 Config values recorded in local .env.local and appsettings.Development.json — de99e65

### Phase 2: Backend JWT Middleware + Test Infrastructure

#### Automated

- [x] 2.1 Backend builds: `dotnet build src/backend/` — 74472c9
- [x] 2.2 All tests pass: `dotnet test src/backend/` — 74472c9
- [x] 2.3 GET /auth/probe without token → 401 (AuthMiddlewareTests) — 74472c9
- [x] 2.4 GET /auth/probe with test JWT → 200 (AuthMiddlewareTests) — 74472c9
- [x] 2.5 POST /routes/loop without token → not 401 (AuthMiddlewareTests) — 74472c9

#### Manual

- [x] 2.6 `curl http://localhost:5098/auth/probe` → 401 — 74472c9
- [x] 2.7 `curl -H "Authorization: Bearer bad" http://localhost:5098/auth/probe` → 401 — 74472c9

### Phase 3: Frontend Clerk Integration + Round-Trip Smoke Test

#### Automated

- [x] 3.1 Frontend builds: `npm run build` from src/frontend/
- [x] 3.2 Lint passes: `npm run lint` from src/frontend/
- [x] 3.3 Vitest tests pass: `npm test` from src/frontend/
- [x] 3.4 No TypeScript errors in new Clerk files

#### Manual

- [x] 3.5 Dev server starts without Clerk errors in browser console
- [x] 3.6 Anonymous route generation works end-to-end without login
- [x] 3.7 Sign-in flow reaches email OTP entry step
- [x] 3.8 After OTP entry, session active; token retrievable via getToken()
- [x] 3.9 `curl -H "Authorization: Bearer <token>" .../auth/probe` → 200
- [x] 3.10 `curl .../auth/probe` (no token) → 401
- [x] 3.11 POST /routes/loop without token → 200 (anonymous access preserved)
