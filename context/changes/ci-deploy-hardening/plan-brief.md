# CI/Deploy Hardening — Plan Brief

> Full plan: `context/changes/ci-deploy-hardening/plan.md`

## What & Why

On 2026-09-09, three separate misconfigurations shipped through consistently green CI: an empty frontend Clerk build-time key, missing backend Clerk App Service settings, and a pending EF migration (`AddShares`) that sat unapplied on production Postgres for six weeks. All three were fixed manually, live, during a debugging session. This plan closes the structural gap that let all three happen: CI proves only "builds and uploads," never "the deployed app actually works."

## Starting Point

`backend.yml` publishes and deploys the .NET binary with no migration step — `Database.Migrate()` only runs in `IsDevelopment()`. The SWA workflow now passes the Clerk key at build time (fixed today, PR #24) but validates nothing. Neither workflow authenticates to Azure beyond narrow deploy credentials — no service principal, no OIDC, and the repo owner cannot set either up. There's no IaC.

## Desired End State

A push to `main` automatically applies pending migrations to production before the new binary ships, a missing required config setting fails the app/build loudly instead of silently, and both deploy workflows verify the live deployment actually works after shipping — not just that it built.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Azure auth for CI | None — no OIDC/SP | Repo owner lacks the Azure AD permissions required to set either up | Plan (user constraint) |
| DB migration network path | Kudu command API, via existing publish-profile credential | Zero new secrets, zero new Azure AD setup, zero Postgres firewall exposure — GitHub's IP allowlist (6,980 CIDRs) turned out infeasible | Plan (research pivot) |
| Migrate vs deploy order | Migrate first, then deploy | New code never runs against a schema it doesn't expect | Plan |
| Migration gate | Fully automatic, no manual approval | Consistent with the rest of this already-unattended pipeline | Plan |
| Config-missing detection | Fail-fast at app/build startup, not a CI Azure query | Needs zero Azure permissions; turns a silent 401 into an immediate loud failure | Plan (research pivot) |
| Post-deploy smoke scope | Backend: health + authed round-trip. Frontend: 200 + non-empty key | Together these would have caught all 3 of today's bugs | Plan |
| Smoke test failure handling | Fail workflow, alert only — no auto-rollback | No existing rollback machinery in this repo; matches current all-manual model | Plan |
| EF model-drift check | Add `has-pending-model-changes` to the test job | Catches "forgot to write the migration" at PR time, closing the authoring-time version of today's bug | Plan |
| Migration secret format | Not needed — Kudu reuses `ConnectionStrings__Default` already on the App Service | No new secret required at all | Plan |
| Priority if time-constrained | Migration-in-CI + fail-fast checks = must-have; smoke tests = should-have; doc sync = nice-to-have | User's explicit ranking | Plan |

## Scope

**In scope:**
- Automatic migration application via Kudu, ordered before deploy, gated by a model-drift CI check
- Backend and frontend fail-fast checks for required Clerk configuration
- Post-deploy smoke tests on both workflows
- Backend README + root docs staleness sweep
- Removing the now-unnecessary stale Postgres firewall rule

**Out of scope:**
- OIDC/service-principal setup for Azure AD
- Auto-rollback on smoke-test failure
- Manual-approval gates for migrations
- Automatic additive-vs-destructive migration classification
- Fixing the discovered `pk_test_` (vs `pk_live_`) Clerk instance mismatch

## Architecture / Approach

The migration path avoids adding any new Azure trust boundary by reusing Kudu — the App Service's existing management API, already authenticated via the publish-profile credential the deploy step already holds. A migration bundle (`dotnet ef migrations bundle`) is built in CI, uploaded to the App Service via Kudu's file API, and executed via Kudu's command API — entirely inside Azure's existing network trust boundary, so Postgres's firewall needs zero new rules. Fail-fast config checks and post-deploy smoke tests are the two independent safety nets layered on top: one catches missing config before the app even serves traffic, the other proves the deployed artifact works end to end.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Migration-in-CI via Kudu | Pending migrations apply automatically, before deploy | Kudu command-execution behavior on this specific Linux App Service hasn't been exercised before — documented manual fallback exists |
| 2. Fail-Fast Config Checks | Missing Clerk config throws at startup/build instead of silently 401ing | `EF.IsDesignTime` guard must be correct or it breaks all `dotnet ef` tooling, including Phase 1's own CI step |
| 3. Post-Deploy Smoke Tests | Both workflows verify the live deployment after shipping | Needs a dedicated Clerk `+clerk_test` smoke user; exact Clerk Frontend API endpoints need verification against current docs at implementation time |
| 4. Doc Sync + Cleanup | Docs describe the new pipeline accurately; stale firewall rule removed | Low risk — documentation and cleanup only |

**Prerequisites:** Phases 1 and 2 are independent and can ship in either order. Phase 3 benefits from Phase 2 already being in place. Phase 4 should be last (documents the final state).
**Estimated effort:** ~2-4 sessions across 4 phases — Phase 1 (Kudu mechanics) and Phase 3 (Clerk API mechanics) carry the real implementation uncertainty; Phases 2 and 4 are comparatively mechanical.

## Open Risks & Assumptions

- Kudu's `/api/command` behavior on this Linux App Service SKU hasn't been validated end-to-end — Phase 1 includes an explicit manual-verification pause before trusting it as the sole path, with the session's manual runbook as documented fallback.
- Clerk's Frontend API sign-in endpoint paths for Phase 3 are described from memory at a reasonable confidence level but should be verified against current Clerk docs during implementation — not a plan-blocking unknown, but a named implementation-time verification step.
- The production Clerk instance running on a `pk_test_` key (not `pk_live_`) is a discovered fact this plan deliberately does not address — worth a separate conversation.

## Success Criteria (Summary)

- A backend-only push to `main` applies any pending migration automatically and the app never 500s on a missing table again
- A missing required Clerk setting is caught at deploy time (fail-fast or smoke test), never silently live for weeks
- No repo doc describes migrations as manual-only after this change lands
