---
change_id: routing-quality-osm
title: Routing quality — OSM scenic/low-traffic preference + cyclist POI proximity
status: abandoned
created: 2026-07-26
updated: 2026-08-05
archived_at: null
---

## Notes

Abandoned 2026-08-05 after Phases 1-4 were implemented and committed, then reverted.
Reason: the public `overpass-api.de` instance (and mirrors `overpass.kumi.systems`,
`overpass.openstreetmap.fr` at the time, `overpass.private.coffee`, `maps.mail.ru`)
proved unreliable during manual verification — repeated 504s / connection stalls under
real usage, not a one-off. Only one mirror (`overpass.openstreetmap.fr`) responded
during a live check. The PRD's "OSM-only, Overpass API" data-source constraint
(FR-010/FR-011) makes this a hard dependency for both mechanisms in this plan
(POI-directed bearing nudging, scenic/low-traffic way-tag scoring), so unreliable
Overpass access undermines the whole slice rather than one best-effort corner of it.

Code reverted via `git revert` of the 4 phase commits (2925574, 5209b39, af5647c,
b6101a0) on `routing-quality-osm` — clean, no conflicts; `dotnet test` (99 passed, 3
skipped live-smoke) and `npx tsc --noEmit` both green post-revert. This plan.md and
research.md are kept as-is for reference if route-quality work resumes with a
different data-source strategy — see `context/foundation/roadmap.md` S-07 for the
reopened options (ORS `extra_info`-based road-class scoring instead of OSM tags is
the leading alternative floated during this abandonment).
