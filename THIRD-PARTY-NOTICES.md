# Third-party notices

CodeShield itself is MIT licensed (see `LICENSE`). The distributed tool redistributes the components below. Each component's
own license and attribution notices are included unchanged in the `licenses` folder of the installed tool, grouped by NuGet
package id; Node dependencies carry their own `LICENSE` files inside `worker/node_modules/<package>/`.

## Managed (.NET) components

| Component | License |
| --- | --- |
| Microsoft.CodeAnalysis.Common / .CSharp (Roslyn) | MIT (.NET Foundation and Contributors) |
| Microsoft.CodeAnalysis.NetAnalyzers (analyzer assemblies in `analyzers/`) | MIT (Microsoft; Compiler Platform / .NET Foundation notices in `licenses/Microsoft.CodeAnalysis.NetAnalyzers/ThirdPartyNotices.txt`) |
| Microsoft.Build.Locator | MIT (Microsoft) |
| Microsoft.Extensions.* and System.Diagnostics.EventLog | MIT (Microsoft) |
| Microsoft.Extensions.AI.Abstractions | MIT (Microsoft) |
| ModelContextProtocol, ModelContextProtocol.Core | Apache-2.0 (Model Context Protocol a Series of LF Projects, LLC); full text in `licenses/Apache-2.0.txt` |

The upstream packages' `ThirdPartyNotices` / `THIRD-PARTY-NOTICES` files, which include the MIT permission and attribution
terms required for redistribution, are copied to `licenses/<PackageId>/`.

## TypeScript worker (Node.js)

The worker ships production dependencies locked by `worker/package-lock.json` (ESLint, TypeScript, typescript-eslint and their
transitive packages). Their license files are retained in `worker/node_modules/`.

## Kotlin worker (JVM)

`kotlin-worker/dist/codeshield-kotlin-worker.jar` contains only CodeShield's own MIT-licensed classes. The worker does not
bundle or redistribute detekt, the Kotlin compiler, the Kotlin standard library or any other dependency. They are loaded at
run time from the detekt-cli installation the user provides (detekt and Kotlin are Apache-2.0 licensed), and the Java runtime
is the user's own.

The source repository contains the Gradle wrapper (`kotlin-worker/gradlew`, `gradlew.bat` and
`gradle/wrapper/gradle-wrapper.jar`; Apache-2.0, Gradle Inc.). It is used only to build the worker jar and is not part of the
distributed tool.
