---
change_id: loop-direction-hint
title: User-facing direction/destination hint for loop route generation
status: archived
created: 2026-08-04
updated: 2026-09-09
archived_at: 2026-09-08T22:25:20Z
---

## Notes

Spawned via `/10x-frame` out of `routing-quality-osm` manual verification (Wilanów,
80-100km: route headed NW at bearing 300.66° instead of south toward Góra Kalwaria).

**Outcome (2026-08-04)**: after a Step 5 pressure-test, the frame concluded no new
plan is warranted yet. Proximate cause confirmed (`SelectBestRoute` has no scenic
signal, so a merely-more-paved candidate beat the scenic one) — but the fix is
`routing-quality-osm` Phase 3 (scenic scoring, already planned, not yet built), not
a new direction-hint feature. Recommendation: resume `routing-quality-osm`, re-test
after Phase 3 lands, and only re-open this change if the south candidate still loses.
The direction-hint want itself remains genuine (user-confirmed) and is parked here
for future reference. See `frame.md` for full reasoning.
