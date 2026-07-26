# Public Route Sharing — Plan Brief

> Full plan: `context/changes/public-route-sharing/plan.md`

## What & Why

Authenticated users can generate a public, unauthenticated link for a saved route and revoke it later. Anyone with the link can view the route (map, name, distance, tags, GPX download) without logging in — closing the last piece of the PRD-v2 route-library scope (FR-009) and letting a cyclist share a specific loop with a friend without exporting a file.

## Starting Point

The library has save (`S-02`), list+detail+GPX (`S-03`), and delete (`S-04`) — all authenticated, all scoped to `Routes` rows the caller owns. No public/unauthenticated page exists yet; every page under `/my-routes/` gates on Clerk. No sharing or snapshot concept exists in the data model.

## Desired End State

From a route's detail page, the owner clicks "Share," gets a copyable `/r/<token>` URL, and can later click "Stop sharing" to kill it. Anyone who opens an active link — no account needed — sees the route on an interactive map and can download its GPX.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Data model | `Shares` table, FK to `Routes`, cascade delete — no snapshot copy | Simpler schema; accepted the tradeoff that a share dies with its source route (see PRD amendment below) |
| Token format | 12-char random base62 via `RandomNumberGenerator.GetString` | Short, enumeration-resistant, zero external dependency (.NET 10 BCL) |
| Delete interaction | Deleting the route also deletes its share (cascade) | **This narrows the original PRD guardrail** ("link must remain stable") — amended live during planning, see below |
| Re-share behavior | Idempotent — `POST` returns the existing token if one exists | One stable link per route while active; DB unique index on `RouteId` enforces this, not just app logic |
| Revocation | **Added mid-planning** — hard-delete the share row; re-share mints a new token, not the same URL | User expanded scope after the initial "not revocable" answer; hard-delete matches the codebase's existing no-soft-delete philosophy (`delete-route`) |
| Revoke confirmation | Plain button, no modal | Revoking only removes a pointer — route itself untouched, re-sharing is one click away; lower stakes than deleting a route |
| Public page content | Map + name + distance + tags + GPX download | Matches the owner's own detail view; PRD frames sharing as a full GPX-export substitute |
| 404 handling | Generic "Route not found" for unknown/revoked/deleted-source tokens | No tombstone/audit trail exists (cascade delete, hard-delete revoke) to distinguish these cases anyway |

**PRD/roadmap amended during this session:** the original PRD language ("once shared, a URL must remain valid") and the roadmap's S-05 risk note (which anticipated a snapshot table specifically to avoid this) were both updated to reflect the FK-cascade + revocable design actually chosen. See `context/foundation/prd-v2.md` Constraints and `context/foundation/roadmap.md` S-05, both dated 2026-07-26.

## Scope

**In scope:**
- `Shares` table + migration
- `POST`/`DELETE /routes/{id}/share` (owner-scoped, idempotent, revocable)
- Public `GET /shares/{token}` + `GET /routes/{id}` extended with `shareToken`
- Detail-page Share/Copy/Stop-sharing UI
- New public `/r/[token]` page (map, GPX download, "Plan your own route" link)

**Out of scope:**
- Snapshot/copy of route data (live read-through instead)
- Preserving the same URL across a revoke → re-share cycle
- View counts, analytics, or a "my shares" list page
- Social-share buttons, expiration/TTL
- Distinguishing *why* a token 404s (never-existed vs. revoked vs. source deleted)

## Architecture / Approach

Backend adds one new entity (`Shares`, FK-cascaded to `Routes`) and four endpoint changes, reusing the existing 404-collapsing ownership-check pattern from `GET`/`DELETE /routes/{id}`. Frontend adds two new proxy-route files (`api/routes/[id]/share/`, `api/shares/[token]/`) plus one new public page (`app/r/[token]/`) that deliberately duplicates — rather than shares a component with — the existing detail page's map/GPX-download logic, since the two pages differ in a load-bearing way (auth gating) and the shared surface is small.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend — Shares data model + endpoints | Migration, share/unshare/public-lookup endpoints, extended detail response | Check-then-insert race on idempotent share creation — mitigated by a DB unique index + catch-and-re-query, not app logic alone |
| 2. Frontend — share UI + public page | Proxy routes, detail-page share controls, new public page | None significant — mostly wiring against patterns already proven in `delete-route`/`route-library` |

**Prerequisites:** `S-02` (save-route) — done. No blockers.
**Estimated effort:** ~2 sessions across 2 phases, similar scope to `delete-route`.

## Open Risks & Assumptions

- The narrowed link-stability guarantee (dies with the route, revocable) is a live product decision made during this planning session, not a re-confirmation of something already validated with real users — worth a gut-check once real sharing usage exists.
- `RandomNumberGenerator.GetString` is assumed available in this project's .NET 10 SDK (introduced .NET 8); not independently re-verified beyond BCL documentation recall.

## Success Criteria (Summary)

- Owner can share a route, get a working public link, and revoke it; the link 404s immediately after revoke or after the source route is deleted.
- A signed-out visitor can view and download GPX from an active link with zero friction.
- Re-sharing after a revoke always produces a new, different URL.
