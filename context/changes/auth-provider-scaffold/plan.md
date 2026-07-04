# Auth Provider Scaffold — Implementation Plan

## Overview

Wire Microsoft Entra External ID CIAM as the VeloRoute auth provider. No user-facing auth UI is built here — this is pure infrastructure: tenant configuration, MSAL.js in Next.js, JWT Bearer middleware in .NET, and a test JWT factory that keeps existing tests green. The deliverable is a verifiable token chain from Entra CIAM → frontend → backend, confirmed by a dev-only smoke endpoint.

## Current State Analysis

Backend (`src/backend/VeloRoute/Program.cs`):
- Completely anonymous today; no auth middleware, no auth packages
- CORS at `Program.cs:10-19` uses `AllowAnyHeader()` — already permits `Authorization` header on cross-origin requests; no CORS changes needed
- Middleware sequence today: `UseCors` (line 58) → endpoint definitions; auth middleware inserts between them

Frontend (`src/frontend/`):
- No auth packages; only Entra-related reference is ORS API key header in `app/api/geocode/route.ts` (unrelated)
- `layout.tsx` is a clean server component — ready to receive a client wrapper child
- `RouteApp.tsx` is already `"use client"` but wrapping there would block future pages (My Routes, account settings) from accessing auth state

Test infrastructure (`src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`):
- `VeloRouteWebApplicationFactory` already uses `ConfigureWebHost`/`ConfigureServices` pattern — test JWT factory extends the same pattern
- No auth-aware tests today; adding JWT Bearer middleware would not break existing tests (endpoints are currently unauthenticated and stay that way)

## Desired End State

After this plan:
- A dedicated Entra External ID CIAM tenant exists for VeloRoute with two app registrations: Web API (with `user.data` scope) and SPA (with localhost + production redirect URIs)
- Frontend has MSAL.js wired at the app root (`layout.tsx` via `MsalProviderWrapper`), using redirect flow, with CIAM authority
- Backend validates Entra-issued JWTs via JWKS, serves a dev-only `GET /auth/probe` that returns 401 without a token and 200 with a valid token
- `POST /routes/loop` and `POST /routes/gpx` remain accessible without a token
- All 43 existing backend tests pass; new auth middleware tests cover 401 rejection and 200 acceptance using a test JWT factory
- All frontend builds, lint, and existing Vitest tests pass

### Verification:
1. `dotnet test` — all tests green including new auth middleware tests
2. `npm run build && npm run lint` — clean
3. Manual: dev server running, `curl http://localhost:5098/auth/probe` → 401, with Bearer token from Entra OTP login → 200
4. Manual: `POST /routes/loop` without token → 200 (anonymous access preserved)
5. Manual: clicking login triggers redirect to Entra CIAM OTP sign-in page

### Key Discoveries:

- `Program.cs:58` — `app.UseCors()` already called with `AllowAnyHeader()`; auth middleware goes after this line, before endpoint definitions
- `TestInfrastructure.cs:70` — `ConfigureWebHost`/`ConfigureServices` pattern is the extension point for test JWT override
- No `staticwebapp.config.json` exists; SWA EasyAuth is off by default and stays off — MSAL owns all auth client-side
- Entra External ID CIAM uses a different authority URL format than standard Entra: `https://<tenant>.ciamlogin.com/<tenant-id>` (not `login.microsoftonline.com`)
- Free tier covers 50,000 MAU/month; CIAM tenant is separate from the Azure subscription hosting SWA + App Service

## What We're NOT Doing

- No login/logout UI (deferred to S-01: `magic-link-auth`)
- No user row creation in the database (deferred to S-01, which requires F-02 as well)
- No auth-state props or hooks added to `RouteForm`, `RouteMap`, `RouteInfoPanel` (invisible until S-01)
- No SWA EasyAuth configuration (stays disabled; MSAL handles auth client-side)
- No production Entra tenant (dev/staging tenant only in this scaffold; production tenant configured at deploy time)
- No token passing from frontend to backend except via the manual smoke test

## Implementation Approach

Three sequential phases matching three concerns: (1) external Azure config first, because both frontend and backend config values flow from it; (2) backend middleware and test infrastructure next, so the JWT chain is verifiable independently; (3) frontend MSAL integration last, enabling the full round-trip smoke test.

