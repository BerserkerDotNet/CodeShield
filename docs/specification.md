# Code quality evaluator: MVP

The evaluator provides deterministic code-quality feedback to an agent working in a Git repository. The initial languages are C#/.NET and TypeScript, with a design extensible to other languages. Business-requirement validation and LLM review are outside this MVP.

## 1. Contract

The agent calls the evaluation tool with a workspace and Git baseline:

```json
{
  "contractVersion": "1",
  "workspace": "C:\\Projects\\MyApp",
  "baseline": "HEAD",
  "ruleset": "default"
}
```

| Input | Meaning |
|---|---|
| `contractVersion` | Version of the request and response contract; initially `"1"`. |
| `workspace` | Location in the Git repository containing the code to evaluate. |
| `baseline` | Git commit or revision. The tool resolves it to a commit ID and returns that ID. |
| `ruleset` | Ruleset name and optional exact version, such as `default` or `default@<version>`. |

The response contains the contract version, resolved baseline commit, analyzed snapshot identity, status, score and threshold, category breakdown, findings with evidence and deductions, and analysis coverage. Checks that are inapplicable or could not run are identified with reasons. An unsupported contract version must produce an explicit error. The contract version is separate from the ruleset version.

Illustrative response shape; fields and names are proposed:

```json
{
  "contractVersion": "1",
  "status": "complete",
  "evaluationId": "<evaluation id>",
  "baselineCommit": "<resolved commit id>",
  "snapshotId": "<analyzed snapshot id>",
  "ruleset": "default",
  "rulesetVersion": "<version>",
  "analyzerVersions": { "metric-engine": "<version>" },
  "score": 88,
  "threshold": 90,
  "passed": false,
  "categories": [
    { "name": "complexity", "deduction": 12 }
  ],
  "findings": [
    {
      "ruleId": "complexity.cyclomatic",
      "definition": "Cyclomatic complexity is one plus the counted decisions in a method.",
      "intent": "Identify methods with control flow that may be difficult to understand and test.",
      "contextId": "<project and target framework>",
      "attribution": "introduced",
      "locations": [
        { "file": "src\\OrderProcessor.cs", "startLine": 12, "startColumn": 1, "endLine": 30, "endColumn": 2 }
      ],
      "actual": 8,
      "limit": 6,
      "deduction": 12,
      "evidence": [{ "kind": "decisionCount", "base": 1, "decisions": 7 }]
    }
  ],
  "coverage": [],
  "reasons": []
}
```

This abbreviated example omits the other category entries. A complete response must include category deductions and coverage details.

| Status | Score and pass | Meaning |
|---|---|---|
| `complete` | Score 0–100; pass is true or false | All applicable checks completed for the supported change; compare the score with 90. Unsupported files are ignored. |
| `incomplete` | No score or pass result | Required analysis failed or the snapshot became stale. Return available findings and reasons. |
| `no_changes` | No score or pass result | Git found no changes that the MVP can assess, such as a README-only or Python-only change. Return the reason; do not imply the change was analyzed. |
| `error` | No score or pass result | The request or evaluator failed before producing an assessment. |

Only a complete result can pass. Incomplete findings are useful evidence but do not produce a partial score.

Every response includes `status`, `score` and `passed`. For `incomplete`, `no_changes` and `error`, `score` and `passed` are `null`; for `complete`, they are an integer from 0 to 100 and a Boolean. Scores require no rounding because all deductions are whole points.

Failures and incomplete checks include a stable machine-readable reason code and a human-readable message identifying the affected check where applicable:

| Category | Initial codes |
|---|---|
| Request and Git | `INVALID_ARGUMENT`, `UNSUPPORTED_CONTRACT_VERSION`, `UNKNOWN_RULESET`, `INVALID_BASELINE`, `GIT_FAILURE` |
| Incomplete analysis | `MISSING_ANALYZER`, `MISSING_DEPENDENCY`, `PROJECT_LOAD_FAILED`, `UNRESOLVED_OWNERSHIP`, `SUPPRESSED_RULE`, `STALE_SNAPSHOT` |
| Detail lookup | `EVALUATION_NOT_FOUND`, `RULE_NOT_FOUND` |

The agent must not interpret an unknown reason code as success.

**Evaluation response contract v1:** all `evaluate_quality` responses include `contractVersion`, `status`, `score`, `passed`, `threshold`, `evaluationId`, `baselineCommit`, `snapshotId`, `ruleset`, `rulesetVersion`, `analyzerVersions`, `categories`, `findings`, `coverage`, and `reasons`. Use `null` for unavailable scalar values and empty arrays for empty collections; an error before an evaluation is created has a null `evaluationId`. `analyzerVersions` maps analyzer names to exact version strings. A category entry has `name` and total `deduction`; it is informational and has no separate budget.

