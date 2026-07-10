---
id: loop-algorithm-tuning
title: Loop route algorithm quality tuning
status: archived
created: 2026-06-20
updated: 2026-07-10
reviewed: 2026-06-21
archived_at: 2026-07-10T18:15:47Z
roadmap_ref: S-03
---

Improve generated loop route quality: implement paved-surface preference (using
existing ORS segment data), expose paved ratio in the API and RouteInfoPanel,
tune waypoint geometry, and lock acceptance thresholds with automated tests.
Covers PRD Business Logic ("segments on unpaved surfaces deprioritised") and
roadmap north star ("routes that feel like real road cycling loops").