`Microsoft.Identity.Web` is used on the backend (not raw `JwtBearer`) — it's the Microsoft-maintained library for Entra integration in .NET, handles JWKS endpoint discovery, issuer, and audience validation automatically from a config section.

The test JWT factory generates JWTs signed with a test RSA key and overrides the JWT Bearer scheme's `IssuerSigningKey` in `VeloRouteWebApplicationFactory.ConfigureWebHost` — auth middleware runs in tests but trusts the test key instead of Entra JWKS.

## Critical Implementation Details

**MSAL CIAM authority format** — Entra External ID CIAM uses `https://<tenant-name>.ciamlogin.com/<tenant-id>` as the authority, not the standard `https://login.microsoftonline.com/<tenant-id>`. MSAL also requires `knownAuthorities: ['<tenant-name>.ciamlogin.com']` in the config to avoid "untrusted authority" errors; without it, MSAL silently rejects the CIAM endpoint.

**Middleware ordering** — In `Program.cs`, `app.UseAuthentication()` and `app.UseAuthorization()` must be inserted after `app.UseCors()` (line 58) and before the `app.MapGet`/`app.MapPost` endpoint definitions. This ensures CORS headers appear on 401 responses (CORS middleware runs first, adds headers to all responses including auth failures). Reversing this causes CORS preflight to pass but rejected auth calls to arrive at the client without CORS headers.

**`MsalProviderWrapper` must be a `"use client"` component** — `layout.tsx` is a server component and cannot call React hooks or context providers directly. `MsalProvider` from `@azure/msal-react` uses React context (client-only). The wrapper component carries the `"use client"` directive and wraps `{children}`; `layout.tsx` imports and renders it without becoming a client component itself.

---

## Phase 1: Azure Tenant + App Registrations

### Overview

Create the Entra External ID CIAM tenant, register the Web API and SPA applications, define the `user.data` scope, and document all config values needed by Phases 2 and 3. This phase produces no code — only configuration and updated `.env.example` files.

### Changes Required:

#### 1. Entra External ID CIAM tenant

**File**: Azure portal (external configuration)

**Intent**: Create a dedicated Entra External ID CIAM tenant for VeloRoute (free tier; separate from the Azure subscription hosting SWA and App Service). Enable email one-time passcode as the identity provider.

**Contract**: Tenant produces a `<tenant-name>` and `<tenant-id>` (GUID). Authority base URL: `https://<tenant-name>.ciamlogin.com/<tenant-id>`. OIDC discovery endpoint: `https://<tenant-name>.ciamlogin.com/<tenant-id>/v2.0/.well-known/openid-configuration`.

#### 2. Web API app registration

**File**: Azure portal — App registrations (in the CIAM tenant)

**Intent**: Register the VeloRoute backend as a Web API resource application. Expose a single scope `user.data` that the SPA will request when acquiring access tokens.

**Contract**: Produces a `<web-api-client-id>` (GUID). Scope URI: `api://<web-api-client-id>/user.data`. Audience for backend JWT validation: `<web-api-client-id>` (or the full `api://` URI — match whatever `Microsoft.Identity.Web` expects for the `Audience` config key).

#### 3. SPA app registration

**File**: Azure portal — App registrations (in the CIAM tenant)

**Intent**: Register the VeloRoute frontend as a Single Page Application client. Configure redirect URIs for both local dev and production.

**Contract**: Produces a `<spa-client-id>` (GUID). Redirect URIs: `http://localhost:3000` (dev) + the production SWA URL. Post-logout redirect URI: `http://localhost:3000`. Grant API permission to `api://<web-api-client-id>/user.data` (delegated).

#### 4. Frontend `.env.example` update

**File**: `src/frontend/.env.example`

**Intent**: Document all MSAL-required environment variables so any contributor can wire up their own Entra app registration.

**Contract**: Add four variables:
```
NEXT_PUBLIC_ENTRA_CLIENT_ID=           # SPA app registration client ID
NEXT_PUBLIC_ENTRA_AUTHORITY=           # https://<tenant-name>.ciamlogin.com/<tenant-id>
NEXT_PUBLIC_ENTRA_REDIRECT_URI=        # http://localhost:3000
NEXT_PUBLIC_ENTRA_API_SCOPE=           # api://<web-api-client-id>/user.data
```