Each finding has `ruleId`, `definition`, `intent`, `contextId`, `attribution` (`introduced`, `pre_existing`, or `ambiguous`), `deduction`, `locations`, `actual`, `limit`, and `evidence`. Use a zero deduction for context-only findings. A location has a repository-relative `file`, `startLine`, `startColumn`, `endLine`, and `endColumn`, with one-based lines and columns; graph findings can carry multiple locations or an empty list when no source location exists. `actual` and `limit` are typed JSON values appropriate to the rule or null; `evidence` is an array of objects with a `kind` and rule-specific structured fields. Detail responses include the same finding fields and full evidence, while evaluation findings can include summary evidence.

Each coverage entry has `ruleId`, `contextId`, `status`, and a `reasons` array. Each reason has `code`, `message`, and nullable `ruleId` and `contextId`. An analysis context is identified by its repository-relative project or configuration path plus target framework and configuration where applicable. Paths in responses use the native platform separator. Rule-specific evidence object fields and exact SDK versions are pinned with the ruleset at release; they must be included in the published schema and conformance fixtures.

Coverage contains one entry per rule and analysis context:

| Coverage status | Meaning |
|---|---|
| `complete` | The rule ran. |
| `not_applicable` | There was nothing for the rule to assess. |
| `failed` | The rule should have run but could not; include a reason code and message. |

Any required failed coverage entry makes the overall evaluation `incomplete`. A completed rule with no findings is different from one that could not run.

The default pass threshold is **90/100**. If the score is below 90, the agent improves the code and evaluates again, unless the user directs it to proceed. The evaluator itself does not change the code.

### Rule details

Each evaluator rule has its own stable ID, definition and intent. Rule semantics, thresholds and deductions belong to the versioned ruleset. `evaluate_quality` returns the ID, definition and intent alongside each finding, plus summary evidence and the evaluation ID.

Use readable IDs such as `complexity.cyclomatic`, `correctness.csharp.ca2000` and `correctness.typescript.no-floating-promises`. The evaluator ID identifies the rule; the separate ruleset version identifies its exact definition, options and deduction. Underlying analyzer IDs are retained as evidence, not used as the sole evaluator ID.

An agent can request more detail for **all findings of a failed rule in one evaluation**:

```json
{
  "contractVersion": "1",
  "evaluationId": "<evaluation id>",
  "ruleId": "complexity.cyclomatic"
}
```

The `get_rule_details` MCP tool returns the rule definition and intent, ruleset version, calculation method, threshold and deduction, and all matching findings with their individual calculations, source locations and evidence. It refers to the original evaluation snapshot, not the current workspace. A subsequent evaluation has a new evaluation ID.

Example detail response:

```json
{
  "contractVersion": "1",
  "status": "complete",
  "evaluationId": "eval-123",
  "ruleId": "complexity.cyclomatic",
  "rulesetVersion": "<version>",
  "definition": "One plus the counted decisions in a method.",
  "intent": "Identify difficult control flow.",
  "limit": 6,
  "deductionPerFinding": 12,
  "findings": [
    {
      "contextId": "<project and target framework>",
      "attribution": "introduced",
      "locations": [
        { "file": "src\\Orders.cs", "startLine": 42, "startColumn": 1, "endLine": 60, "endColumn": 2 }
      ],
      "actual": 8,
      "limit": 6,
      "deduction": 12,
      "calculation": "1 base + 7 decisions",
      "evidence": [
        { "kind": "decision", "decisionKind": "if", "line": 45 },
        { "kind": "decision", "decisionKind": "if", "line": 47 },
        { "kind": "decision", "decisionKind": "if", "line": 49 },
        { "kind": "decision", "decisionKind": "loop", "line": 51 },
        { "kind": "decision", "decisionKind": "if", "line": 53 },
        { "kind": "decision", "decisionKind": "if", "line": 55 },
        { "kind": "decision", "decisionKind": "if", "line": 57 }
      ]
    }
  ]
}
```

The detail response associates each finding with the rule metadata above it. Machine-readable evidence uses objects: complexity and nesting evidence contain decision kind and location; cohesion evidence contains nodes, edges and component memberships; duplication contains matched ranges and token/line counts; graph rules contain module or file nodes and dependency edges; distance contains type counts, dependency counts and unrounded values; correctness contains analyzer ID, diagnostic location and diagnostic message. Evidence must fully support the calculation. An error response uses `status: "error"` and includes a reason code and message.

Retain evaluation details in memory until the MCP server stops; do not persist them to disk. A server restart expires its evaluation IDs. An unknown or expired evaluation ID, or a rule that has no findings in that evaluation, returns an explicit error.

