---
change_id: save-route
title: Save route (S-02)
status: planned
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