#### 5. Backend appsettings section documentation

**File**: `src/backend/VeloRoute/appsettings.json`

**Intent**: Add the `EntraExternalId` configuration section shape with placeholder values so the backend knows what config keys to expect.

**Contract**: Add section:
```json
"EntraExternalId": {
  "Authority": "",
  "ClientId": "",
  "Audience": ""
}
```
Real values go in `appsettings.Development.json` (gitignored) or environment variables; the `appsettings.json` entry documents the shape.

### Success Criteria:

#### Automated Verification:

- Frontend `.env.example` updated: `git diff --name-only` shows `src/frontend/.env.example`
- Backend `appsettings.json` updated: `git diff --name-only` shows `src/backend/VeloRoute/appsettings.json`
- OIDC discovery endpoint resolves: `curl https://<tenant-name>.ciamlogin.com/<tenant-id>/v2.0/.well-known/openid-configuration` returns JSON

#### Manual Verification:

- CIAM tenant created; both app registrations visible in Azure portal
- SPA granted delegated permission to `user.data` scope on Web API registration
- Config values (tenant name, tenant ID, SPA client ID, Web API client ID) recorded in local `src/frontend/.env.local` and `src/backend/appsettings.Development.json` (both gitignored)

**Implementation Note**: After completing this phase and verifying the OIDC discovery endpoint resolves, pause for manual confirmation that config values are recorded locally before starting Phase 2.

---

## Phase 2: Backend JWT Middleware + Test Infrastructure

### Overview

Add JWT Bearer authentication to the .NET backend using `Microsoft.Identity.Web`, wire auth middleware into `Program.cs`, add a dev-only `/auth/probe` endpoint for smoke testing, and extend `VeloRouteWebApplicationFactory` with a test JWT factory so all 43 existing tests pass and new auth tests can verify token acceptance/rejection.

### Changes Required:

#### 1. Add Microsoft.Identity.Web NuGet package

**File**: `src/backend/VeloRoute/VeloRoute.csproj`

**Intent**: Add the `Microsoft.Identity.Web` package, which provides Entra-native JWT Bearer configuration for .NET, including automatic JWKS endpoint discovery and issuer/audience validation.

**Contract**: Add `<PackageReference Include="Microsoft.Identity.Web" Version="..." />`. Pin to a stable release compatible with .NET 10.

#### 2. Register auth services in Program.cs

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Register JWT Bearer authentication with the Entra External ID CIAM authority and audience so the DI container can validate tokens on protected endpoints.

