# Auth Provider Scaffold — Plan Brief

> Full plan: `context/changes/auth-provider-scaffold/plan.md`

## What & Why

Wire Microsoft Entra External ID CIAM as VeloRoute's auth provider — the foundation all authenticated features (S-01 through S-06) depend on. No user-facing login UI is built here; this is infrastructure: Azure tenant config, MSAL.js in Next.js, and JWT Bearer middleware in .NET. Without this, no v2 auth slice can start.

## Starting Point

VeloRoute v1 is fully stateless and anonymous — no auth packages, no session code, no auth middleware anywhere in either project. CORS is already configured with `AllowAnyHeader()` so the `Authorization` header passes through without changes.

## Desired End State

A verifiable token chain exists from Entra CIAM to the .NET backend: sign in via Entra OTP, acquire an access token scoped to `user.data`, call the dev-only `/auth/probe` endpoint, and receive 200. The existing anonymous route generation and GPX export flows are completely unaffected — they return 200 without a token, as today.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Auth provider | Entra External ID CIAM | Azure-only; email OTP sign-in; avoids B2C custom-policy complexity (resolved in roadmap) |
| App registration | Separate SPA + Web API | Standard OAuth2 access token flow; clean scope separation for future features; works on free Azure |
| CIAM tenant scope | Separate from Azure hosting subscription | CIAM tenant is an identity directory, unrelated to the subscription running SWA + App Service |
| API scope name | `user.data` | Single broad scope covers all v2 authenticated operations; no granular split needed for a flat user model |
| MSAL interaction | Redirect flow | Works in all browsers including mobile Safari; popup blockers cause silent failures |
| MSAL placement | `layout.tsx` via `MsalProviderWrapper` | Auth context available to all future pages (My Routes, account settings) without making the root layout a client component |
| Token storage | `localStorage` | MSAL default; appropriate for a cycling app; survives page refresh |
| Backend package | `Microsoft.Identity.Web` | Entra-maintained; handles JWKS, issuer, and audience automatically from a config section |
| Backend test strategy | Test JWT factory in WebApplicationFactory | Auth middleware runs in tests; both rejection and acceptance are covered; no Entra dependency in CI |
| Probe endpoint | Dev-only (`IsDevelopment()` gate) | Matches existing Swagger/OpenAPI pattern in `Program.cs:51`; no production surface area |
| Existing component auth | Invisible until S-01 | Zero risk to existing flows; S-01 wires auth state into UI |

## Scope

**In scope:**
- Entra External ID CIAM tenant creation and app registrations (SPA + Web API)
- `@azure/msal-browser` + `@azure/msal-react` installed; MSAL instance config; `MsalProviderWrapper` component; `layout.tsx` wrapped
- `Microsoft.Identity.Web` wired in .NET; `UseAuthentication` + `UseAuthorization` in pipeline
- Dev-only `GET /auth/probe` smoke endpoint
- Test JWT factory + `AuthMiddlewareTests` (3 tests)
- `.env.example` (frontend) and `appsettings.json` (backend) updated with config shapes

**Out of scope:**
- Login/logout UI buttons (S-01)
- User row creation in database (S-01, requires F-02)
- Auth state passed to `RouteForm`, `RouteMap`, `RouteInfoPanel`
- Production Entra tenant (dev/staging only)
- SWA EasyAuth (not configured; stays off)

## Architecture / Approach

```
[User browser]
     │ loginRedirect() → MSAL.js (@azure/msal-browser)
     │ ← redirects to https://<tenant>.ciamlogin.com
     │ ← OTP sign-in → issues access token (user.data scope)
     │
[Next.js frontend (SWA)]
     │ MsalProviderWrapper wraps layout.tsx
     │ Bearer token → Authorization header
     │
[.NET backend (App Service)]
     │ UseAuthentication() validates JWT via JWKS
     │ /auth/probe (dev-only) → 401 or 200
     │ /routes/loop, /routes/gpx → 200 (anonymous, unchanged)
     │
[Entra External ID CIAM JWKS endpoint]
     └── JWT signature verified against rotating public keys
```

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Azure tenant + app registrations | CIAM tenant, two app registrations, config values, `.env.example` updated | Tenant setup errors (wrong redirect URIs, missing scope grant) only surface in Phase 3 smoke test |
| 2. Backend JWT middleware + test infra | Auth middleware wired; probe endpoint; all 43 tests green; new auth tests added | `Microsoft.Identity.Web` config keys differ from raw `JwtBearer` — config section must match exactly |
| 3. Frontend MSAL + round-trip smoke | MSAL wired at app root; full token chain verified manually | CIAM authority URL format (`ciamlogin.com`) vs standard Entra must be correct; missing `knownAuthorities` causes silent auth failure |

**Prerequisites:** Azure subscription (free tier sufficient); access to Azure portal to create External ID CIAM tenant  
**Estimated effort:** ~2 sessions across 3 phases (Phase 1 ≈ 45 min Azure portal work; Phases 2–3 ≈ 1 session each)

## Open Risks & Assumptions

- Entra External ID CIAM is still in public preview for some regions; feature availability and portal UI may differ from GA Entra docs
- Email OTP delivery reliability depends on Entra's email sending infrastructure — not testable locally; must be verified with a real email address during Phase 3 smoke test
- `Microsoft.Identity.Web` version compatibility with .NET 10 — pin to a release tested against .NET 10 (currently supported)

## Success Criteria (Summary)

- `dotnet test` passes (43 existing + 3 new auth tests)
- `npm run build && npm run lint` passes with MSAL packages added
- Full round-trip: Entra OTP login → token → `curl /auth/probe` with Bearer → 200; without Bearer → 401; anonymous `/routes/loop` → 200
