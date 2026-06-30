---
id: security-privacy-guards
title: Security and privacy guards
status: done
created: 2026-06-20
updated: 2026-06-20
roadmap_ref: F-04
---

Integration tests confirming that (a) completed route-generation requests leave no input
coordinate values in backend logs, and (b) ORS HTTP error responses forwarded to the caller
contain no API key string. Covers test-plan Phase 3 (Risk #4 and Risk #6).
