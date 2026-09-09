---
change_id: anonymous-flow-e2e
title: Core anonymous flow end-to-end (test-plan phase 5, risk #7)
status: implemented
created: 2026-09-09
updated: 2026-09-09
archived_at: null
---

## Notes

Implements `context/foundation/test-plan.md` §3 phase 5, defending risk #7 (High × High).
Two halves: a Playwright e2e for the signed-out generate → map → GPX flow, and the
missing `POST /routes/gpx` integration test.

See `research.md` for grounding. Four findings that change the phase's shape:

- `POST /routes/gpx` has **two** validation branches, not the three the test-plan claims.
- The Clerk key needs no CI secret — `pk_test_Y2xlcmsuZXhhbXBsZS5jb20k` passes validation offline.
- The load-bearing e2e case is "clerk-js blocked", not "download works" — the download is
  already auth-free; the *rendering* of `RouteInfoPanel` is what auth can break.
- Start point can only be set via `/api/geocode`; `RouteMap` has no click handler.

Planning added three findings research did not have (see `plan.md` → Key Discoveries):

- `clerkMiddleware()` also hard-requires **`CLERK_SECRET_KEY`** — without it every request
  500s. A synthetic `sk_test_…` suffices. Research solved only the publishable key.
- The app **does** survive clerk-js being blocked — settled from the Clerk SDK source, so
  the e2e is deterministic and no bug was found.
- The non-finite branch is only reachable via `1e400` or quoted `"NaN"`; a bare `NaN`
  literal fails at model binding before the guard runs.
