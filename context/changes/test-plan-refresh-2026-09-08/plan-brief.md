# Test Plan Refresh (2026-09-08) — Plan Brief

> Full plan: `context/changes/test-plan-refresh-2026-09-08/plan.md`

## What & Why

`context/foundation/test-plan.md` was written 2026-06-20 against a v1-only project. Every v2 slice shipped after it — auth, save, library, delete, share, account deletion, edit. All eight sections are now stale: the risk map covers only v1 routing concerns while the majority of 101 backend test methods defend v2 behaviour that appears nowhere in it, the hot-spot window covers 8 commits, and a CI gate marked "not started" has been half-live since 2026-07-01.

## Starting Point

The document is accurate about the three shipped rollout phases and its own strategy principles — those held up. Everything factual around them drifted. Separately, the frontend has 47 Vitest cases across 8 files that gate nothing: the Azure SWA workflow deploys without running them.

## Desired End State

A test plan that describes the project as it is on 2026-09-08 — ten risks spanning v1 routing and v2 accounts, 90-day hot-spot data, phase statuses that match CI and the archive, a stack section listing every installed runner, and a frontend cookbook entry. And a CI pipeline where a failing frontend test blocks the deploy.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| Scope | Doc refresh + phase 4 CI gate | Otherwise a freshly-refreshed doc ships with phase 4 already stale on day one | Plan |
| Risk 4/6 (logs vs API key) | Split back into two rows | Each needs a different oracle, layer, and anti-pattern; both already covered, so splitting is free | Plan |
| Risk numbering | IDs 1–6 frozen, new appended 7–10 | §3's shipped rows cite risk IDs; renumbering would falsify that history | Plan |
| §4 e2e layer | Playwright named, unpinned | A pinned version for an uninstalled dep is the staleness class this refresh removes | Plan |
| §6 cookbook | Frontend entry now, e2e TBD | The gap is real today — frontend tests have shipped since July with no documented pattern | Plan |
| Node version in CI | `lts/*` | Repo pins none anywhere; a CI-only pin would drift with nothing to sync against | Plan |
| Rate-limit abuse risk | Dropped to §7 | PRD defers app-level throttling; a test would require building the safeguard first | Handoff |
| Share-token guessability | Reframed as lifecycle | Guessability needs generation internals — research territory; observable form is "revoked token returns 404" | Handoff |

## Scope

**In scope:** rewrite of all eight sections of `test-plan.md`; a `test` job in the SWA workflow gating deploy; the `AGENTS.md` CI sentence.

**Out of scope:** implementing rollout phases 5 or 6 (own change folders); installing Playwright; the `vitest.config.ts` exclude (a phase-5 prerequisite, documented not applied); new tests for risks 9 and 10, which shipped covered.

## Architecture / Approach

Phase 1 touches one markdown file and nothing else. Phase 2 touches CI plus the one doc it makes stale. The alternative split — risk map separately from mechanical sections — was rejected because §3's phase rows cite risk IDs, so a half-updated document is internally inconsistent between commits.

The CI job mirrors `backend.yml`'s existing test→deploy structure. Note that the SWA workflow has no Node toolchain at all (the deploy action builds internally via Oryx), so this is a new job, not an inserted step — the "two-line edit" the handoff assumed does not exist.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Refresh test-plan.md | All eight sections match the 2026-09-08 state | Risk-ID drift retroactively falsifying shipped phase rows |
| 2. Frontend CI gate | `npm test` blocks SWA deploy; phase 4 closes | `close_pull_request_job` wrongly inheriting `needs: test`, blocking PR closure |

**Prerequisites:** none — every fact needed was verified against the repo during planning.
**Estimated effort:** ~1 session. Phase 1 is the bulk; phase 2 is one job plus a PR round-trip to verify.

## Open Risks & Assumptions

- Backend count recorded as **101 `[Fact]`/`[Theory]` methods**, not the handoff's "104 tests". Theories expand per `[InlineData]` row (35 present), so the executed case count is higher; a real figure needs a `dotnet test` run, which needs Docker.
- Phase 2's manual verification requires a throwaway PR with a deliberately failing test. If that is unwelcome on `main`'s PR history, the gate ships unproven until the next real frontend PR fails.
- The 2026-09-14 deadline means phase 5 (core anonymous flow) is the priority if only one rollout phase lands after this. This change deliberately does not consume that budget.

## Success Criteria (Summary)

- Every stale fact identified during planning is corrected, and each is re-derivable from a command recorded in the plan.
- A frontend PR with a failing test produces no preview deployment.
- Phases 5 and 6 are scoped well enough that `/10x-new` can open either without another interview.
