---
change_id: route-library
title: Route library
status: archived
created: 2026-07-18
updated: 2026-07-18
archived_at: 2026-07-18T19:55:14Z
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

- Out-of-scope fix riding on this branch: commit `8487e2e` fixes an S-02 (save-route) bug — `RouteInfoPanel` kept `isSaved`/name/tags state across route regeneration, leaving the save form disabled until refresh. Unrelated to route-library's contracts; landed here instead of its own change folder. Flagged by impl-review F2.
