---
id: gpx-export
title: GPX Export
status: impl_reviewed
created: 2026-06-04
updated: 2026-06-04
---

## Summary

Add a "Download GPX" button to the route info panel. GPX serialisation lives in the
.NET backend (`POST /routes/gpx`), proxied via Next.js, so any future client (mobile,
CLI) can reuse the same endpoint.

## Links

- Plan: `context/changes/gpx-export/plan.md`
- PRD requirement: FR-006 (GPX export), US-01 acceptance criteria
