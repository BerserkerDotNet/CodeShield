# CodeShield.Tool

CodeShield is a deterministic code-quality evaluator for AI coding agents. This .NET tool provides the `codeshield` command, which runs a local MCP server (`codeshield mcp`) that scores C#, TypeScript and Kotlin changes against a Git baseline.

```
dotnet tool install --global CodeShield.Tool
codeshield doctor
```

Requires the .NET 10 SDK and Git. TypeScript analysis also needs Node.js 22.13 or newer in the 22.x line, or Node.js 24 or newer. Kotlin analysis (Android, Kotlin Multiplatform and JVM backends built with Gradle) also needs a Java 17+ runtime and detekt-cli 1.23.x. Register the server through the CodeShield agent plugin for your client; see the project README.
