# CodeShield

A deterministic code-quality evaluator for AI coding agents, exposed as a local MCP server (stdio). It scores the changes in a Git working tree against a baseline commit (C# via Roslyn, TypeScript via a bundled Node worker). No LLM is involved. The full contract is in [`docs/specification.md`](docs/specification.md).

Tools: `evaluate_quality` (contract version `"1"`, pass threshold 90) and `get_rule_details` (details are held in memory only, so they expire when the server restarts).

## Quickstart

1. **Install the tool** (needs the .NET 10 SDK, Git, and Node.js 22.13+ in the 22.x line or 24+ for TypeScript). `CodeShield.Tool` is **not yet published** to NuGet, so install it from a local pack for now:

   ```powershell
   dotnet pack src/CodeShield.Mcp -c Release -o artifacts/feed
   dotnet tool install --global CodeShield.Tool --add-source artifacts/feed
   codeshield doctor
   ```

   Once the package is published, this becomes `dotnet tool install --global CodeShield.Tool`.

2. **Install the plugin** in your agent. The plugin registers the MCP server and adds the `quality-check` skill; your own MCP and instruction files are never edited.

   | Client | How |
   | --- | --- |
   | GitHub Copilot app | Open **Customize**, add the plugin marketplace `BerserkerDotNet/CodeShield`, and install `codeshield` (not verified in the GUI here) |
   | GitHub Copilot CLI | `copilot plugin marketplace add BerserkerDotNet/CodeShield` then `copilot plugin install codeshield@codeshield` |
   | Claude Code | `claude plugin marketplace add BerserkerDotNet/CodeShield` then `claude plugin install codeshield@codeshield` |
   | VS Code | **Chat: Open Customizations**, then install the plugin from this repository (marketplace or source). Not verified against a running VS Code here |

3. **Ask your agent:** "Check my changes with CodeShield."

Update or remove with the client's own commands (`copilot plugin update|uninstall codeshield`, `claude plugin update|uninstall codeshield@codeshield`) and `dotnet tool update|uninstall --global CodeShield.Tool`.

Run `codeshield doctor` first when something is off: it checks the packaged analyzers, the TypeScript worker and its locked dependencies, Git, and the Node.js version. A missing Node.js only affects TypeScript analysis. C# analysis never restores packages: run `dotnet restore` in the analysed repository first, otherwise affected checks are `incomplete`.

## Requirements

To run the host: .NET runtime 10, Git on `PATH`, and Node.js 22.13+ (22.x line) or 24+ for TypeScript analysis (the ESLint engine ranges exclude 22.0-22.12 and 23). To build from source: .NET SDK 10.0.100 or newer in the 10.0 feature bands (`global.json` uses `rollForward: latestFeature`) and npm.

For full C# evaluation of a repository you also need what that repository's projects need to load: a .NET SDK able to evaluate them, their reference/targeting packs (for example `FrameworkReference` packs and `netstandard` references) and restored NuGet packages. Evaluation never restores or builds; missing pieces make the affected checks `incomplete` with `MISSING_DEPENDENCY` or `PROJECT_LOAD_FAILED`.

## Build from source

```powershell
scripts\setup.ps1        # or: sh scripts/setup.sh
```

Both the portable folder and the .NET tool are produced by the same payload target in `src/CodeShield.Mcp`, which runs `npm ci` and `npm run build` in `worker/`, installs production `node_modules` from the lockfile, and stages `schemas/`, `agent-instructions/` and `release-manifest.json`. `setup` publishes to `artifacts/codeshield`; `dotnet pack` produces the tool package. The host finds `worker/dist/main.js` next to itself; override with `CODESHIELD_TS_WORKER` and `CODESHIELD_NODE`. `scripts/verify-install.ps1` packs, installs the tool into a temporary directory and runs the installed-tool tests from an unrelated working directory.

## Contract artifacts

- JSON schemas for both tools and the structured evidence: [`docs/schemas`](docs/schemas).
- Exact ruleset (`default@1.0.0`), per-rule limits and deductions, selected correctness rules, analyzer versions/options and runtime set: [`docs/release-manifest.json`](docs/release-manifest.json) (a test keeps it identical to the compiled ruleset).
- Calibration is **unreviewed**: limits and deductions are fixed by the specification. Deterministic conformance fixtures verify the implementation; they are not empirical calibration.

## Manual MCP registration

Only needed without the plugin. Use the tool command, or point at a portable build:

```json
{
  "mcpServers": {
    "codeshield": { "command": "codeshield", "args": ["mcp"] }
  }
}
```

For a portable build use `"command": "dotnet", "args": ["<install-dir>/codeshield.dll"]`.

## Agent instructions

The plugin's `quality-check` skill carries the same guidance as the opt-in file [`docs/agent-instructions/copilot-instructions.md`](docs/agent-instructions/copilot-instructions.md) (and in `<install-dir>/agent-instructions/`). Copy it to `<your-repository>/.github/copilot-instructions.md`, or append it to the existing file. Setup never modifies your repositories or global instructions. It covers when to evaluate, retaining `baselineCommit` and `ruleset@version`, reading each finding's own `deduction` and `attribution` (a `pre_existing` finding in a changed unit can still be deducted), the distinct `complete` / `incomplete` / `error` / `no_changes` outcomes, overrides, and the ☑/☒/☐ summary card (failed metric, measured value against limit, deduction, target and previous score).

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

## Configuration (`.codeshield.json`)

Cascading `.codeshield.json` files tune limits or exclude code from charging. The root file is global; a file in a folder applies to its subtree and the nearest folder wins, setting by setting. Schema: [`docs/schemas/codeshield-config.v1.schema.json`](docs/schemas/codeshield-config.v1.schema.json).

```jsonc
{
  "version": 1,
  "exclude": false,
  "rules": {
    "complexity.cyclomatic": { "limit": 10, "reason": "legacy parser" },
    "coupling.main-sequence-distance": { "enabled": false, "reason": "stable shared contract kernel" }
  }
}
```

- `reason` is required for `exclude: true`, `enabled: false` and any limit looser than the default. Unknown properties and rule IDs are rejected (`INVALID_CONFIGURATION`, no score).
- Configurable limits: cyclomatic, nesting, method length, parameter count, `cohesion.lcom4`, file length (integers up to 2147483647) and main-sequence distance (0–1). Duplication thresholds, the pass score and deductions are fixed.
- Exclusions affect charging only: excluded code is still analyzed and stays in the graph, Ca/Ce and the duplicate corpus. Affected coverage reads `excluded`.
- Responses disclose a `configuration` block (files with change status, exclusions, overrides, excluded-finding counts). Configs are part of the snapshot identity; a config-only change returns `no_changes`.
- Agents must not add or edit configs to raise a score without explicit user direction. See section 3.5 of the specification.

## Development

```powershell
dotnet build CodeShield.slnx
dotnet test CodeShield.slnx          # unit + real-MCP integration tests (temporary Git fixture repos)
cd worker; npm test
```

Unit tests fake Git. Integration tests create isolated temporary Git repositories and drive the real server over stdio, including both scenarios in `docs/acceptance-vectors.json`.

## Releases, CI and license

- **CI** (`.github/workflows/package.yml`) runs on pushes to `main`, pull requests and manual dispatch. On Windows, macOS and Linux (x64 and ARM64) it builds, runs the unit, worker and integration suites, then packs the tool, installs it into a temporary tool path and runs the installed-tool tests from an unrelated directory.
- **Releasing** is incremental and tag-driven: update `CHANGELOG.md` (move items from *Unreleased* into a dated `## [X.Y.Z]` section), set the same version in `src/CodeShield.Mcp/CodeShield.Mcp.csproj` and both `integrations/codeshield` plugin manifests, then push the tag `vX.Y.Z`. `scripts/check-release.ps1` enforces that all of these agree; the release workflow then re-runs the full matrix and attaches the package, `SHA256SUMS.txt` and the changelog notes to a GitHub Release. The product version is independent of the ruleset version (`default@1.0.0`) and the contract version (`"1"`).
- **NuGet** publishing is intentionally not configured; `CodeShield.Tool` stays unpublished until its identity and credentials are explicitly set up.
- **License**: MIT, see [`LICENSE`](LICENSE). Redistributed third-party components and their notices are listed in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) and shipped inside the tool package under `licenses/`.

