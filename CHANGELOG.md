# Changelog

All notable changes to CodeShield are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the product follows [Semantic Versioning](https://semver.org/). The product version (this file, `CodeShield.Tool`, the plugin
manifests) is independent of the analysis ruleset version (`default@1.0.0`) and the MCP contract version (`"1"`), which change
only when scoring or the wire contract change.

## [Unreleased]

Nothing has been released yet. The work below is the first set of capabilities intended for version 0.1.0.

### Added

- Local MCP server (stdio) exposing `evaluate_quality` and `get_rule_details`: deterministic scoring of Git-scoped C# (Roslyn) and TypeScript (bundled Node worker) changes against a baseline.
- Cascading `.codeshield.json` configuration: per-rule limits, rule disabling and project exclusion with a required reason, a `configuration` disclosure block and the published JSON schema.
- `CodeShield.Tool` .NET tool (`codeshield mcp`, `doctor`, `--version`, `--help`) and a shared agent plugin for GitHub Copilot, Claude Code and VS Code.
- MIT license and third-party notices shipped inside the package.
- CI that builds, tests and installs the tool on Windows, macOS and Linux (x64 and ARM64), and a tag-driven release workflow.
