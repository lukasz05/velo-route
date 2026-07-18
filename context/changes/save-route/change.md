---
change_id: save-route
title: Save route (S-02)
status: implemented
created: 2026-07-18
updated: 2026-07-18
archived_at: null
---

## Notes

Roadmap slice S-02. Prerequisite S-01 (magic-link-auth) done, F-02
(data-layer-schema) done — the `Routes` table (UserId FK cascade-delete,
Name, Tags text[], DistanceKm, Geometry jsonb, CreatedAt) already exists and
is schema-tested (`UserRouteSchemaTests.cs`). This slice adds the write path
(save endpoint + UI); it does not add a library/list view — that's S-03,
still blocked on this slice.

PRD's "editable name/tags before or after saving" is satisfied here via
*before*-save inline editing only, since there is no library page yet for
*after*-save editing.

## Known Limitations

**Signed-out users see no Save UI at all (deviates from the original plan's
"clicks Save → Clerk sign-in modal" design).** During Phase 2 manual
verification, S-01's already-documented known limitation (cross-tab
magic-link completion requires a manual page reload — `useUser()` doesn't
reactively pick up the session; see
`context/archive/2026-07-15-magic-link-auth/change.md`) surfaced concretely
here: a signed-out user who opened the Save modal, signed in via magic link,
and returned to the original tab still needed a refresh before Save worked
— and refreshing loses the in-memory generated route. Rather than
re-attempt a fix S-01 already tried twice and abandoned, this slice hides
the entire name/tags/Save section for signed-out users (Download GPX stays
visible). Revisit if/when the underlying Clerk reactivity gap is fixed.