## Known limitations of this MVP

- Calibration is unreviewed: deterministic conformance fixtures verify the contract, they are not empirical calibration of the thresholds.
- Method-metric findings are labelled `pre_existing`, `introduced` or `ambiguous` by matching units in the rename-mapped baseline file (Git's own rename detection; no custom rename heuristics). A unique match is `pre_existing`/`introduced` by metric comparison; an unmatched or duplicated identity is `ambiguous` and charged. Changed units are always charged.
- Graph facts are kept per context: one per target framework for C# (`<project>|<tfm>`) and one per owning tsconfig for TypeScript. Cycle and boundary findings are charged once per affected context; disjoint contexts contribute only supporting edges. For main-sequence distance, afferent coupling counts distinct dependents across all contexts and efferent coupling counts the context's own dependencies.
- External inputs are re-discovered after analysis (a second analysis pass), so an input that newly becomes resolvable yields `STALE_SNAPSHOT` with null score/pass and retained evidence. The cost is a second analysis pass per evaluation.
- Duplication detection has no bucket cap (verified with 70 identical files).
- The baseline is exported blob-by-blob from the commit (`export-ignore`/`export-subst` have no effect) and sealed read-only; evaluation never restores or builds. Projects must be restored beforehand, otherwise the result is `incomplete` with `MISSING_DEPENDENCY`. Real restored NuGet, `FrameworkReference` and `netstandard2.1` baselines are covered by integration tests (these tests need NuGet access or a warm cache for setup only). Only the net10.0 targeting pack and `netstandard2.1` reference pack were available on the development machine, so other frameworks were not exercised.
- JavaScript files are not scored. `.tsx/.mts/.cts` sources and `allowJs` configurations are covered by integration tests.
- Dogfood result (this repository, measured against a fixed baseline): with default rules the status is `complete` with score 88/100 and one `coupling.main-sequence-distance` finding on the stable `Abstractions` contract module, so the 90 gate is **not met**. With the maintainer-approved `src/CodeShield.Abstractions/.codeshield.json` (disables that rule, with a stated reason) the policy-based score is 100/100. The exemption is disclosed in the response's `configuration` block; the default-rules score is the unadjusted figure.