**Contract**: After `builder.Services.AddCors(...)` and before `var app = builder.Build()`, add:
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("EntraExternalId"));
builder.Services.AddAuthorization();
```
The `EntraExternalId` config section must contain `Authority`, `ClientId`, and `Audience` keys (documented in Phase 1).

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

#### 5. Add test JWT factory

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: Add a `TestJwtFactory` helper that generates RSA-signed JWTs using a test key, and extend `VeloRouteWebApplicationFactory` to accept an option that replaces the JWKS-based key with the test RSA key — so existing tests run without Entra dependency and new auth tests can issue valid test tokens.

**Contract**: Add a static `TestJwtFactory` class with a method `CreateToken(string subject, string[] scopes)` that returns a signed JWT string. Extend `VeloRouteWebApplicationFactory` constructor to accept `bool useTestAuth = false`; when true, `ConfigureTestServices` overrides the JwtBearer `IssuerSigningKey` with the test RSA key and sets `ValidateIssuer = false`, `ValidateAudience = false`.

#### 6. Add auth middleware test class

**File**: `src/backend/VeloRoute.Tests/Routing/AuthMiddlewareTests.cs` (new file)

**Intent**: Verify that the auth middleware correctly rejects requests without a token, accepts requests with a valid token, and does not block the anonymous endpoints.

**Contract**: Three xUnit facts:
- `GET /auth/probe` with no token → HTTP 401
- `GET /auth/probe` with `TestJwtFactory.CreateToken("test-user", ["user.data"])` → HTTP 200
- `POST /routes/loop` with no token → HTTP 200 or 422 (not 401; anonymous access preserved)

#### 7. Add Microsoft.AspNetCore.Authentication.JwtBearer to test project

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

## Phase 3: Frontend MSAL Integration + Round-Trip Smoke Test

### Overview

Install MSAL packages, create the MSAL instance config pointing at the CIAM authority, wrap `layout.tsx` with a `"use client"` `MsalProviderWrapper`, and document all required environment variables. Then perform the full round-trip smoke test: sign in via Entra OTP, acquire a token, call `/auth/probe`, and verify anonymous route generation is unaffected.

### Changes Required:

#### 1. Install MSAL packages

**File**: `src/frontend/package.json`

**Intent**: Add MSAL browser library and React bindings.

**Contract**: `npm install @azure/msal-browser @azure/msal-react` in `src/frontend/`. Add both as runtime dependencies.

#### 2. Create MSAL config

**File**: `src/frontend/src/lib/msalConfig.ts`

**Intent**: Create the MSAL `PublicClientApplication` instance with CIAM authority, SPA client ID, redirect URI, and localStorage cache. Exporting a singleton instance avoids multiple MSAL initializations across re-renders.

**Contract**: Export a `msalInstance: PublicClientApplication` using environment variables `NEXT_PUBLIC_ENTRA_CLIENT_ID`, `NEXT_PUBLIC_ENTRA_AUTHORITY`, and `NEXT_PUBLIC_ENTRA_REDIRECT_URI`. Config must include `knownAuthorities: [new URL(process.env.NEXT_PUBLIC_ENTRA_AUTHORITY!).hostname]` to trust the CIAM endpoint. Cache location: `'localStorage'`, `storeAuthStateInCookie: false`.

#### 3. Create MsalProviderWrapper client component

**File**: `src/frontend/src/components/auth/MsalProviderWrapper.tsx`

**Intent**: Wrap `MsalProvider` (a React context provider, client-only) in a named `"use client"` component so `layout.tsx` can import it without becoming a client component itself.

**Contract**: `"use client"` directive at top. Accepts `{ children: React.ReactNode }` props. Renders `<MsalProvider instance={msalInstance}>{children}</MsalProvider>`. Imports `msalInstance` from `@/lib/msalConfig`.

#### 4. Wrap layout.tsx with MsalProviderWrapper

**File**: `src/frontend/src/app/layout.tsx`

**Intent**: Make MSAL auth context available to all pages and components in the app without making the root layout a client component.

**Contract**: Import `MsalProviderWrapper` from `@/components/auth/MsalProviderWrapper`. Wrap the `{children}` inside `<body>` with `<MsalProviderWrapper>`. `layout.tsx` itself gains no `"use client"` directive; the `metadata` export and server component semantics are preserved.

#### 5. Update frontend .env.local (local dev)

**File**: `src/frontend/.env.local` (gitignored; created by developer)

**Intent**: Supply actual Entra config values from Phase 1 to the dev environment.

**Contract**: Developer populates all four `NEXT_PUBLIC_*` variables from the SPA app registration created in Phase 1. `.env.example` already documents the shape (added in Phase 1).

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` from `src/frontend/`
- Lint passes: `npm run lint` from `src/frontend/`
- Existing Vitest tests pass: `npm test` from `src/frontend/`
- TypeScript: no type errors in new files

#### Manual Verification:

- `npm run dev` starts without MSAL errors in browser console
- Anonymous route generation works end-to-end (form → generate → map display → GPX download) without logging in
- Triggering `loginRedirect()` from browser console (`await msalInstance.loginRedirect({ scopes: [process.env.NEXT_PUBLIC_ENTRA_API_SCOPE] })`) navigates to Entra CIAM OTP sign-in page
- After completing OTP login, app redirects back to `http://localhost:3000`
- Token present in localStorage under MSAL cache keys
- `curl -H "Authorization: Bearer <token>" http://localhost:5098/auth/probe` → 200 with `{ "sub": "..." }`
- `curl http://localhost:5098/auth/probe` → 401
- `POST /routes/loop` without token → 200 (anonymous access preserved)