### Agent integration

Expose `evaluate_quality` and `get_rule_details` as local MCP tools over standard input/output. Their arguments and structured results follow the versioned contracts above; analysis stays local.

Configure the agent to start the MCP server separately from its instructions. The agent's auto-loaded instructions must also specify when to call the tool and how to act on results, as described in section 4. Merely registering the MCP server does not force the agent to call it.

**Recommended distribution:** ship a local .NET MCP host with the official MCP C# SDK, Roslyn-based C# analysis, and a bundled Node.js TypeScript analysis worker. Install both the host and the worker's pinned dependencies during setup, not on tool invocation. Provide a host-appropriate MCP registration example and auto-loaded agent instructions; do not silently modify a repository's configuration. The user explicitly installs/registers the tool. A missing runtime or dependency is an explicit error or incomplete check, not an automatic installation. Pin and test a compatible stable .NET SDK, Node.js runtime, Roslyn/.NET analyzer, TypeScript, ESLint, and typescript-eslint set in release lockfiles; publish those exact versions and options with the ruleset.

## 2. Metrics and scoring

The following is the **approved initial scoring rubric**, not yet empirically calibrated against representative code. No metric proves a design principle is satisfied, and 100 means no deductions were found by the checks performed, not that perfection is proven.

| Area | Rule ID | Check | Draft deduction |
|---|---|---|---:|
| Correctness | `correctness.<language>.<rule>` | New finding from a selected static correctness rule | 12 per distinct finding |
| Complexity | `complexity.cyclomatic` | Cyclomatic complexity above **6** | 12 per method |
| Complexity | `complexity.nesting` | Nesting deeper than **2** levels | 11 per method |
| Complexity | `complexity.method-length` | Method above **40 code lines** | 11 per method |
| Cohesion | `cohesion.lcom4` | Applicable class has **LCOM4 of 2 or more** | 12 per class |
| Coupling | `coupling.module-cycle` | Change introduces a module dependency cycle | 15 per new cycle group |
| Coupling | `coupling.typescript-file-cycle` | Change introduces an intra-package TypeScript file import cycle | 15 per new cycle group |
| Coupling | `coupling.boundary` | Change violates a configured architectural boundary | 15 per violation |
| Coupling | `coupling.main-sequence-distance` | Qualifying abstractness/instability distance | 12 per affected module |
| Structure | `structure.file-length` | New file exceeds **400 code lines**, or an existing file meets the rule below | 11 per file |
| Structure | `structure.duplication` | New or changed code duplicates a substantial block | 12 per distinct duplicated block |
| Readability | `readability.parameter-count` | Method has more than **5 parameters** | 5 per method |

Start with 100 points, subtract each violation's deduction, and floor the final score at zero. Categories organize findings; they do not have point budgets or separate floors. Each rule has a fixed, versioned cost per violation; it does not vary with the magnitude of an individual violation. When the same issue occurs in multiple analysis contexts, report and deduct it separately for each context. A check with nothing applicable produces no violation and no deduction. Unavailable analysis is different: report it as incomplete, not as a clean pass. Deduction values should reflect the violation's expected impact on long-term stability and maintainability and the cost of fixing it; the draft values still require calibration.

### Shared measurement rules

The evaluator owns a versioned definition for each metric. C# and TypeScript adapters implement those definitions, rather than blindly adopting tool-specific variants. Conformance examples must verify the detailed rules below before release.

An affected method is a new or modified executable function or method body, identified from the diff and the language syntax tree. A function nested inside another function is measured separately; decisions inside it do not contribute to the enclosing function's complexity or nesting. Constructors and accessors are executable methods for method metrics. Declaration-only methods have no body and are not scored. When mapping a deletion, consult both baseline and current syntax trees; if an entire method is deleted, it has no current method metric.

**Cyclomatic complexity:** begin at 1 for each body. Add 1 for each `if` (including `else if`), `for`, `foreach`/`for...of`/`for...in`, `while`, `do...while`, `catch`, conditional `?:`, and non-default `switch` case or non-default switch-expression arm. Add 1 for each short-circuit `&&`, `||`, `??`, and `??=` operator in that body. A `default` case and an unconditional discard switch arm add 0. Ordinary calls, plain blocks and an `else` add 0. A method with six counted decisions has complexity 7 and exceeds the limit of 6.

Count one per syntactic case label/arm even when a pattern contains alternatives. Count the listed short-circuit operators wherever they occur in the body, not only in conditions; nested functions have their own counts. Optional chaining, null-forgiving operators and ordinary calls add zero. Other operators, including logical compound assignments not listed above, add zero in ruleset v1. Analyze C# conditional compilation according to the active project/framework context. TypeScript overload declarations without bodies are excluded; their implementation body is counted once.

