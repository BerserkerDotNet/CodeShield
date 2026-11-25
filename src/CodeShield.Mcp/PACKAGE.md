# CodeShield.Tool

CodeShield is a deterministic code-quality evaluator for AI coding agents. This .NET tool provides the `codeshield` command, which runs a local MCP server (`codeshield mcp`) that scores C# and TypeScript changes against a Git baseline.

```
dotnet tool install --global CodeShield.Tool
codeshield doctor
```

Requires the .NET 10 SDK, Git, and Node.js 22.13 or newer in the 22.x line, or Node.js 24 or newer (TypeScript analysis only). Register the server through the CodeShield agent plugin for your client; see the project README.
