---
title: "VeloRoute — Anti-Corruption Layer Plan: Clerk Session in the Frontend UI Layer"
created: 2026-09-11
type: refactor-plan
---

# VeloRoute — Anti-Corruption Layer Plan

Scope: this document is a **refactor plan**, not an implementation. No production code is modified as part of producing it.

## Step 0 — Context

Read: `context/foundation/prd-v2.md` (Access Control Changes, Scope of Change → Authentication), `context/foundation/roadmap.md` (F-01 `auth-provider-scaffold`, and the provider-swap history), `README.md`, `src/frontend/README.md`, `.github/copilot-instructions.md`. No document declares Clerk itself as swappable-by-design ("so we can swap X" language does not exist for Clerk anywhere in `context/`). What the roadmap *does* show is that the provider was already swapped once, before implementation: `roadmap.md:77` — "~~Which magic link provider?~~ — Resolved 2026-07-04: Microsoft Entra External ID + email OTP. Superseded 2026-07-07: switched to Clerk + email OTP — Entra CIAM tenant creation blocked by Azure subscription region policy." The swap happened at the planning stage (F-01 had not shipped yet), so it paid no code cost — but it establishes that provider churn is a real, precedented risk for this app, not a hypothetical.

`src/frontend/AGENTS.md` independently flags this exact dependency as high-risk: "verify against the installed package's own types and source under `node_modules/next/`... Heed deprecation notices," and `roadmap.md:78` — "`@clerk/nextjs` + Next.js App Router (RSC) behavior should be checked against current SDK docs, not training data." A dependency already flagged as version-fragile is exactly the one that should have a single point of contact, not many.

