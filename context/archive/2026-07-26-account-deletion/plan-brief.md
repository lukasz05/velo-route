# Account Deletion — Plan Brief

> Full plan: `context/changes/account-deletion/plan.md`

## What & Why

Roadmap slice S-06: let an authenticated user permanently delete their own account (email/identity + all saved routes + shares) self-serve from account settings, with no support contact required. Closes the last v2 NFR gap.

## Starting Point

Auth (Clerk magic-link) and the data layer (Postgres via EF Core, `Users`→`Routes`→`Shares` all FK-cascade-deleted already) are both done and unmodified by this plan. No existing integration with Clerk's Backend API exists anywhere in the repo — the backend only validates inbound JWTs today. No account-settings page exists on the frontend.

## Desired End State

User opens `/account`, sees their email, clicks "Delete Account", types `DELETE` to confirm, and their account is gone from both Postgres and Clerk. They're signed out and land on `/` with a one-time "your account and data have been deleted" message. Anonymous route generation is untouched.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Where the Clerk-identity delete call lives | Backend (.NET), new `IClerkClient` | Keeps "delete account" as one atomic backend operation, matching the repo's convention that the backend owns all data mutation. |
| Delete ordering / partial-failure handling | Postgres first, then Clerk (best-effort) | A failed Clerk call self-heals via the existing `auth/sync` upsert if the user logs in again; the reverse order risks permanently orphaned, unreachable route data. |
| Confirmation UX strength | Typed "DELETE" confirmation, extending `ConfirmModal` | Matches the roadmap's own risk note that a single button click isn't enough safeguard for a whole-account irreversible action. |
| Endpoint shape | `DELETE /account`, no id param | Target is always the caller's own `sub` from the JWT — structurally impossible to target another account. |
| Post-delete UX | Sign out + redirect to `/` with a one-time query-param banner | Reuses the existing inline-message convention (no toast library); user lands on the still-functional anonymous planner immediately. |
| Clerk-call failure test coverage | Dedicated integration test with a throwing fake client | Proves the safety property the delete-ordering decision relies on. |
| Account page scope | Minimal — email + delete section only | No other account-settings item exists anywhere in the roadmap/PRD; avoids building a settings framework nobody asked for. |

## Scope

**In scope:** `DELETE /account` backend endpoint, new Clerk Backend API client, cascade verification, `/account` frontend page, typed-confirmation modal, sign-out + redirect UX, nav link.

**Out of scope:** soft-delete/grace period, admin-initiated deletion, general settings framework, background job for the Clerk call, new toast/notification system.

## Architecture / Approach

Two independent deletes, no shared transaction: hard-delete the Postgres `Users` row (cascades routes+shares via existing FK config — no migration needed), then best-effort call Clerk's Backend API (`DELETE /v1/users/{id}`) to remove the identity. The endpoint always returns `204` once the Postgres delete commits; a Clerk-call failure is logged, not surfaced, and self-heals via the existing `auth/sync` upsert.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Clerk API client | `IClerkClient`/`ClerkClient` + config, mirrors `IOpenRouteServiceClient` pattern | Clerk Backend API response shape should be verified against current docs, not assumed |
| 2. `DELETE /account` + tests | Endpoint + cascade verification + partial-failure test | Proving the self-healing safety property under test |
| 3. Proxy route + modal | `/api/account` proxy, typed-confirmation `ConfirmModal` extension | Must not regress existing route-delete modal usage |
| 4. Account page + UX | `/account` page, nav link, sign-out + one-time banner | `useSearchParams` needs a Suspense boundary in Next 15 App Router |

**Prerequisites:** S-01 (magic-link-auth) and F-02 (data-layer-schema) — both done.
**Estimated effort:** ~1-2 sessions across 4 phases.

## Open Risks & Assumptions

- Clerk Backend API's exact `DELETE /v1/users/{id}` response contract is assumed from general Clerk API conventions and should be double-checked against current Clerk docs during Phase 1 implementation.
- No transactional guarantee across Postgres + Clerk; the plan accepts and documents the specific asymmetric failure mode (Clerk-side lingering identity, self-healing) as the least-bad option.

## Success Criteria (Summary)

- User can delete their account end-to-end from `/account`; Postgres rows and the Clerk identity are both gone, and the old session no longer authenticates.
- Anonymous route generation and GPX export are unaffected.
- A simulated Clerk-API failure still results in the Postgres-side data being permanently deleted (verified by automated test).
