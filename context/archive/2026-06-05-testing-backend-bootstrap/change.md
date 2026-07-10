---
change_id: testing-backend-bootstrap
title: Backend test bootstrap — critical ORS mapping and GPX locale coverage (Phase 1)
status: archived
created: 2026-06-05
updated: 2026-07-10
archived_at: 2026-07-10T18:15:47Z
---

## Notes

Phase 1 of context/foundation/test-plan.md: "Backend test bootstrap + critical coverage". Risks covered: #1 (ORS enum mapping drift), #3 (GPX locale/format failure). Test types planned: unit (xUnit). Risk response intent: - Risk #1: prove ORS response codes map to correct SurfaceType/RoadClass values for all known codes; the team has already been burned by this exact bug. Challenge "it rendered = parsing was correct." - Risk #3: prove GpxSerializer produces trk/trkseg/trkpt with InvariantCulture decimal formatting regardless of server locale. Challenge "works in dev = works in prod."