Stack/layers (per `.github/copilot-instructions.md`): Next.js 15 App Router frontend (`src/frontend/src/`), .NET 10 minimal-API backend (`src/backend/VeloRoute/`), no shared runtime. Auth: Clerk (`@clerk/nextjs` on the frontend; JWT/JWKS validation against Clerk's endpoints on the backend, `Program.cs:74-90`).

## Step 1 — Leaking dependencies identified

Three external-dependency candidates were checked for cross-layer leakage; two are already well-contained and are recorded here as negative results / positive counter-examples, not overlooked.

**Checked and ruled out:**
- `maplibre-gl` / `@vis.gl/react-maplibre` — only imported in `src/frontend/src/components/RouteMap.tsx`. Single file, no leak.
- `NetTopologySuite` (backend) — only imported in `src/backend/VeloRoute/Routing/OverlapDetector.cs:1-3`. The domain already has its own `RouteCoordinate` (`Routing/RouteResult.cs:17`) and `GeoJsonLineString` (`Data/GeoJsonLineString.cs:3`) types; NTS geometry types never appear in a DTO, entity, or API signature. This is what "well-isolated" looks like and is the bar Step 4 below aims to match on the frontend.
- Clerk on the **backend** — also already well-contained: outbound calls to Clerk's Backend API (account deletion) go through a narrow port, `Auth/IClerkClient.cs:3` / `Auth/ClerkClient.cs:6`, injected via DI (`Program.cs:60-64`). `ClaimsPrincipalExtensions.GetSub()` (`Auth/ClaimsPrincipalExtensions.cs:7`) reads a standard .NET `ClaimsPrincipal`, not a Clerk type. This is a second existing example of the target pattern — evidence the team already knows how to build one.

**The leak — Clerk (`@clerk/nextjs`) on the frontend, UI layer.**

Files that import and call the SDK directly:
- `src/frontend/src/middleware.ts:1,3` — `clerkMiddleware()` (framework composition root; acceptable, see Step 4).
- `src/frontend/src/app/layout.tsx:2,22,32` — `ClerkProvider` (framework composition root; acceptable, see Step 4).
- `src/frontend/src/components/Header.tsx:5,8-10` — `useAuth`, `useClerk`, `useUser`.
- `src/frontend/src/components/RouteInfoPanel.tsx:4,30-31` — `useAuth`, `useUser`.
- `src/frontend/src/app/my-routes/page.tsx:6,17-19` — `useAuth`, `useClerk`, `useUser`.
- `src/frontend/src/app/my-routes/[id]/page.tsx:7,21-23` — `useAuth`, `useClerk`, `useUser`.
- `src/frontend/src/app/account/page.tsx:5,9-11` — `useAuth`, `useClerk`, `useUser`.
- `src/frontend/src/components/RouteInfoPanel.test.tsx` — mocks `@clerk/nextjs` directly (test double against the vendor SDK, not a domain port).

Duplicated reconstruction — the exact same "get a token, build a Bearer header" sequence, hand-written independently 11 times across the 5 non-root files above:
- `Header.tsx:16,19`
- `RouteInfoPanel.tsx:45,50`
- `my-routes/page.tsx:39,41,57,60`
- `my-routes/[id]/page.tsx:56,58,127,133,160,163,182,185,201,204` (five separate occurrences in one file)
- `account/page.tsx:30,33`

Library type leaking past the boundary into rendering, not converted to a domain shape: `Header.tsx:47` — `user.primaryEmailAddress?.emailAddress` reads Clerk's `UserResource` shape directly in JSX. `isSignedIn`, `isLoaded`, and `user` from `useUser()` are likewise threaded straight into 5 components' render logic and effect-dependency arrays with no intermediate type.

By contrast, the Next.js API routes (`src/frontend/src/app/api/**/route.ts`, the frontend's own service layer that proxies to the backend) never import Clerk at all — they only relay a caller-supplied `Authorization` header (`src/frontend/src/lib/apiProxy.ts:1-7`). So the leak is not "Clerk crosses UI into service" — it is "Clerk crosses UI into UI," independently reinvented in every screen that needs a signed-in user, exactly the duplicated-reconstruction signal this step is looking for.

## Step 2 — Classification and selection

| Candidate | Files/layers touched | Swap risk/cost today | Intent-vs-code gap |
|---|---|---|---|
| MapLibre (frontend) | 1 | Low — isolated already | None declared |
| NetTopologySuite (backend) | 1 | Low — isolated already | None declared |
| Clerk, backend outbound (deletion) | 1 port + 1 adapter | Low — isolated already | None declared |
| **Clerk, frontend UI (session/token)** | **6 files** (5 components/pages + 1 test double), **11 duplicated reconstructions**, 1 raw-type read in JSX | **High** — a provider swap or a breaking SDK change (flagged as plausible by `AGENTS.md` and `roadmap.md:78`) means editing 6 files and re-auditing 11 near-identical blocks by hand | Real precedent for provider churn (`roadmap.md:77`, Entra → Clerk) landed with **zero** abstraction in place on this side, unlike the backend's own `IClerkClient` |

Selected: **Clerk on the frontend UI layer.** It is the only candidate that fails on both blast radius (6 files, not 1) and duplication (11 copies of the same reconstruction, not 0), and it is the one candidate where the codebase's own sibling (`IClerkClient` on the backend) proves the fix is already an established local pattern, not a new idea.

## Step 3 — Diagnosis

Duplication, verbatim shape, one representative pair (full list in Step 1):

```
// RouteInfoPanel.tsx:45,50
const token = await getToken();
...
Authorization: `Bearer ${token}`,
```
```
// account/page.tsx:30,33
const token = await getToken();
...
headers: { Authorization: `Bearer ${token}` },
```

Same three lines, independently retyped 11 times. A future change to how the token is obtained or attached (e.g. Clerk changing `getToken()`'s signature, or a future provider swap) requires finding and editing all 11, with no compiler or grep signal tying them together beyond the string `getToken`.

Boundary crossing into render logic: `Header.tsx:47` renders `user.primaryEmailAddress?.emailAddress` — a Clerk `UserResource` field — directly. Nothing downstream of `useUser()` in these 5 files sees a VeloRoute-owned "current user" shape; every consumer sees Clerk's own types (`UserResource | null | undefined`, `isLoaded`/`isSignedIn` booleans with SDK-specific loading semantics).

No document claims Clerk is meant to be swappable — but `roadmap.md:77` shows it already swapped once (Entra → Clerk) for reasons outside the team's control (Azure region policy), and `roadmap.md:78` / `AGENTS.md` both flag the installed SDK as a version-fragile dependency to re-verify against current docs rather than training data. The gap here is not a broken promise in a doc — it is the absence of a promise where the project's own history says one is warranted.

## Step 4 — ACL design

Domain value object — the UI-facing shape, holding no Clerk types:

```ts
// src/frontend/src/lib/auth/session.ts
export interface AuthSession {
  isLoaded: boolean;
  isSignedIn: boolean;
  userEmail: string | null;   // was: user.primaryEmailAddress?.emailAddress
  signIn(): void;              // was: openSignIn()
  signOut(): Promise<void>;    // was: signOut()
}
```

Narrow port (domain-side interface; the rest of the app depends only on this):

```ts
// src/frontend/src/lib/auth/port.ts
export interface AuthSessionPort {
  useSession(): AuthSession;
  authorizedFetch(path: string, init?: RequestInit): Promise<Response>;
  // authorizedFetch owns "get a token, attach Bearer" — the one place
  // the reconstruction happens, replacing all 11 inline copies.
}
```

Adapter (the only file that imports `@clerk/nextjs`'s hooks for session/token reads):

```ts
// src/frontend/src/lib/auth/clerkAdapter.ts
import { useAuth, useClerk, useUser } from '@clerk/nextjs';

export function useClerkSession(): AuthSession {
  const { isLoaded, isSignedIn, user } = useUser();
  const { openSignIn, signOut } = useClerk();
  return {
    isLoaded,
    isSignedIn: !!isSignedIn,
    userEmail: user?.primaryEmailAddress?.emailAddress ?? null,
    signIn: () => openSignIn(),
    signOut: async () => { await signOut(); },
  };
}

export async function clerkAuthorizedFetch(path: string, init?: RequestInit): Promise<Response> {
  const { getToken } = useAuth(); // adapter-internal; not exported past this file
  const token = await getToken();
  return fetch(path, {
    ...init,
    headers: { ...init?.headers, Authorization: `Bearer ${token}` },
  });
}
```

`middleware.ts` and `layout.tsx` keep importing `@clerk/nextjs` directly — they are the Next.js-mandated composition root (route-matcher middleware, provider tree), not domain code reading session state, and NTS/`IClerkClient` show this project already treats root wiring as outside the port/adapter boundary. Everything else — the 5 components/pages in Step 1 — is rewritten to call `useClerkSession()` / `authorizedFetch()` only, never `@clerk/nextjs` directly.

## Step 5 — Isolation proof + before/after

Before (know Clerk today): `middleware.ts`, `layout.tsx`, `Header.tsx`, `RouteInfoPanel.tsx`, `my-routes/page.tsx`, `my-routes/[id]/page.tsx`, `account/page.tsx`, `RouteInfoPanel.test.tsx` — 8 files.

After: `middleware.ts`, `layout.tsx` (root wiring, unchanged), `lib/auth/clerkAdapter.ts` (new, sole adapter) — 3 files. A future provider swap edits 1 file (`clerkAdapter.ts`) instead of 5, and the mechanical `getToken` → `Bearer` reconstruction exists in exactly 1 place instead of 11.

Verification: after the refactor, `grep -rl "@clerk/nextjs" src/frontend/src` must return only `middleware.ts`, `layout.tsx`, and `lib/auth/clerkAdapter.ts` (plus test files exercising the adapter itself, which now mock `AuthSessionPort`/`useClerkSession` instead of the vendor SDK). Any other hit is a regression.

Before/after for a duplicated site (`RouteInfoPanel.tsx`):
```ts
// before
const { isSignedIn } = useUser();
const { getToken } = useAuth();
...
const token = await getToken();
fetch('/api/routes', { headers: { Authorization: `Bearer ${token}` }, ... });

// after
const { isSignedIn } = useClerkSession();
...
authorizedFetch('/api/routes', { ... });
```
The component receives a ready-made domain `AuthSession` and a fetch helper that already knows how to authenticate; it never sees a Clerk type.

No open question here is contingent on Clerk's own contract (unlike, say, a wire-format question) — the port's shape (`AuthSession`, `authorizedFetch`) is a VeloRoute decision, not something Clerk's docs dictate, so nothing is deferred to Step 6.

## Step 6 — Plan

1. **Add the port + adapter.** Create `lib/auth/session.ts` (types), `lib/auth/port.ts` (interface), `lib/auth/clerkAdapter.ts` (Clerk-backed implementation: `useClerkSession`, `authorizedFetch`). No existing file changes yet — additive only.
2. **Migrate the 5 call sites one at a time**, in order of duplication count (highest first, so the biggest win lands early and each migration is independently reviewable):
   - `my-routes/[id]/page.tsx` (5 duplicated blocks)
   - `my-routes/page.tsx` (2)
   - `account/page.tsx` (1)
   - `RouteInfoPanel.tsx` (1)
   - `Header.tsx` (1, plus the `UserResource` read at line 47)
3. **Migrate the test double.** Update `RouteInfoPanel.test.tsx` to mock `useClerkSession`/`authorizedFetch` from the port instead of `@clerk/nextjs`.
4. **Verify isolation.** Run the Step 5 grep; confirm it matches only the 3 expected files.
5. **Regression pass.** `npm test` and `npm run e2e` (per `.github/copilot-instructions.md` dev commands) — the e2e signed-in/signed-out flows in `context/foundation/test-plan.md` §6.5 are the ones most likely to catch a behavioral slip in the adapter.
6. **Docs.** No doc currently claims Clerk is swappable, so none needs correcting; if this ACL ships, `context/foundation/roadmap.md`'s F-01 entry is the natural place to note the frontend now has a Clerk adapter mirroring the backend's `IClerkClient`, for symmetry with that existing note.

## Summary

The frontend calls `@clerk/nextjs`'s `useAuth`/`useUser`/`useClerk` directly from five separate components and pages, hand-reconstructing the same "fetch a token, attach it as a Bearer header" sequence eleven times and reading Clerk's own `UserResource` shape straight into JSX (`Header.tsx:47`) — no domain type sits between the SDK and the UI. This is the worst leak in the codebase by blast radius and duplication count; MapLibre and NetTopologySuite are each confined to one file, and Clerk's *backend* usage is already isolated behind a narrow `IClerkClient` port. The roadmap's own history (`roadmap.md:77`, an Entra→Clerk swap forced by Azure region policy) shows provider churn is a real risk here, and `AGENTS.md`/`roadmap.md:78` flag this exact SDK as version-fragile — yet the frontend has no single point of contact for it. The fix mirrors the backend's existing pattern: an `AuthSession` value object plus a narrow `AuthSessionPort` (`useSession`, `authorizedFetch`), with a `clerkAdapter.ts` as the only file left importing the SDK for session/token reads (root wiring in `middleware.ts`/`layout.tsx` stays as-is). Post-refactor, `grep -rl "@clerk/nextjs" src/frontend/src` narrows from 8 hits to 3, and the token-to-header reconstruction collapses from 11 copies to 1.