**Nesting:** the function body starts at depth 0. Entering the body of an `if`, loop, `switch`, or `catch`, or a branch of a conditional `?:`, adds one level. A switch case does not add another level beyond its switch. An `else if` is at the same depth as its corresponding `if`; a plain block, `try`, `finally`, or `else` does not independently add a level. The metric is the maximum depth reached. A third nested control-flow construct yields 3 and exceeds the limit of 2.

An `else` branch is at the same depth as its matching `if` branch, not one level deeper. For `do...while`, the loop body adds one level only. Analyze each lambda, local function or TypeScript nested function independently; the enclosing function does not count its nested control flow.

**Code lines:** count each physical line containing at least one source token other than a comment or whitespace. A line containing code and a comment counts once; blank and comment-only lines count zero. Count declaration and brace lines when they contain tokens. Count a method from its declaration through its body; nested function text counts toward the enclosing method's physical length as well as the nested function's own length.

**Parameters:** count declared parameters of the method, including optional and variadic parameters; exclude TypeScript's special compile-time `this` parameter. A method with six parameters exceeds the limit of 5.

Count a destructured TypeScript parameter as one parameter, not one per binding. For a method containing a nested function, count the two signatures separately.

**LCOM4:** for an applicable class, create a graph whose nodes are its concrete instance methods and instance property accessors with bodies, excluding constructors, destructors and empty bodies. Add an undirected edge when two nodes access the same declared instance field or auto-property, or when either directly calls the other. Resolve accesses and calls by symbol, not by text; inherited members and static members are excluded. LCOM4 is the number of connected components, including isolated nodes. A class with two disconnected method groups scores 2 and is flagged. A class without instance state or with fewer than two eligible nodes is inapplicable. Show the nodes, connections and groups as evidence. Constructors are excluded because they can connect otherwise unrelated groups merely by initializing fields.

If a new or modified applicable class exceeds the limit, deduct even if the cohesion issue predates the edit. The finding must distinguish existing and introduced evidence; it must not claim the edit created a pre-existing problem. This does not require automatically rewriting unrelated legacy classes.

For partial C# classes, combine declarations belonging to the same resolved symbol in a context. Treat C# auto-properties and TypeScript parameter properties as instance state. Explicit property getter/setter bodies are eligible nodes; resolve accesses to them as calls to their accessor nodes. Do not invent edges for shared static fields, inherited members, or code inside nested functions. Exclude generated files from scoring by default; project context can still depend on generated declarations, and missing required declarations make analysis incomplete.

**Duplication:** tokenize C# and TypeScript separately, retaining token kinds and exact token text and ignoring comments and whitespace. Compare contiguous token sequences from changed code with other code in the same language. A match qualifies only when each occurrence spans at least **10 code lines and 50 tokens**. A block can match existing code, but at least one occurrence must intersect new or changed code.

Group occurrences of the same qualifying token sequence. Group wholly contained or partially overlapping matches together: two matches are connected when any of their occurrence ranges overlap in the same file, and a group is a connected component of those matches. Deduct 12 points once per group, not per pair or overlapping match. Use the longest token sequence as the representative; break ties by repository-relative path and start offset in ordinal order. Report all occurrence locations and overlapping matches as evidence, identifying those involving changed code. Similar logic with different identifiers or literals is not an exact token match; semantic duplication is outside this rule. This grouping can combine distinct patterns when they overlap.

**File size:** apply the same code-line definition to the whole file. For a new file, flag a current count above 400. For an existing file, flag when the baseline was at or below 400 and the current file exceeds 400; also flag an already oversized file if the change adds a method. A small edit to an already oversized file does not by itself trigger this rule. Deduct at most once per file per analysis context for this rule.

### Dependency and module metrics

For the MVP, a **module** is a .NET project or a TypeScript package. A TypeScript source file belongs to the nearest ancestor directory within the repository containing `package.json`. Build a directed graph of repository modules: an edge from A to B means A depends on B. Include .NET project references and TypeScript imports between repository packages, including type-only imports. Exclude third-party packages. Multiple references from A to B count as one edge. An unresolved dependency makes the affected metric incomplete rather than counting as zero.

**Cycles:** find strongly connected components in the before and after module graphs. A component containing more than one module represents a cycle group. Also check TypeScript file-level import cycles within each package; exclude type-only imports from this file-level check. Package-level dependency metrics still include type-only imports.

