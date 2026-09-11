# Contributor Map — Git History

Follows `context/map/artifact-1-territory.md` and `artifact-2-structure.md`. Window: full repo history (2026-05-20 → 2026-09-10, ~4 months — shorter than the nominal 12-month window per artifact-1).

## Top 5 areas identified for potential contributor contact

Picked from artifact-1's hottest files/folders and artifact-2's structural hub findings — areas where a future change is most likely to need someone who knows the history:

1. **Backend routing core** — `src/backend/VeloRoute/Routing`, `VeloRoute.Tests/Routing` (27 + 30 file-touches, artifact-1; zero fan-in/fan-out cycles but `LoopRouteGenerator` is the highest-fan-out type and the test-plan's own highest-risk module, artifact-2).
2. **`Program.cs`** (backend composition root) — 14 commits (artifact-1's #2 hottest file); the seam every endpoint change passes through (12-commit co-change with `VeloRoute.Tests/Routing`, artifact-1; 3 composition-root-only types with hidden fan-in, artifact-2).
3. **Frontend `app` + `components`** — 44 + 28 file-touches (artifact-1's top two folders); `RouteApp.tsx` (fan-out 8) and `RouteMap.tsx` (fan-in 4, fan-out 5) are the structural hub nodes (artifact-2).
4. **`VeloRoute.Tests/Routing/TestInfrastructure.cs`** — shared test harness, 6-commit co-change with `Program.cs` (artifact-1's flagged coupling smell — touched on over half of `Program.cs`'s own commits).
5. **CI/deploy** — `.github/workflows/*`, `context/changes/{ci-deploy-hardening,backend-deploy}` (13 + 11 + 9 file-touches, artifact-1); operational rather than structural, but high-churn and easy to get wrong silently (secrets, migration steps).

## Contributors per area (last 12 months, bots/agents filtered)

**Filtering note**: no bot or automation commits found (`dependabot`, `github-actions[bot]`, etc. — zero hits). No agent-only commits either — every commit in the repo carries explicit human authorship (`Łukasz Orawiec`); `Co-Authored-By: Claude …` / `Co-authored-by: Copilot` appear only as trailers alongside that human authorship, not as the commit author, so nothing was excluded on that basis.

**Repo has exactly one contributor**, committing under three email identities (`58004501+lukasz05@users.noreply.github.com` — GitHub PR-merge commits; `Lukasz.Orawiec@suntech.pl` — early/pre-rename commits; `orawiec@mailbox.org` — CI/deploy and recent doc work). All three resolve to the same person (`Łukasz Orawiec`) — not three contributors, just three git configs across machines/contexts. There is no support line to route to beyond the author; the value here is in *what to ask about*, not *who to ask*.

### 1. Backend routing core — Łukasz Orawiec
- **Algorithm design & correctness**: `feat(loop-route-generation): backend foundation`, `loop route generation service + POST /routes/loop`, `feat: loop route algorithm quality tuning (#7)`, `feat(routing): improve loop route quality`.
- **External integration**: `feat(routing-api-wiring): ORS typed HTTP client and data contract (p1)`, `dev-only /routes/preview endpoint (p2)`.
- **Hardening**: `fix(routing-api-wiring): impl-review fixes (safety, reliability, scope)`, `fix(review): triage impl-review findings F1-F4, F6`.
- **Test coverage**: `test: backend test bootstrap — ORS mapping and GPX locale coverage (#4)`, `feat(F-03)/(F-04): route generation integration tests`.
- → Best person to ask about `LoopRouteGenerator` internals, ORS client contract, and why specific safety/reliability guards exist.

### 2. `Program.cs` (composition root) — Łukasz Orawiec
- **Endpoint additions, one per v2 feature slice**: F-01 (Clerk auth + JWT middleware), F-02 (data schema wiring), S-01 (magic-link auth) → S-08 (edit route), including save/library/delete/share/account-deletion.
- **Deploy-facing wiring**: `chore(backend-deploy): CI gate smoke test (#8)`, `feat(ci-deploy-hardening): fail-fast config checks (phase 2)`.
- **Rename/scaffolding**: `chore(project-rename): backend rename (p1)`, `Scaffold the backend and frontend projects`.
- → Best person to ask about endpoint registration order, DI wiring, and how a new feature slice should hook into the composition root.

### 3. Frontend `app` + `components` — Łukasz Orawiec
- **UI build-out per feature**: frontend UI for loop-route generation (p3/p4), auth UI (S-01), route library UI (S-02/S-03), sharing UI (S-05), account deletion (S-06/S-17).
- **Bug fixes**: `fix: SearchBar keyboard/reopen bugs + Warsaw default map center (#18)`.
- **Test tooling**: `feat(frontend): add Vitest + React Testing Library`.
- **Rename**: `chore(project-rename): frontend metadata rename (p2)`.
- → Best person to ask about `RouteApp.tsx`/`RouteMap.tsx` orchestration and why the map/component split is shaped the way it is.

### 4. `TestInfrastructure.cs` (shared test harness) — Łukasz Orawiec
- Touched incidentally on 8 feature commits (`feat(routing): improve loop route quality`, `S-01`, `S-04`, `S-06`, `F-01`, `F-02`, `F-04`, `#7`) rather than as its own deliberate refactor target — consistent with artifact-1's read that it's debt accumulating passively, not a maintained abstraction.
- → Best (only) person to ask before extending it; also the person who'd most benefit from the artifact-1-flagged stabilization work landing in M4L4.

### 5. CI/deploy — Łukasz Orawiec
- **Pipeline build-out**: `feat(backend-deploy): add backend CI/CD workflow (p2)`, `ci: add Azure Static Web Apps workflow file` (on-behalf-of `@Azure opensource@microsoft.com` — official GitHub Actions template attribution, not a second contributor).
- **Migration-in-CI hardening**: `feat(ci-deploy-hardening): migration-in-CI via Kudu (phase 1)`, five follow-up `fix(ci-deploy-hardening)` commits (Kudu VFS PUT, shell spawn, cookie persistence, base64 padding, secret scrubbing).
- **Smoke testing**: `ci(ci-deploy-hardening): post-deploy smoke tests + doc sync (phases 3-4)`, `chore(backend-deploy): end-to-end smoke test complete (p4)`.
- → Best person to ask about the Kudu migration step's fragility history — five targeted fixes in sequence signals this integration point is genuinely tricky, not just under-tested.

## Bottom line

Single-maintainer project — there is no support line beyond the author for any of these five areas. The practical value of this map for a new contributor or agent is knowing *which commit trail to read* before touching each area (linked above), not who to escalate to.
