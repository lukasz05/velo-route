---
id: route-generation-integration-tests
title: Route generation integration tests
status: impl_reviewed
created: 2026-06-20
updated: 2026-06-20
reviewed: 2026-06-20
roadmap_ref: F-03
---

Integration tests verifying that `LoopRouteGenerator` produces routes within [min_km, max_km]
and with ≤ 10% overlap (Risk #2), and that the 4.5 s ORS deadline fires correctly under
slow-response conditions (Risk #5). Covers test-plan Phase 2.