Compare dependency edges before and after the change. A current cycle group is a finding when a newly added edge participates in a cycle, even if the group already contained a cycle. Deduct 15 once per affected current cycle group, not per added edge. A pre-existing group with no new cyclic edge is context only. Match file identities by unchanged path or Git's recognized rename map; identify a .NET module by its `.csproj` path and a TypeScript module by its package's `package.json` path using the same map. An unrecognized move is a deletion and addition. Where identity cannot be matched uniquely, classify affected edge attribution as ambiguous and count it as new while disclosing the uncertainty.

**Boundary violations:** compare resolved dependency edges with explicit module-to-module rules in optional `.quality-architecture.json` at the repository root:

```json
{
  "version": 1,
  "forbiddenDependencies": [
    {
      "from": "src\\Domain\\Domain.csproj",
      "to": "src\\Infrastructure\\Infrastructure.csproj"
    }
  ]
}
```

Paths identify modules relative to the repository root. Deduct 15 per newly introduced prohibited edge. Without the configuration file this check is inapplicable, not a passed check. Invalid configuration or an unresolved module makes the check incomplete; do not silently ignore the rule.

**Abstractness and instability:** for each affected module, calculate `A = abstract types / all eligible types`, `I = Ce / (Ca + Ce)`, and `D = abs(A + I - 1)`.

- Eligible C# types are classes, records, structs and interfaces; eligible TypeScript types are classes and interfaces. Abstract classes and interfaces count as abstract. Exclude enums, delegates and TypeScript type aliases. Count each resolved declared type once per module and analysis context; merge partial C# declarations. Count only types declared in source files owned by the module, not imported external types.
- `Ca` is the number of distinct repository modules with an edge into the measured module. `Ce` is the number of distinct repository modules that the measured module depends on. Count modules, not individual references.
- TypeScript type aliases and structural typing are not represented in abstractness; this limits cross-language comparability.

- Existing module: deduct 12 when its current `D` is **greater than 0.5** and increased by **at least 0.1** relative to the baseline.
- New module: deduct 12 when its current `D` is **greater than 0.5**.
- Compare unrounded values; display rounded values only for readability. Deduct at most once per affected module per analysis context for this rule.
- If `Ca + Ce` is zero, instability and distance are undefined. If eligible type count is zero, abstractness and distance are undefined. Report them as inapplicable, not as perfect values. An incomplete dependency graph is instead an analysis failure, not inapplicability.

### Static correctness

Use the following deterministic analyzer rules for detectable defects. A new attributable finding currently deducts 12 points; there is no separate category floor. One finding therefore reduces a complete score to at most 88/100. Compiler errors are excluded from the quality score. Business-requirement correctness is outside the MVP.

| Language | Rule | Detects |
|---|---|---|
| C# | `CA2000` | Undisposed resources |
| C# | `CA2011` | Recursive property setters |
| C# | `CA2012` | Incorrect `ValueTask` use |
| C# | `CA2013` | Value-type identity comparisons |
| C# | `CA2014` | `stackalloc` in loops |
| C# | `CA2016` | Dropped cancellation tokens |
| C# | `CA2017` | Logging argument mismatch |
| C# | `CA2018` | Incorrect byte counts in `Buffer.BlockCopy` |
| C# | `CA2019` | Thread-static initialization |
| C# | `CA2021` | Incompatible enumerable casts |
| C# | `CA2022` | Unchecked partial stream reads |
| C# | `CA2023` | Invalid log templates |
| TypeScript | `@typescript-eslint/no-floating-promises` | Unhandled promises |
| TypeScript | `@typescript-eslint/no-misused-promises` | Promises used in inappropriate contexts |
| TypeScript | `@typescript-eslint/await-thenable` | Awaiting non-thenables |
| TypeScript | `array-callback-return` | Missing array callback returns |
| TypeScript | `for-direction` | `for` loop updates moving in the wrong direction |
| TypeScript | `no-async-promise-executor` | Async promise executors |
| TypeScript | `no-constant-binary-expression` | Suspicious constant binary expressions |
| TypeScript | `no-dupe-else-if` | Duplicate conditions in an if/else-if chain |
| TypeScript | `no-self-assign` | Self-assignment |
| TypeScript | `no-unsafe-finally` | Control flow in `finally` that can mask errors |
| TypeScript | `no-unsafe-optional-chaining` | Unsafe use of optional chaining |
| TypeScript | `use-isnan` | Incorrect comparisons with `NaN` |

