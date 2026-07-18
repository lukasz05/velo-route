# Magic Link Auth — Plan Brief

> Full plan: `context/changes/magic-link-auth/plan.md`

## What & Why

Give VeloRoute its first real sign-up/log-in/log-out UX, via Clerk's
prebuilt magic-link (email_link) components. This is S-01 on the v2
roadmap — the gate everything else in the auth/library stream (S-02, S-03,
S-06) sits behind. All the hard infra (JWT validation, Postgres schema) is
already done; this slice is UI plus one small backend endpoint.

## Starting Point

F-01 wired Clerk's JWT validation into the .NET backend and `<ClerkProvider>`
into the Next.js frontend, but built no UI and left the sign-in strategy
(email code vs magic link) explicitly undecided. F-02 built the `Users`/
`Routes` schema but nothing creates a `Users` row yet. No header/nav exists
anywhere in the frontend today.

## Desired End State

A person can click "sign in" in a new header, enter their email in a Clerk
modal, click the magic link in their inbox, and land back on the app signed
in — no page reload. Their `Users` row now exists in Postgres. They can log
out and the header resets. None of this touches anonymous route generation
or GPX export, which keep working exactly as before.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Sign-in strategy | Magic link (`email_link`), not OTP code | Matches the change-id and PRD's Access Control section; roadmap's "6-digit code" wording was F-01's unresolved leftover, now corrected | Plan |
| Component approach | Clerk prebuilt `<SignedIn>`/`<SignedOut>` + modal (`openSignIn`/`openSignUp`), not custom hooks | Clerk's default UI already handles expiry errors + resend; a custom flow would duplicate that for no benefit | Plan |
| Cross-device magic links | Off — "require same device" stays on (Clerk's own default) | Keeps this fully within prebuilt-component territory; cross-device needs the custom `handleEmailLinkVerification` hook this plan explicitly avoids | Plan |
| User-row creation | Dedicated `POST /auth/sync` endpoint, idempotent upsert | Explicit single write point mirroring the existing `/auth/probe` pattern; frontend calls it once after sign-in | Plan |
| Header placement | New minimal header component in `layout.tsx` | Standard slot; sets up where S-03's "My Routes" link will live later without building it now | Plan |
| Anonymous→signed-in handoff | Nothing carries over; map/form resets on login | Zero extra state code; there's nothing to save into until S-02 exists anyway | Plan |
| Post-login landing | Same page (route planner); header updates in place | No `/account` page exists yet — redirecting to one would be scope creep into S-03 | Plan |
| Account-area scope | Sign-in/sign-up/logout only — no "My Routes" stub link | Keeps this slice to exactly what S-01's roadmap outcome promises | Plan |

## Scope

**In scope:**
- Clerk Dashboard config: magic-link-only, same-device required
- New `Header` component: sign-in trigger (signed-out) / user indicator +
  logout (signed-in)
- `POST /auth/sync`: idempotent `Users` row creation from the JWT `sub`
- Integration tests for the sync endpoint (401 / create / idempotent)

**Out of scope:**
- `/account` or `/my-routes` pages (S-03, S-06)
- Custom hook-based email-link verification flow
- Password/SSO strategies
- Preserving an anonymous route across the login transition
- Admin roles, app-level rate limiting

## Architecture / Approach

```
[Browser]
  │ click "Sign in" in Header → openSignIn() modal
  │ enter email → Clerk sends magic-link email
  │ click link (same tab/browser) → Clerk auto-completes, modal closes
  │
[Header component]
  │ useAuth().isSignedIn → true → effect fires POST /auth/sync (Bearer token)
  │
[.NET backend]
  │ JWT validated (existing F-01 middleware) → upsert Users row by `sub`
  │
[Postgres]
  └── Users row exists; Routes cascade-delete FK already in place (F-02)
```

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Clerk magic-link UI | Dashboard config + Header component; full sign-in/out UX works | Whether the prebuilt modal actually auto-completes on same-device link click is unconfirmed until manually spiked |
| 2. Backend JIT provisioning | `/auth/sync` endpoint; `Users` row created on first sign-in, idempotently | Upsert must be safe against rapid duplicate calls — relies on the PK constraint as the real safety net |

**Prerequisites:** F-01 and F-02 both done (confirmed); Clerk Dashboard
access
**Estimated effort:** ~1–2 sessions across 2 phases

## Open Risks & Assumptions

- Clerk's prebuilt component's same-device auto-complete behavior is
  inferred from its type surface, not confirmed in prose docs for this
  exact version (7.5.13) — Phase 1 includes a manual spike to verify before
  the rest of the phase's UI work leans on the assumption
- Email deliverability is outside the app's control (Clerk free-tier limits)
  — carried over as a known risk from the roadmap, not newly introduced here

## Success Criteria (Summary)

- A person can sign up, log in, and log out via magic link with no page
  reloads, and see the header reflect their auth state correctly
- Exactly one `Users` row exists per Clerk identity, even across repeat
  sign-ins
- Anonymous route generation and GPX export are provably unaffected
