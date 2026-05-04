---
name: 10x
description: Execute requested code changes faster while preserving quality gates: verify scope first, implement directly, validate high-impact paths, and finish with clean commit/push status. Use when the user asks for faster delivery, high-velocity execution, or "10x" mode.
disable-model-invocation: true
---

# 10x Execution Mode

## Purpose
Ship requested changes quickly with strong quality and clear completion evidence.

## Workflow
1. Restate the exact requested scope in one short sentence.
2. Verify current repo state before editing:
   - current branch
   - target commit/branch constraints
   - dirty files
3. Implement changes directly (avoid over-planning unless blocked).
4. Validate only the touched scope:
   - lint diagnostics for edited files
   - targeted runtime/build checks if available
5. Summarize outcomes as a checklist (done / not done / blocked).
6. If requested, commit and push with a message matching repo style.

## Quality Rules
- Do not claim completion without file-level verification.
- Prefer minimal, focused diffs over broad refactors.
- Preserve existing behavior outside requested scope.
- Surface blockers immediately with the next best action.

## Response Style
- Short progress updates while working.
- Final response includes:
  - what changed
  - what was validated
  - git commit hash and push result (if requested)
