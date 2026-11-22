# Code quality (CodeShield)

Copy this file to `<your-repository>/.github/copilot-instructions.md` (or append it to the existing file). It is opt-in: nothing in CodeShield setup writes to your repository.

When you finish a coherent change (not after every edit) and before you say the work is ready:

1. Call the `evaluate_quality` MCP tool with `contractVersion: "1"`, the absolute workspace path and a baseline (the commit the user names, otherwise `HEAD`). The first response's `baselineCommit` and `ruleset@rulesetVersion` are the task's identity: retain them and pass both on every later call for this task so scores stay comparable. A missing exact ruleset returns `UNKNOWN_RULESET`; do not switch versions.
2. Present **every** evaluation to the user with the card below. Use each finding's own `deduction` and `attribution`: a `pre_existing` finding in a unit you changed can still carry a deduction, a matched finding with deduction 0 is context only, and `ambiguous` attribution is deducted as new and the uncertainty is disclosed. Do not assume that pre-existing means free.
3. If the status is `complete` and the score is below 90, fix actionable findings with meaningful changes and evaluate again against the same baseline. Use `get_rule_details` (same server session only) for evidence. Never make superficial edits, edit thresholds, suppress rules or change the ruleset to raise the score.
4. If improvement needs a questionable refactor or wider scope, stop and ask the user.
5. A complete score of 90 or above passes when it reflects the current code. The user may direct you to proceed below the threshold or despite incomplete analysis; label that as an override, never as a pass, and list the unresolved findings.

Statuses are different outcomes; never merge them:

- `complete`: score and pass are present.
- `incomplete`: some checks could not run (missing dependency or analyzer, project load failure, unreadable input, stale snapshot). Score and pass are null. Report the reasons and coverage; retry a `STALE_SNAPSHOT` once files stop changing; run `dotnet restore` / install dependencies for `MISSING_DEPENDENCY`.
- `error`: the request failed (invalid baseline, unsupported contract version, Git failure). Fix the request or ask the user.
- `no_changes`: no supported source or configuration changed in scope. Report it; do not invent a score.

Scope: the evaluator covers staged, unstaged and untracked supported files against the baseline. If the baseline is not what the user expects, or scope looks wrong, ask instead of guessing.

Summary card after each evaluation (☑ complete and passing, ☒ complete but below target, ☐ incomplete, stale, error or no changes; show no score for ☐; omit "Previous" on the first evaluation):

```text
☒  Code quality: 49 / 100
   Target: 90    Previous: 45

   Complexity    ProcessOrder exceeds 3 limits   −34
   Duplication   Matches existing code           −12
   Parameters    6; limit is 5                    −5
```

Each line names the failed metric, the measured value against its limit (the target) and the deduction. Give source locations and full details in the report; the card is not a substitute.
