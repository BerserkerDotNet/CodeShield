# CodeShield

A deterministic code-quality evaluator for AI coding agents, exposed as a local MCP server (stdio). It scores the changes in a Git working tree against a baseline commit (C# via Roslyn, TypeScript via a bundled Node worker). No LLM is involved. The full contract is in [`docs/specification.md`](docs/specification.md).

Tools: `evaluate_quality` (contract version `"1"`, pass threshold 90) and `get_rule_details` (details are held in memory only, so they expire when the server restarts).

## Requirements

To launch the host and the TypeScript worker:

- .NET runtime 10 (the installed build is framework-dependent) and Node.js 22+
- Git on `PATH`

To build from source: .NET SDK 10.0.400 (see `global.json`) and npm.

For full C# evaluation of a repository you also need what that repository's projects need to load: a .NET SDK able to evaluate them, their reference/targeting packs (for example `FrameworkReference` packs and `netstandard` references) and restored NuGet packages. Evaluation never restores or builds; missing pieces make the affected checks `incomplete` with `MISSING_DEPENDENCY` or `PROJECT_LOAD_FAILED`.

## Setup

```powershell
scripts\setup.ps1        # or: sh scripts/setup.sh
```

This runs `npm ci` and `npm run build` in `worker/`, then publishes a self-contained layout to `artifacts/codeshield`: the host, the pinned Roslyn analyzers, `worker/` (compiled worker plus production `node_modules` from the lockfile), `schemas/` and `release-manifest.json`. The folder can be copied anywhere; it needs only the .NET runtime, Node.js 22+ and Git. The host finds `worker/dist/main.js` by walking up from its own directory; override with `CODESHIELD_TS_WORKER` and `CODESHIELD_NODE`.

C# analysis never restores packages: run `dotnet restore` in the analysed repository first, otherwise affected checks are reported as incomplete.

## Contract artifacts

- JSON schemas for both tools and the structured evidence: [`docs/schemas`](docs/schemas).
- Exact ruleset (`default@1.0.0`), per-rule limits and deductions, selected correctness rules, analyzer versions/options and runtime set: [`docs/release-manifest.json`](docs/release-manifest.json) (a test keeps it identical to the compiled ruleset).
- Calibration is **unreviewed**: limits and deductions are fixed by the specification. Deterministic conformance fixtures verify the implementation; they are not empirical calibration.

## Register the MCP server

Point `command` at the installed build; replace `<install-dir>` with where you copied or built it (for example `artifacts/codeshield`).

```json
{
  "mcpServers": {
    "codeshield": {
      "command": "dotnet",
      "args": ["<install-dir>/codeshield.dll"]
    }
  }
}
```

## Agent instructions

A ready-to-copy, opt-in instruction file is shipped in [`docs/agent-instructions/copilot-instructions.md`](docs/agent-instructions/copilot-instructions.md) (and in `<install-dir>/agent-instructions/`). Copy it to `<your-repository>/.github/copilot-instructions.md`, or append it to the existing file. Setup never modifies your repositories or global instructions. It covers when to evaluate, retaining `baselineCommit` and `ruleset@version`, reading each finding's own `deduction` and `attribution` (a `pre_existing` finding in a changed unit can still be deducted), the distinct `complete` / `incomplete` / `error` / `no_changes` outcomes, overrides, and the ☑/☒/☐ summary card (failed metric, measured value against limit, deduction, target and previous score).

Condensed form for other agents' always-loaded instructions:

```markdown
After finishing a change, call `evaluate_quality` with the absolute workspace path, contractVersion "1",
and a baseline (the user's, otherwise `HEAD`). Retain `baselineCommit` and the exact
`ruleset@rulesetVersion` from the first response and pass both on later evaluations of the same task.
Present every result. Use each finding's `deduction` and `attribution` independently; pre_existing
findings in changed units may still be deducted, ambiguous ones are deducted as new. Treat `complete`,
`incomplete`, `error` and `no_changes` as different outcomes: only `complete` has a score. Never edit
thresholds, suppress rules or change the ruleset to pass. Follow section 4 of docs/specification.md.
```

An optional `.quality-architecture.json` at the repository root declares forbidden module dependencies; this repository's own file is an example.

## Development

```powershell
dotnet build CodeShield.slnx
dotnet test CodeShield.slnx          # unit + real-MCP integration tests (temporary Git fixture repos)
cd worker; npm test
```

Unit tests fake Git. Integration tests create isolated temporary Git repositories and drive the real server over stdio, including both scenarios in `docs/acceptance-vectors.json`.

## Known limitations of this MVP

- Calibration is unreviewed: deterministic conformance fixtures verify the contract, they are not empirical calibration of the thresholds.
- Method-metric findings are labelled `pre_existing`, `introduced` or `ambiguous` by matching units in the rename-mapped baseline file (Git's own rename detection; no custom rename heuristics). A unique match is `pre_existing`/`introduced` by metric comparison; an unmatched or duplicated identity is `ambiguous` and charged. Changed units are always charged.
- Graph facts are kept per context: one per target framework for C# (`<project>|<tfm>`) and one per owning tsconfig for TypeScript. Cycle and boundary findings are charged once per affected context; disjoint contexts contribute only supporting edges. For main-sequence distance, afferent coupling counts distinct dependents across all contexts and efferent coupling counts the context's own dependencies.
- External inputs are re-discovered after analysis (a second analysis pass), so an input that newly becomes resolvable yields `STALE_SNAPSHOT` with null score/pass and retained evidence. The cost is a second analysis pass per evaluation.
- Duplication detection has no bucket cap (verified with 70 identical files).
- The baseline is exported blob-by-blob from the commit (`export-ignore`/`export-subst` have no effect) and sealed read-only; evaluation never restores or builds. Projects must be restored beforehand, otherwise the result is `incomplete` with `MISSING_DEPENDENCY`. Real restored NuGet, `FrameworkReference` and `netstandard2.1` baselines are covered by integration tests (these tests need NuGet access or a warm cache for setup only). Only the net10.0 targeting pack and `netstandard2.1` reference pack were available on the development machine, so other frameworks were not exercised.
- JavaScript files are not scored. `.tsx/.mts/.cts` sources and `allowJs` configurations are covered by integration tests.
- Dogfood result (this repository analysed as all-new code, with `.quality-architecture.json`): status `complete`, score 64/100, so the 90 gate is **not met**. The only findings are three `coupling.main-sequence-distance` hits (D = 0.97, 0.67, 0.69) on the stable, concrete `Abstractions`, `CSharp` and `Core` modules (abstractness 0.03, 0.00, 0.06). All complexity, nesting, length, LCOM4, cycle, boundary, duplication and correctness checks are clean. The findings were not suppressed or gamed; whether to introduce abstractions or accept an override is a design decision left to the maintainer.
