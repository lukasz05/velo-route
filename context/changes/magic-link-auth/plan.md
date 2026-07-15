# Magic Link Auth Implementation Plan

## Overview

Add user-facing sign-up, log-in, and log-out to VeloRoute via Clerk's prebuilt
magic-link (email_link) components in modal mode, plus a backend endpoint
that creates the `Users` row on a person's first sign-in. This is S-01 on the
v2 roadmap — it builds entirely on already-shipped infrastructure (F-01's JWT
middleware, F-02's Postgres schema) and adds no new infra of its own.

## Current State Analysis

The backend (`src/backend/VeloRoute/Program.cs`) already validates Clerk JWTs
via JWKS (`AddJwtBearer`, `Program.cs:57-85`), exposes a dev-only
`GET /auth/probe` that reads the `sub` claim (`Program.cs:103-105`), and
leaves `POST /routes/loop` / `POST /routes/gpx` anonymous. No code anywhere
creates a `Users` row — F-02 built the schema and cascade-delete FK, but
provisioning was explicitly deferred to this slice.

The frontend (`src/frontend/src/app/layout.tsx`) already wraps the app in
`<ClerkProvider>`, and `middleware.ts` runs `clerkMiddleware()` on every route
except static assets. No Clerk UI component is used anywhere yet — no
`<SignIn>`, `<SignedIn>`, `useAuth`, or `useClerk` calls exist in the
codebase. There is no header/nav component; `page.tsx` renders only
`<RouteApp/>`.

The Clerk Dashboard sign-in strategy is currently ambiguous — F-01's plan
repeatedly wrote "email OTP (or magic link)" without disambiguating, and no
record exists of which was actually toggled on. This plan resolves that:
magic link (`email_link` strategy), matching the change-id and the PRD's
Access Control section (roadmap's S-01 outcome text said "6-digit one-time
code," which was the unresolved wording — corrected in `roadmap.md` as part
of this change).

## Desired End State

- A user can click a sign-in control in a new header, enter their email in
  Clerk's prebuilt modal, receive a magic-link email, click it, and land back
  on the app signed in — without a full-page navigation.
- On first sign-in, a `Users` row is created keyed by the Clerk `sub` claim;
  repeat sign-ins are idempotent (no duplicate rows, no errors).
- The header shows a logged-in indicator and a logout control when signed in;
  clicking logout returns to the signed-out header state.
- Anonymous route generation and GPX export continue to work unauthenticated,
  unaffected by any of the above (unchanged behavior, verified by existing
  tests).
- `dotnet build`, `dotnet test`, `npm run build`, and `npm run lint` all pass.

### Key Discoveries:

- `src/frontend/src/middleware.ts:1-7` — `clerkMiddleware()` matcher already
  covers `/`; no middleware changes needed since the magic-link's target page
  is wherever the modal was opened from (same page, per the "same device"
  Dashboard setting this plan enables — see Critical Implementation Details).
- `src/frontend/.env.example:5-7` — `NEXT_PUBLIC_CLERK_SIGN_IN_URL=/sign-in`
  is a stale placeholder from F-01; this plan uses modal mode
  (`openSignIn()`/`openSignUp()`), which needs no dedicated route, so this
  env var is removed rather than acted on.
- `src/backend/VeloRoute/Program.cs:103-105` — `/auth/probe`'s
  `RequireAuthorization()` + `sub`-claim-read pattern is the direct precedent
  for the new `/auth/sync` endpoint.
- `src/backend/VeloRoute.Tests/Routing/AuthMiddlewareTests.cs` — existing
  3-test pattern (`TestJwtFactory.CreateToken`, no-token → 401, valid token →
  200) is the precedent to extend for `/auth/sync`'s tests.
- `src/backend/VeloRoute.Tests/Data/PostgresFixture.cs` (from F-02 Phase 2) —
  Testcontainers-backed real-Postgres fixture, needed here to verify the
  idempotent-upsert behavior against a real unique constraint, not an
  in-memory stand-in.
- F-02's `Data/User.cs` is `record User(string Id, DateTimeOffset CreatedAt)`
  — no email field. This plan does not add one: Clerk remains the sole source
  of identity data beyond the row's own primary key, per F-02's explicit
  design decision (account deletion cascades the Postgres row; the email
  itself is deleted via Clerk's Backend API in S-06).

## What We're NOT Doing

- No `/account` or `/my-routes` page — S-03 and S-06 own those areas. This
  slice's UI surface is limited to the header's sign-in/sign-up/logout
  controls.
- No custom `useSignIn`/`useSignUp` hook-based flow — Clerk's prebuilt
  `<SignedIn>`/`<SignedOut>` components and `openSignIn()`/`openSignUp()`
  modal triggers are sufficient; no `handleEmailLinkVerification` custom
  cross-device handling (see Critical Implementation Details).
- No password or SSO sign-in strategies — magic link only, per the PRD's
  Access Control section.
- No preservation of an anonymously-generated route across the login
  transition — the map/form resets; there is nothing to save into until S-02
  exists anyway.
- No admin role or multi-tier permissions — flat user model, unchanged from
  the PRD.
- No app-level rate limiting on sign-up/sign-in — Clerk's own free-tier
  limits are the ceiling, consistent with F-01/F-02's precedent of not adding
  app-level throttling.

## Implementation Approach

Two phases, split along the frontend/backend seam: (1) the Clerk Dashboard
config + header UI that lets a person actually sign in and out, (2) the
backend endpoint that turns a signed-in Clerk identity into a `Users` row.
Phase 1 is independently verifiable (sign in/out works) before Phase 2 wires
persistence on top of it.

## Critical Implementation Details

**Magic-link same-device setting is load-bearing, not cosmetic.** Clerk's
`email_link` strategy has a Dashboard toggle, "require the same device and
browser." Leaving it **on** (Clerk's own recommended default) means the tab
that opened the sign-in modal auto-completes when the link is clicked in that
same browser — Clerk's prebuilt component polls and resolves this
automatically, with no code on our side. Turning it **off** enables
cross-device magic links (e.g. click on phone, session resolves on desktop),
but that path requires `useClerk().handleEmailLinkVerification()` — the
custom-flow API this plan explicitly avoids using. Phase 1 sets this toggle
on and includes a manual spike (send a real email, click it) to confirm the
prebuilt modal actually auto-completes before relying on that assumption for
the rest of the phase's UI work.

**`/auth/sync` must be idempotent at the database level, not just in
application logic.** Two rapid calls (e.g. a re-render firing the sync effect
twice) must not throw or create duplicate rows. Use an upsert pattern
(`INSERT ... ON CONFLICT (Id) DO NOTHING` via EF Core's
`ExecuteUpdateAsync`/raw SQL, or a check-then-insert guarded by the existing
`Users.Id` primary key constraint catching `DbUpdateException` on conflict)
— the primary key itself is the safety net; the test in Phase 2 verifies
calling the endpoint twice with the same `sub` leaves exactly one row.

## Phase 1: Clerk Magic-Link UI

### Overview

Configure Clerk's Dashboard for magic-link-only sign-in, and add a header
component with sign-in/sign-up (modal) and logout controls.

### Changes Required:

#### 1. Clerk Dashboard configuration

**Intent**: Disambiguate F-01's left-open sign-in strategy decision — enable
magic link, disable other first-factor strategies, and lock in the
same-device setting this plan's UX depends on.

**Contract**: Manual, via Clerk Dashboard (User & Authentication →
Email, Phone, Username): enable "Email verification link," disable
"Email verification code" and password sign-in if currently enabled; under
the email link's advanced settings, confirm "require the same device and
browser" is on.

#### 2. Frontend env cleanup

**File**: `src/frontend/.env.example`

**Intent**: Remove the stale `NEXT_PUBLIC_CLERK_SIGN_IN_URL` placeholder from
F-01 — modal mode needs no dedicated sign-in route.

**Contract**: Delete the `NEXT_PUBLIC_CLERK_SIGN_IN_URL=/sign-in` line;
`NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` and `CLERK_SECRET_KEY` remain.

#### 3. Header component

**File**: `src/frontend/src/components/Header.tsx` (new)

**Intent**: Give the app its first nav/identity surface — signed-out state
shows a sign-in trigger; signed-in state shows a logged-in indicator and
logout control.

**Contract**: Client component (`"use client"`). Uses Clerk's `<SignedOut>`
wrapping a button that calls `useClerk().openSignIn()`; `<SignedIn>` wrapping
the current user's identifier (`useUser()`) plus a logout button calling
`useClerk().signOut()`. No new routes — modal mode only. Clerk's prebuilt
`<SignIn>` modal includes its own "don't have an account? sign up" link
internally (handles the sign-up branch without a second component wired on
our side), and its default UI includes expiry-error messaging and a resend
control natively — the plan does not build custom copy for either.

**File**: `src/frontend/src/app/layout.tsx`

**Intent**: Mount the header above the app content, globally.

**Contract**: Render `<Header/>` inside `<ClerkProvider>`, above
`{children}`.

### Success Criteria:

#### Automated Verification:

- Frontend builds: `npm run build` (from `src/frontend/`)
- Lint passes: `npm run lint`

#### Manual Verification:

- Clicking the header's sign-in control opens Clerk's modal without a page
  navigation
- Entering a real email and completing magic-link sign-up creates a Clerk
  user and the header switches to the signed-in state without a full page
  reload (confirms the same-device auto-complete assumption from Critical
  Implementation Details)
- Clerk's modal shows a working resend option and a clear error on an
  expired/already-used link
- Clicking logout returns the header to the signed-out state
- Anonymous route generation (`/routes/loop`, `/routes/gpx` via the existing
  UI) still works fully signed out

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Backend JIT User Provisioning

### Overview

Add an idempotent `/auth/sync` endpoint that creates the `Users` row on
first sign-in, called once by the frontend right after a successful Clerk
sign-in.

### Changes Required:

#### 1. `/auth/sync` endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Turn a validated Clerk JWT into a `Users` row, idempotently.
Mirrors `/auth/probe`'s existing pattern for reading the `sub` claim and
requiring authorization.

**Contract**: `POST /auth/sync`, `RequireAuthorization()`. Reads `sub` the
same way `/auth/probe` does (`ClaimTypes.NameIdentifier` with a raw `"sub"`
fallback). Upserts a `Users` row keyed by that id (see Critical
Implementation Details for the idempotency requirement) and returns 200 with
no body (or the row's `CreatedAt`, implementer's choice — no client
currently reads the response beyond status code).

#### 2. Frontend sync call

**File**: `src/frontend/src/components/Header.tsx`

**Intent**: Trigger the backend sync once per sign-in, without requiring the
user to take any extra action.

**Contract**: In the signed-in branch, an effect keyed on `useAuth().isSignedIn`
becoming `true` calls `POST /auth/sync` with the session's bearer token
(via `useAuth().getToken()`). The endpoint's idempotency (Critical
Implementation Details) makes redundant calls from re-renders harmless, so no
one-shot guard beyond the effect's own dependency array is needed.

#### 3. Sync endpoint tests

**File**: `src/backend/VeloRoute.Tests/Routing/AuthMiddlewareTests.cs` (or a
new sibling file if the existing one is kept probe-only — implementer's
call)

**Intent**: Verify the idempotency contract Phase 2 depends on, using the
same Testcontainers-backed real-Postgres fixture F-02 already built (an
in-memory provider would not catch a unique-constraint violation).

**Contract**: Extend the `PostgresFixture`-backed test collection with three
cases: (a) no token → 401; (b) valid token with a new `sub` → creates exactly
one `Users` row; (c) valid token with an already-provisioned `sub`, called
twice → still exactly one row, no exception.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- New sync-endpoint tests pass: no-token → 401, new-sub → row created,
  repeat-sub → idempotent

#### Manual Verification:

- Full round trip: sign up via magic link in the running app → inspect
  Postgres (`psql` or GUI) → confirm exactly one `Users` row with the
  signed-in Clerk user's `sub` as `Id`
- Sign out and sign back in with the same account → confirm no duplicate row
  appears
- CI (`backend.yml`) passes unmodified on a pushed branch

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- No meaningful unit-level (non-HTTP, non-DB) logic exists in this slice to
  isolate — the sync endpoint's only real behavior is the upsert, which is
  exercised at integration level (Phase 2).

### Integration Tests:

- `/auth/sync`: no-token 401, new-user row creation, idempotent re-sync
  (Phase 2)
- Existing `AuthMiddlewareTests` (401/200/anonymous-passthrough) continue to
  pass unmodified, confirming this slice doesn't regress F-01's middleware

### Manual Testing Steps:

1. Sign up via a real email through the header's magic-link modal; confirm
   the header updates to signed-in state without a page reload
2. Inspect Postgres to confirm one `Users` row was created for that account
3. Sign out, sign back in with the same email; confirm the header returns to
   signed-in state and no duplicate `Users` row was created
4. Confirm anonymous route generation and GPX export are unaffected
   throughout

## Performance Considerations

None expected — this slice adds one lightweight upsert on sign-in, no
list/query endpoints yet (those start with S-03).

## Migration Notes

No schema changes — `Users` table already exists from F-02. This slice only
adds the code path that populates it.

## References

- Roadmap: `context/foundation/roadmap.md` (S-01: Magic link auth)
- PRD: `context/foundation/prd-v2.md` (FR-001–FR-003, US-01, Access Control)
- F-01 (auth middleware, JWT precedent):
  `context/archive/2026-07-04-auth-provider-scaffold/plan.md`
- F-02 (schema, Testcontainers test infra precedent):
  `context/archive/2026-07-10-data-layer-schema/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Clerk Magic-Link UI

#### Automated

- [x] 1.1 Frontend builds: `npm run build` — 1e6e638
- [x] 1.2 Lint passes: `npm run lint` — 1e6e638

#### Manual

- [x] 1.3 Sign-in control opens Clerk modal without page navigation — 1e6e638
- [x] 1.4 Magic-link sign-up completes and header updates without full reload — 1e6e638
- [x] 1.5 Resend option and expired-link error message work as expected — 1e6e638
- [x] 1.6 Logout returns header to signed-out state — 1e6e638
- [x] 1.7 Anonymous route generation/GPX export still work signed out — 1e6e638

### Phase 2: Backend JIT User Provisioning

#### Automated

- [x] 2.1 Backend builds: `dotnet build src/backend/VeloRoute/VeloRoute.csproj`
- [x] 2.2 All backend tests pass: `dotnet test src/backend/VeloRoute.Tests/VeloRoute.Tests.csproj`
- [x] 2.3 Sync endpoint tests pass: 401, row creation, idempotent re-sync

#### Manual

- [x] 2.4 Full round trip creates exactly one `Users` row, verified in Postgres
- [x] 2.5 Re-sign-in does not create a duplicate row
- [ ] 2.6 CI (`backend.yml`) passes unmodified on a pushed branch