**Implementation Note**: After the full round-trip smoke test passes, F-01 is complete. Proceed to `/10x-plan data-layer-schema` (F-02) or begin S-07 (`routing-quality-osm`) in parallel.

---

## Testing Strategy

### Unit Tests:

- `AuthMiddlewareTests.cs` — three tests covering: 401 without token, 200 with valid test JWT, anonymous endpoint stays accessible

### Integration Tests:

- Full round-trip (manual): Entra OTP sign-in → token → `/auth/probe` → 200
- Anonymous route generation unaffected (manual + existing `LoopRouteIntegrationTests.cs`)

### Manual Testing Steps:

1. Start backend: `dotnet run` from `src/backend/VeloRoute/`
2. Start frontend: `npm run dev` from `src/frontend/`
3. In browser console: `await msalInstance.loginRedirect({ scopes: ['api://<web-api-client-id>/user.data'] })`
4. Complete Entra OTP flow; verify redirect back to `localhost:3000`
5. In browser console: `const accounts = msalInstance.getAllAccounts(); const resp = await msalInstance.acquireTokenSilent({ scopes: ['api://<web-api-client-id>/user.data'], account: accounts[0] }); console.log(resp.accessToken)`
6. `curl -H "Authorization: Bearer <token>" http://localhost:5098/auth/probe` → `{"sub":"..."}` 200
7. `curl http://localhost:5098/auth/probe` → 401
8. `curl -X POST http://localhost:5098/routes/loop -H "Content-Type: application/json" -d '{"startLon":0,"startLat":0,"minKm":20,"maxKm":40}' ` → not 401

## References

- Roadmap F-01: `context/foundation/roadmap.md` (lines 67–78)
- PRD v2 auth scope: `context/foundation/prd-v2.md` (lines 80–99)
- Existing backend bootstrap: `src/backend/VeloRoute/Program.cs`
- Test infrastructure: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`
- Frontend entry point: `src/frontend/src/app/layout.tsx`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Azure Tenant + App Registrations

#### Automated

- [ ] 1.1 Frontend `.env.example` updated with NEXT_PUBLIC_ENTRA_* variables
- [ ] 1.2 Backend `appsettings.json` EntraExternalId section added
- [ ] 1.3 OIDC discovery endpoint resolves (curl returns JSON)

#### Manual

- [ ] 1.4 CIAM tenant created; both app registrations visible in Azure portal
- [ ] 1.5 SPA granted delegated permission to user.data scope
- [ ] 1.6 Config values recorded in local .env.local and appsettings.Development.json

### Phase 2: Backend JWT Middleware + Test Infrastructure

#### Automated

- [ ] 2.1 Backend builds: `dotnet build src/backend/`
- [ ] 2.2 All tests pass: `dotnet test src/backend/`
- [ ] 2.3 GET /auth/probe without token → 401 (AuthMiddlewareTests)
- [ ] 2.4 GET /auth/probe with test JWT → 200 (AuthMiddlewareTests)
- [ ] 2.5 POST /routes/loop without token → not 401 (AuthMiddlewareTests)

#### Manual

- [ ] 2.6 `curl http://localhost:5098/auth/probe` → 401
- [ ] 2.7 `curl -H "Authorization: Bearer bad" http://localhost:5098/auth/probe` → 401

### Phase 3: Frontend MSAL Integration + Round-Trip Smoke Test

#### Automated

- [ ] 3.1 Frontend builds: `npm run build` from src/frontend/
- [ ] 3.2 Lint passes: `npm run lint` from src/frontend/
- [ ] 3.3 Vitest tests pass: `npm test` from src/frontend/
- [ ] 3.4 No TypeScript errors in new MSAL files

#### Manual

- [ ] 3.5 Dev server starts without MSAL errors in browser console
- [ ] 3.6 Anonymous route generation works end-to-end without login
- [ ] 3.7 loginRedirect() navigates to Entra CIAM OTP sign-in page
- [ ] 3.8 After OTP login, app redirects back to localhost:3000; token in localStorage
- [ ] 3.9 `curl -H "Authorization: Bearer <token>" .../auth/probe` → 200
- [ ] 3.10 `curl .../auth/probe` (no token) → 401
- [ ] 3.11 POST /routes/loop without token → 200 (anonymous access preserved)
