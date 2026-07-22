# Delete Route (S-04) — Plan Brief

> Full plan: `context/changes/delete-route/plan.md`

## What & Why

Authenticated users need to remove routes from their saved library — a hard delete behind a confirmation prompt, per PRD FR-006. This closes the CRUD loop that `save-route` (S-02) and `route-library` (S-03) opened: users can now save, browse, view, and remove routes.

## Starting Point

`route-library` shipped `GET /routes` (list) and `GET /routes/{id}` (detail), both scoped to the caller with a 404-collapsing not-found/not-owned check. No write/delete endpoint exists yet, and no confirmation-dialog component exists anywhere in the frontend.

## Desired End State

A signed-in user can delete a route from its row in `/my-routes` (row disappears in place) or from the open `/my-routes/<id>` page (redirects to the list). Either path opens the same confirmation modal naming the route before anything is destroyed. The deletion is immediate, permanent, and irreversible.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Trigger location | Both list row and detail page | Delete should work wherever the user is looking at the route, no forced navigation | Plan (user Q&A) |
| Confirmation UI | Custom modal (Cancel/Delete) | Matches the app's styled-component convention; PRD calls a prompt "sufficient," not requiring stronger friction | Plan (user Q&A) |
| Post-delete UX | Detail → redirect to list; list → remove row in place | Context-appropriate — no dead page, no jarring full-list reload | Plan (user Q&A) |
| Failure handling | Inline error text; 404-on-delete treated as success | Matches existing inline-error convention; a "still gone" race isn't a real error | Plan (user Q&A) |
| List-row update | Pessimistic (wait for backend confirmation) | Matches how Save/Download buttons already disable during in-flight requests; avoids new rollback UI | Plan (user Q&A) |
| Ownership check | Reuse the detail endpoint's `SingleOrDefaultAsync(Id==id && UserId==sub)` 404-collapse | Same pattern already proven twice in this codebase | Plan (research) |
| Modal design | Generic reusable `ConfirmModal`, not delete-specific | `account-deletion` (S-06) will need the same shape next | Plan (research) |

## Scope

**In scope:**
- `DELETE /routes/{id:guid}` backend endpoint (auth + ownership scoped, 404 collapse, hard delete)
- `DELETE` handler on the existing `/api/routes/[id]` proxy route
- Reusable `ConfirmModal` component
- Delete affordance + wiring on both `/my-routes` (list) and `/my-routes/[id]` (detail)

**Out of scope:**
- Soft-delete, undo, or trash/recovery
- Bulk/multi-select delete
- "Type DELETE to confirm" friction
- Optimistic list-row removal
- Account deletion (S-06 — separate roadmap slice, though it will reuse `ConfirmModal`)

## Architecture / Approach

Backend first: one new minimal-API endpoint reusing the existing ownership/404 pattern. Frontend: a small reusable `ConfirmModal` component, a `DELETE` export added to the existing dynamic proxy route file, then wiring into the two existing My Routes pages — no new pages, no new data model.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Backend delete endpoint | `DELETE /routes/{id}`, 204/404, `DeleteRouteTests.cs` | Low — direct copy of the proven detail-endpoint scoping pattern |
| 2. Frontend delete UI | `ConfirmModal`, proxy `DELETE`, list + detail wiring | Removing the delete button from inside a `<Link>` row without also triggering navigation |

**Prerequisites:** `route-library` (S-03) merged — done, `main@1ba5215`.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- Assumes the list page's row-as-`<Link>` structure can cleanly host a nested interactive button via `preventDefault()`/`stopPropagation()` — standard pattern, but the exact row markup should be double-checked during Phase 2.
- `ConfirmModal` is designed reusable for `account-deletion` (S-06), but that slice isn't planned yet — its actual needs could diverge slightly from what's built here.

## Success Criteria (Summary)

- User can delete a saved route from either the list or its detail page, after confirming a prompt naming the route.
- Deletion is immediate, permanent, and correctly scoped (a user can never delete another user's route).
- No regression to save, list, detail-view, GPX download, or anonymous generation flows.
