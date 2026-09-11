---
change_id: refactor-opportunities
title: Rank and plan fixes for debt found in loop-route-generation-analysis
status: plan_reviewed
created: 2026-09-11
updated: 2026-09-11
archived_at: null
---

## Notes

Intent: we have an analysis of this repository that documents technical debt
and structural risks: context/changes/loop-route-generation-analysis/research.md.
This change answers the question that analysis deliberately left open:
WHICH of these problems are worth fixing, in what target shape,
and in what order.
We explore every recorded problem in the code and history,
then organize them as refactor opportunities.
The change proceeds in stages: exploration → decision and plan → implementation.
No refactoring happens during exploration,
and no decision is made.
Exploration output: this change's research.md,
ending with a ranking of options with trade-offs.
I read the report first; the decision on what to implement is made
at the planning stage, and refactoring starts only from the accepted plan.
