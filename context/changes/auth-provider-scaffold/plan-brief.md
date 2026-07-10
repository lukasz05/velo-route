# Auth Provider Scaffold — Plan Brief

> Full plan: `context/changes/auth-provider-scaffold/plan.md`

## What & Why

Wire Clerk as VeloRoute's auth provider — the foundation all authenticated features (S-01 through S-06) depend on. No user-facing login UI is built here; this is infrastructure: Clerk application config, `@clerk/nextjs` in Next.js, and JWT Bearer middleware in .NET. Without this, no v2 auth slice can start.

**Provider pivot (2026-07-07):** originally Microsoft Entra External ID CIAM. Blocked at tenant creation — Entra's `ciamDirectories` resource type only deploys to broad meta-regions (Global/US/Europe/APAC/Australia/Japan), none of which intersect the available Azure subscription's system-enforced region allowlist. That policy is tied to the "Azure for Students" offer and isn't customer-removable. Rather than move the Postgres subscription too, only F-01 pivoted — to Clerk, which has zero Azure dependency. F-02 (Postgres on Azure) is unaffected.

## Starting Point

VeloRoute v1 is fully stateless and anonymous — no auth packages, no session code, no auth middleware anywhere in either project. CORS is already configured with `AllowAnyHeader()` so the `Authorization` header passes through without changes.

## Desired End State

A verifiable token chain exists from Clerk to the .NET backend: sign in via Clerk email OTP, acquire a session token, call the dev-only `/auth/probe` endpoint, and receive 200. The existing anonymous route generation and GPX export flows are completely unaffected — they return 200 without a token, as today.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Auth provider | Clerk | Free tier (10k MAU); no Azure dependency; native email OTP/magic link; JWKS-based JWT keeps backend architecture provider-agnostic |
| Backend package | Generic `Microsoft.AspNetCore.Authentication.JwtBearer` | No Clerk-maintained .NET package exists; `Authority` + JWKS discovery configured explicitly |
| Audience validation | Custom `azp` check in `OnTokenValidated` | Clerk tokens don't carry a conventional `aud` claim by default; `azp` (authorized party) is the equivalent signal to prevent cross-app token reuse on the same Clerk instance |
| Frontend SDK | `@clerk/nextjs` | Official Next.js App Router SDK; `<ClerkProvider>` wraps server components without a separate client wrapper, unlike MSAL |
| Backend test strategy | Test JWT factory in WebApplicationFactory | Auth middleware runs in tests; both rejection and acceptance are covered; no Clerk dependency in CI (unchanged from original Entra plan — provider-agnostic design) |
| Probe endpoint | Dev-only (`IsDevelopment()` gate) | Matches existing Swagger/OpenAPI pattern in `Program.cs:51`; no production surface area |
| Existing component auth | Invisible until S-01 | Zero risk to existing flows; S-01 wires auth state into UI |

## Scope

**In scope:**
- Clerk application creation; email OTP (or magic link) sign-in method enabled
- `@clerk/nextjs` installed; `<ClerkProvider>` wraps `layout.tsx`; `middleware.ts` with `clerkMiddleware()`
- Generic `JwtBearer` wired in .NET against Clerk's JWKS; `UseAuthentication` + `UseAuthorization` in pipeline
- Dev-only `GET /auth/probe` smoke endpoint
- Test JWT factory + `AuthMiddlewareTests` (3 tests)
- `.env.example` (frontend) and `appsettings.json` (backend) updated with config shapes

**Out of scope:**
- Login/logout UI buttons (S-01)
- User row creation in database (S-01, requires F-02)
- Auth state passed to `RouteForm`, `RouteMap`, `RouteInfoPanel`
- Production Clerk instance (dev instance only)
- SWA EasyAuth (not configured; stays off)
- Any Azure Entra External ID work — superseded, do not resume without re-opening the roadmap decision

## Architecture / Approach

```
[User browser]
     │ Clerk sign-in flow (@clerk/nextjs)
     │ ← email OTP entry → session established
     │
[Next.js frontend (SWA)]
     │ <ClerkProvider> wraps layout.tsx; middleware.ts enables auth()
     │ Bearer token → Authorization header
     │
[.NET backend (App Service)]
     │ UseAuthentication() validates JWT via Clerk JWKS + azp check
     │ /auth/probe (dev-only) → 401 or 200
     │ /routes/loop, /routes/gpx → 200 (anonymous, unchanged)
     │
[Clerk Frontend API JWKS endpoint]
     └── JWT signature verified against rotating public keys
```

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Clerk application setup | Application, email OTP enabled, config values, `.env.example` updated | None Azure-related; risk is scope-name/env-var mismatch surfacing late in Phase 3 |
| 2. Backend JWT middleware + test infra | Auth middleware wired; probe endpoint; all 43 tests green; new auth tests added | No official Clerk .NET package — generic `JwtBearer` OIDC discovery against Clerk's endpoint is unverified; may need manual `MetadataAddress` fallback |
| 3. Frontend Clerk + round-trip smoke | Clerk wired at app root; full token chain verified manually | `<ClerkProvider>`/`middleware.ts` behavior against this Next.js 15/React 19 version is unverified against training data — check `node_modules` docs before assuming API shape |

**Prerequisites:** Clerk account (free); no Azure subscription needed for this phase
**Estimated effort:** ~2 sessions across 3 phases (Phase 1 ≈ 15 min dashboard work; Phases 2–3 ≈ 1 session each)

## Open Risks & Assumptions

- No official Clerk-maintained .NET package — generic `JwtBearer` + `Authority`/`MetadataAddress` config is unverified against Clerk's actual OIDC discovery behavior until Phase 2 implementation
- Clerk's `azp` claim behavior (vs `aud`) needs confirming against a real issued token before the custom validation check can be trusted
- `@clerk/nextjs` behavior against this project's Next.js 15/React 19 (App Router, RSC) needs checking against current SDK docs, not training data (per `src/frontend/AGENTS.md`)

## Success Criteria (Summary)

- `dotnet test` passes (43 existing + 3 new auth tests)
- `npm run build && npm run lint` passes with Clerk packages added
- Full round-trip: Clerk email OTP login → session token → `curl /auth/probe` with Bearer → 200; without Bearer → 401; anonymous `/routes/loop` → 200
</content>