The rule catalog is based on [.NET reliability rules](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/reliability-warnings), [typescript-eslint](https://typescript-eslint.io/rules/), and [ESLint](https://eslint.org/docs/latest/rules/). The evaluator ships with analyzer versions and explicit rule options pinned by its ruleset, and reports the versions used. Changing an analyzer version or rule options requires a new ruleset version. It does not install or download analyzers during evaluation.

The evaluator uses its own versioned rule configuration and assesses required rules independently of repository suppressions. If a suppression prevents it from assessing a required rule, that check is incomplete rather than a clean pass. Pin exact versions and explicit options in the release ruleset, including default options expressed explicitly; use `ignoreVoid: false` as specified below. Missing required analyzers or type information make affected checks incomplete, not clean passes. These rules can produce false positives and are not comprehensive correctness coverage.

For these catalog entries, use `correctness.csharp.<lowercase-CA-ID>` for C# and `correctness.typescript.<rule-name>` for TypeScript, omitting the `@typescript-eslint/` namespace from the evaluator ID. Retain the full underlying analyzer ID in the finding.

Configure `@typescript-eslint/no-floating-promises` with `ignoreVoid: false`: `void` alone does not handle a rejection. Awaiting or returning a promise, or attaching a rejection handler, can satisfy the rule according to the pinned analyzer's behavior; the rule does not prove all asynchronous failures are handled.

Run the selected correctness rules against both snapshots for the same analysis context. First match findings with the same rule ID and corresponding code unit when the Git unchanged-line map places the baseline location at the current location. Match each baseline finding at most once. If a candidate maps to multiple findings, a changed line cannot be mapped uniquely, or code-unit identity cannot be established, classify it as ambiguous and deduct as new. A current finding in a demonstrably new code unit is introduced. Retain the analyzer diagnostic ID and message as evidence; do not match by message text alone. Matched findings are pre-existing and are not deducted. Report ambiguous attribution explicitly rather than claiming certainty.

### Calibration and conformance

The shared rules must have fixtures in both languages covering thresholds, exclusions, unchanged baseline issues, changed units, deletions and edge cases. Verify exact measurements, finding identities, deductions and result shape. The approved initial weights are not empirically calibrated until checked against representative examples; a passing score is not a guarantee of exceptional quality.

Acceptance tests exercise the real MCP server against temporary Git repositories with C# and TypeScript fixtures. They cover:

- Exact metric values and deductions at thresholds and edge cases.
- Staged, unstaged, untracked, renamed and deleted files.
- Project ownership and dependencies, including linked C# files and overlapping TypeScript configurations.
- A failing evaluation, a fix, and a passing re-evaluation against the same baseline.
- Details for multiple findings of one rule, and expired evaluation IDs.
- Missing analyzers, project-loading failures, stale snapshots and no-change results.

Unit tests fake Git operations; they do not depend on a real Git repository. Integration tests stage an isolated local Git repository in a test folder and create fixture commits, then exercise the actual Git commands and MCP server against it. They must not commit to or modify the user's repository. These real tool-contract and Git-flow tests complement focused metric tests.

Minimum conformance cases include: complexity 6/7; nesting 2/3; method lengths 40/41; file lengths 400/401 and an already oversized legacy file; five/six parameters; LCOM4 one/two components and stateless inapplicability; duplicate matches below and at both minimums, including overlaps; existing/new cyclic edges and type-only imports; distance changes below/at 0.1 and distances at/above 0.5; and every selected correctness diagnostic with a triggering and non-triggering example in its applicable language. Assert separate deductions per context, flooring at zero, score 90 passing, incomplete null scores, and the original evaluation's detail evidence after a subsequent code edit.

For calibration, use a separately reviewed set of clean, borderline and problematic examples in both languages and document false positives, false negatives and resulting scores. Do not claim that the rubric is empirically calibrated until those fixtures have actually been evaluated and reviewed.

## 3. Tool operation

### 3.1 Getting the scope

Resolve the repository root and the supplied baseline:

```text
git rev-parse --show-toplevel
git rev-parse --verify --end-of-options "<revision>^{commit}"
```

`HEAD` is an example; resolve the supplied revision once to a commit ID. Reject invalid baselines. Return the resolved ID so the agent can reuse it on subsequent calls.

Use the resolved commit ID as `<baseline>`:

```text
git diff --no-ext-diff --no-textconv --find-renames=50% --name-status -z <baseline> --
git diff --no-ext-diff --no-textconv --find-renames=50% --unified=0 <baseline> --
git ls-files --others --exclude-standard -z
```

- The first command supplies tracked-file status and rename information using NUL-delimited paths.
- The second supplies old/new line ranges for mapping to code. Deleted lines have baseline locations rather than current locations.
- The third lists untracked, non-ignored files; `git diff` alone does not include them. Treat their contents as additions.
- Handle paths with spaces, Unicode or newlines safely; do not split `-z` output on whitespace or construct shell commands by concatenating untrusted paths.
- Rename detection is heuristic. Keep both paths when a rename is detected; not all moves will be recognized.

For v1 use Git's `--find-renames=50%` consistently for status and hunk commands. Map a recognized rename's old path to its new path; a rename without changed hunks is not by itself a modified method. Treat an unrecognized move as a deletion plus an addition. For a modified file, map unchanged lines using the ordered Git hunks; lines inside replaced/deleted ranges have no unique automatic mapping. For an untracked non-ignored source file, every existing line is added. Detect Git failures and unsupported binary source diffs explicitly rather than treating them as empty changes.

Evaluate the current working tree against the baseline, rather than giving staged and unstaged changes separate scores. When staged and unstaged versions differ, evaluate the current file once, not the staged version. Reconcile overlapping deletion and untracked-file states without double-counting; if they cannot be reconciled, return an incomplete result. Report unreadable supported files rather than silently dropping them.

All qualifying changes since the baseline are included, including any uncommitted work that predated the agent. A Git commit does not identify who made an edit. The evaluator acts only when invoked; it does not monitor the workspace or ask whether it was already dirty. Ignored untracked files are excluded; tracked files remain in scope even when an ignore rule matches them.

The MVP assesses supported C# and TypeScript changes and relevant project or evaluator configuration. Ignore unsupported files, including in mixed-language changes; the score applies only to the supported scope. When no supported change can be assessed, return `no_changes` without a score.

#### Find the relevant projects and modules

The Git diff identifies changed paths; it does **not** identify which projects compile them. Discover and map project membership separately for both the baseline and current snapshots:

1. **Inventory project definitions.** From the repository root, enumerate baseline paths using `git ls-tree -r --name-only -z <baseline>` and current tracked and non-ignored untracked paths using `git ls-files --cached --others --exclude-standard -z`. Deduplicate paths and exclude files absent from the current snapshot. Find `.csproj`, `tsconfig.json` and other `tsconfig.*.json` files, and `package.json` files. Include project configuration files even if they are not themselves changed. Generated or ignored project definitions required for correct loading must be reported if unavailable.
2. **Resolve C# membership.** For each declared target framework in the Debug configuration, use evaluated MSBuild project information to determine which source files each `.csproj` compiles and its project references. Each project/framework combination is a separate analysis context. Do not assume a `.cs` file belongs only to the project in its parent directory: linked files and include/exclude rules can give it zero, one or several owners. Compare canonical paths and retain every owner. A `.csproj` is the module for module metrics; a multi-targeted project has more than one analysis context.
3. **Resolve TypeScript membership.** Parse each `tsconfig` using TypeScript's configuration and module-resolution rules, including `files`, `include`, `exclude`, `extends`, and project references. Exclude a configuration from standalone analysis when another configuration extends it and no TypeScript project reference points to it; it still contributes settings to the configurations that extend it. This is a heuristic: a directly built configuration that is also extended can be excluded. Map a changed source file to every remaining configuration that includes it, not just the nearest one; each is a separate analysis context. For module metrics, use the closest ancestor directory containing a `package.json` within the repository as its package root; a nested package therefore owns its own files. If none exists, report module ownership as unresolved. A `tsconfig` is an analysis context, not necessarily a package boundary.
4. **Select the affected contexts.** For added or modified source files, select their current owners. For deleted files, select their baseline owners; for renames or changed membership, consider both old and new paths and owners. If a changed project, imported build configuration, `tsconfig`, extended configuration or package definition changes membership or dependencies, select the affected contexts even when no source line changed.
5. **Expand for dependency analysis.** Load project references and package dependencies, plus source-level dependencies needed by the rule, to form the relevant before/after graph. This context can include unchanged projects or modules. Loading a dependency for context does not automatically make all its code part of the scored change.
6. **Report unresolved ownership.** A changed source file with no resolved owner, a missing configuration or dependency, an unsupported project type, or a failed project evaluation is not silently discarded. Report the affected checks as incomplete. A file with multiple owners retains all relevant contexts; equivalent findings in each context receive separate deductions.

The discovery commands list repository paths; they do not themselves evaluate project membership. Project evaluation may read locally available project imports and referenced dependencies outside the repository, such as SDKs and packages, but must not automatically download or restore them. Missing dependencies make the affected checks incomplete. External dependencies remain excluded from module-coupling counts.

An agent's tool call is treated as permission to evaluate the workspace; it is not proof of a separate host trust decision. MSBuild evaluation itself can execute project-supplied logic. The evaluator must not explicitly invoke project targets or restore packages automatically. If a baseline requires materializing files for project evaluation, use an isolated read-only analysis snapshot, not a checkout or modification of the user's working tree. If evaluation cannot be performed, report the affected analysis as incomplete.

### 3.2 Running analyzers

Map changed ranges to whole methods, classes, files and modules using language-aware syntax and symbols. Use the baseline version as well for deletions and before/after comparisons.

| Analyzer | Context needed |
|---|---|
| Method metrics | Whole affected methods. |
| Cohesion | Whole affected classes and member relationships. |
| Static correctness | Project and type information required by selected rules. |
| File length | Whole affected files and baseline versions. |
| Duplication | Changed code and potential matches elsewhere in the codebase. |
| Dependencies and module metrics | Relevant before/after dependency graphs and affected modules. |

An analyzer may need to load a full .NET project or TypeScript project to resolve symbols, and existing tools may run rules over more files than changed. For analyzers under our control, targeted analysis may be possible after project context is loaded; verify each analyzer's capabilities.

The evaluator operates locally and does not send source code to an LLM. It does not explicitly invoke project targets or restore dependencies automatically, and it does not intentionally modify the repository. An agent's call permits MSBuild project evaluation, which may execute project-supplied logic and therefore cannot be assumed side-effect-free. If sufficient context cannot be obtained, report the affected checks as incomplete.

### 3.3 Collecting results

Filter for **relevance to the change**, not solely for intersection with changed lines:

- Score whole new or modified methods and applicable affected classes, against the cleaner standard even when nearby legacy code does not meet it.
- Compare baseline and current diagnostics and architectural graphs to identify new issues and change-attributable effects.
- Report a duplicate only when at least one side involves new or changed code; show both locations.
- Separate useful pre-existing context from newly introduced findings. Score a cohesion issue in an affected class even if it predates the edit, while identifying it accurately.
- Include findings that point to unchanged lines when they arise from an affected unit or a new cross-file dependency.

Calculate a content-based snapshot ID from a sorted inventory of source files, relevant configurations and resolved references used for analysis, including external references. Use SHA-256 for each file's bytes. Encode each inventory entry as a scope tag (`repository` or `external`), canonical path and lowercase hexadecimal content hash. Encode each field in UTF-8 and prefix it with its byte length as an unsigned 64-bit big-endian integer. Sort entries by scope and path UTF-8 bytes in ordinal order, concatenate the encoded entries and SHA-256 that sequence. Use repository-relative canonical paths for repository files and canonical absolute paths for external files. Re-run change and input discovery and hash the resulting input set after analysis; an added, removed or changed input returns `incomplete` with `STALE_SNAPSHOT`. Record the resolved baseline commit, ruleset version and analyzer versions separately for auditability. This verifies enumerable analysis inputs, not arbitrary side effects of project-supplied MSBuild logic.

### 3.4 Reporting results

Calculate category and total scores using the versioned ruleset, and report findings with the code unit, location, measured value, threshold and deduction. Report the scope, baseline, analyzed snapshot, applicable checks and missing coverage.

On a subsequent call, use the **same resolved baseline commit** and the exact `ruleset@rulesetVersion` returned by the first call, and evaluate the current working tree again. A missing exact ruleset returns `UNKNOWN_RULESET` rather than silently switching versions. The result covers the full change since the baseline, not just the latest fix.

## 4. How the agent uses the tool

1. **After a coherent change:** call the evaluator before declaring the work ready. The evaluator is not called after every keystroke. Retain the resolved baseline commit and ruleset version from the result.
2. **Present every evaluation:** show the score, target, previous score when available, and concise information about findings. Show incomplete or stale analysis explicitly; it is not a pass.
3. **Improve and repeat:** if the complete score is below 90, address actionable findings with meaningful changes and evaluate again against the same baseline. The agent must not make superficial changes merely to improve the score.
4. **Stop when blocked:** if improvement would require a questionable refactor or expanding the requested scope, stop and ask the user whether to proceed or change scope.
5. **Finish or override:** a complete score of 90 or above passes when it reflects the current code. The user may direct the agent to proceed below the threshold or despite incomplete analysis; identify the override and any consequential unresolved findings in the final result. Normal task verification remains separate.

### Evaluation summary

Use a compact, readable summary after **each** evaluation. Example values are illustrative:

```text
☒  Code quality: 49 / 100
   Target: 90    Previous: 45

   Complexity    ProcessOrder exceeds 3 limits   −34
   Duplication   Matches existing code           −12
   Parameters    6; limit is 5                    −5
```

Use **☑** for a complete passing evaluation, **☒** for a complete evaluation below the target, and **☐** for incomplete or stale analysis. Show no score for incomplete results, errors, or no changes. Do not mark an overridden result as a pass. Omit the previous score on the first evaluation. Provide source locations and full finding details in the report; the summary is not a substitute for that evidence.
