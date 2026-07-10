---
id: gpx-export
title: GPX Export
status: archived
created: 2026-06-04
updated: 2026-07-10
archived_at: 2026-07-10T18:15:47Z
---

## Summary

Add a "Download GPX" button to the route info panel. GPX serialisation lives in the
.NET backend (`POST /routes/gpx`), proxied via Next.js, so any future client (mobile,
CLI) can reuse the same endpoint.

## Links

- Plan: `context/changes/gpx-export/plan.md`
- PRD requirement: FR-006 (GPX export), US-01 acceptance criteria
