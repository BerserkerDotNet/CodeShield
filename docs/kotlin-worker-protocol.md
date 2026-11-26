# Kotlin worker protocol (v1)

The Kotlin adapter (`src/CodeShield.Kotlin`) runs a JVM worker once per request. It writes one JSON document to stdin and reads one JSON document from stdout. The JSON shapes are the same as for the [TypeScript worker](worker-protocol.md) and mirror the C# records in `src/CodeShield.Abstractions`, with camelCase property names. **All paths are repository-relative with forward slashes** (`app/src/main/kotlin/App.kt`).

The worker (`kotlin-worker/`) contains only CodeShield code. It uses the Kotlin compiler's PSI parser and the detekt rules from the **user's detekt-cli 1.23.x installation**, which is put on the class path at run time. The worker never runs Gradle, Gradle plugins, build logic or the Kotlin compiler back end, and never resolves types.

## Launch

```text
<java> -Xss16m -XX:+UseSerialGC -Djava.awt.headless=true -cp <worker.jar><path-separator><detekt jars> codeshield.kotlin.Main
```

Each item is resolved as follows:

| Item | Resolution order |
|---|---|
| `<java>` | `CODESHIELD_JAVA`, then `JAVA_HOME/bin/java`, then `java` on `PATH`. It must be Java 17 or newer. |
| `<detekt jars>` | `CODESHIELD_DETEKT` (a `detekt-cli-*-all.jar` or an installation directory), then a `detekt-cli` or `detekt` launcher on `PATH`. Symbolic links and Scoop `.shim` files are followed. The jars are read from the installation's `lib`, `libexec/lib` or `libexec` directory. The jar manifest's `DetektVersion` must match `1.23.x`. |
| `<worker.jar>` | `CODESHIELD_KOTLIN_WORKER`, otherwise `kotlin-worker/dist/codeshield-kotlin-worker.jar` beside the installed tool. |

### Output and exit codes

`Main` redirects `System.out` to stderr while the request runs, so detekt or compiler output cannot corrupt the response; stdout carries only the JSON response. A leading UTF-8 byte order mark in the request is ignored.

| Exit | Meaning | Adapter result |
|---|---|---|
| 0 | Success, even when facts contain failures. | The response is used. |
| 1 | Crash or malformed request; the stack trace is on stderr. | The language analysis fails. |
| 3 | `LinkageError`: the detekt-cli on the class path is not API-compatible. stderr reads "The Kotlin worker could not use the detekt-cli installation (...). detekt-cli 1.23.x is required; install it or set CODESHIELD_DETEKT." | `MISSING_ANALYZER` with that message. |

When the JVM is older than 17 it fails to load the worker class (`UnsupportedClassVersionError`). The adapter reports `MISSING_ANALYZER`: "The Kotlin worker needs a Java runtime (JDK or JRE) 17 or newer; '<java>' is older. Set CODESHIELD_JAVA or JAVA_HOME."

When Java or a compatible detekt-cli cannot be found, the adapter does not start the worker. It reports Kotlin analysis as `MISSING_ANALYZER`; C# and TypeScript analysis are unaffected.

## Requests

Every request has `"protocol": 1` and a `command`.

```json
{
  "protocol": 1,
  "command": "analyze",
  "root": "C:\\abs\\snapshot\\root",
  "files": ["settings.gradle.kts", "app/build.gradle.kts", "app/src/main/kotlin/App.kt"],
  "focusFiles": ["app/src/main/kotlin/App.kt"],
  "changedConfigFiles": [],
  "isBaseline": false,
  "correctnessRules": [
    { "analyzerId": "InvalidRange", "evaluatorId": "correctness.kotlin.invalid-range", "options": {} }
  ]
}
```

- `files` is every repository file in this snapshot: tracked files plus untracked, non-ignored ones. The worker never walks the disk for the inventory.
- `focusFiles` lists the changed `.kt` files, expressed in this snapshot's paths.
- `changedConfigFiles` lists the changed Gradle scripts (`*.gradle`, `*.gradle.kts`) in this snapshot.
- `correctnessRules` comes from `CorrectnessCatalog`, the single source of truth. `options` is a detekt rule configuration object.

A `tokenize` request carries `root` and `files`, the source files to tokenize. A `version` request carries nothing else.

## version response

```json
{ "detekt": "1.23.8", "java": "21.0.11", "kotlin": "2.0.21" }
```

- `detekt` is the `DetektVersion` attribute of the detekt-cli jar manifest.
- `kotlin` is the version of the Kotlin compiler bundled in that detekt-cli, which decides which syntax can be parsed.
- `java` is the running JVM's `java.version`.

`codeshield doctor` uses this response as a handshake to check that the worker can load the installed detekt-cli.

## analyze response (`LanguageAnalysis`)

```json
{
  "language": "kotlin",
  "analyzerVersions": { "detekt": "1.23.8", "java": "21.0.11", "kotlin": "2.0.21" },
  "contexts": [ ContextFacts ],
  "graph": ModuleGraphFacts,
  "inputs": [ { "scope": "repository", "path": "app/build.gradle.kts", "sha256": "..." } ],
  "unownedFiles": [ "focus .kt files with no owning Gradle project" ],
  "failures": [ Reason ]
}
```

- `inputs` lists every file the analysis read: Kotlin sources, settings scripts, build scripts and applied scripts. Each is hashed over the bytes actually used. Files outside the snapshot root have `scope: "external"` and a canonical absolute path.
- `failures` holds `UNREADABLE_SOURCE` for files that could not be read, with `contextId` set to the file's path.

## Gradle model

The worker reads Gradle Kotlin DSL scripts with the Kotlin PSI parser and Groovy DSL scripts with a small built-in parser. It never executes them. **Every occurrence counts**: a statement inside an `if`, loop or closure is read as though it always runs. A non-literal argument is reported, never guessed.

### Builds

- **Discovering builds.** Every directory with a settings script is a build; `settings.gradle` is preferred over `settings.gradle.kts` in the same directory. `includeBuild` targets are also builds, as is a `buildSrc` directory with only a build script.
- **Single-project builds.** A build script outside every build is a single-project build.
- **Settings statements.** The worker reads these from settings scripts:
  - `include` (a nested `"a:b"` declares both `:a` and `:a:b`);
  - `includeFlat`;
  - `includeBuild`;
  - `project(":x").projectDir = ...`;
  - `project(":x").buildFileName = ...`. A `.gradle` name also matches its `.gradle.kts` form.
  - `apply(from = ...)` / `apply from: ...`.
- **Build-script statements.** The worker reads these from build scripts:
  - project dependencies from configurations whose names neither start with `test` nor contain `Test`, as `project(":x")` or type-safe `projects.x.y` accessors (camelCase of `-` and `_` names);
  - `sourceSets.<set>.kotlin.srcDir(...)` and `.java.srcDir(...)` (also `srcDirs`, `setSrcDirs`);
  - `apply(from = ...)`. Remote URLs are skipped.
- **Cross-project blocks.** `subprojects`, `allprojects`, `childProjects` and `configure(...)` blocks apply to projects that cannot be determined statically. Their statements are read, but a project dependency inside them cannot be attributed to a single project and is reported. An applied script inside them is reported too, unless its path is anchored at the root directory.
- **Path expressions.** Paths are evaluated from these forms:
  - string literals, including `"$rootDir/x"`;
  - `file(...)` and `rootProject.file(...)`;
  - `File(dir, "x")` and `files(...)`;
  - `layout.projectDirectory.dir(...)`;
  - `listOf`/`setOf`;
  - `projectDir`, `rootDir`, `buildDir` and `settingsDir`.
- **Projects.** Projects whose directories are outside the repository are ignored. A project's `moduleId` is its build-script path, or its directory (`.` for the root project) when it has no script.

### Ownership

A `.kt` file belongs to the project whose source directory contains it. A source directory is the conventional `<projectDir>/src/<set>/kotlin` or `<projectDir>/src/<set>/java`, or a declared `srcDir`.

- **Choosing the owner.** The deepest matching directory wins. Ties go to the ordinally smallest `moduleId`, then to `main`.
- **Main and test.** A source set whose name starts with `test` or contains `Test` (`test`, `androidTest`, `testDebug`, `commonTest`, `jvmTest`) is a **test** source set; every other set (`main`, `debug`, flavors, `commonMain`, `androidMain`, `jvmMain`) is **main**.
- **Generated output.** The .NET adapter excludes paths with a `build/` or `.gradle/` segment before any `src` segment.

### ContextFacts

Each project has up to two contexts, `<contextPrefix>|main` and `<contextPrefix>|test`. `contextPrefix` is the project's build-script path, or `<settings>|<projectPath>` when the project has no build script. Examples: `app/build.gradle.kts|main` and `settings.gradle.kts|:core|test`.

**When contexts exist and are selected:**

- The main context of a project with a build script always exists.
- A test context, or the main context of a script-less project, exists only when it owns files.
- A context is returned when it owns a focus file, or when a file in its project's script chain is in `changedConfigFiles`. The chain is the settings script, the build script and the scripts they apply.

**Fields:**

- `language`: `kotlin`. `framework`: null.
- `configPath`: the build script, else the settings script, else the `moduleId`.
- `modulePath`: the build script for `main`. It is null for `test` contexts and for projects without a build script, so the coupling and main-sequence checks are not applied to them.
- `ownedFiles`: every `.kt` file the context owns.
- `files`: a `FileFact` for each owned **focus** file only.
  - `codeLines` counts physical lines with at least one token that is not a comment or whitespace.
  - `generated` is true when the first 10 lines contain `@generated` or `<auto-generated>` (case-insensitive).
- `classes`: a `ClassFact` (LCOM4) for every class in focus files.
- `types`: a `TypeFact` for every type in every owned file.
- `diagnostics`: detekt findings for every owned file, not only focus files.
- `failures`:
  - The project's Gradle model problems.
  - `UNREADABLE_SOURCE` with one of these messages: "Cannot read X.", "Cannot parse X: syntax error at line L, column C." or "Cannot parse X: the file is nested too deeply."
  - Correctness failures (see below).

### Module graph (`graph`)

- `modules` is every project with a build script, plus the modules of context owners, plus every module linked by an edge.
- `edges` links distinct projects of the same build, from a non-test project dependency. `typeOnly` is always false.
- `contexts` holds one graph, `main`, with the same edges. `fileImports` is empty: Kotlin module metrics use project dependencies, not source imports.
- `unresolved` holds `MISSING_DEPENDENCY` reasons, with `contextId` set to the declaring script:
  - "Cannot resolve project ':x' referenced by <file>:<line>."
  - "Cannot resolve project accessor 'projects.x' referenced by <file>:<line>."
  - "Cannot resolve the project referenced by <file>:<line>; only literal project paths are supported."
  - "Cannot attribute the project dependency in <file>:<line> to a single project; …", for a dependency in a cross-project block.
  - An applied script that cannot be resolved.

A problem that prevents reading a project's build is `PROJECT_LOAD_FAILED` in that project's context `failures`, and also in `unresolved`. Examples are an unreadable or unparsable script, and a non-literal `include`, `projectDir` or `buildFileName` ("Cannot evaluate … in <file>:<line>; only literal values are supported.").

## Measurement rules

These must match [the specification](specification.md#shared-measurement-rules), including its Kotlin mapping.

- **Units.**
  - Every `KtDeclarationWithBody` with a body is a unit: named functions (block or expression body), secondary constructors, property accessors, lambdas and anonymous functions. So is every `init` block.
  - A primary constructor has no body, so it is not a unit.
  - Nested functions, lambdas and classes are measured as their own units. Their decisions and nesting do not count toward the enclosing unit, but their text counts toward its `codeLines`.
- **Complexity.** The count starts at 1 and adds 1 for each of these:
  - `if` (`else if` included);
  - `for`, `while` and `do...while` (decision kind `loop`);
  - `catch`;
  - each non-`else` `when` entry (`case`);
  - each `&&`, `||` and `?:` (`logical`).
  - `decisions` evidence is `{ "decisionKind": "if|loop|catch|case|logical", "line": N }`.
- **Nesting.**
  - The body is at depth 0. `if`, loops, `when` and `catch` each add one level.
  - `when` entries add nothing beyond their `when`.
  - `else if` is at the same depth as its `if`. `try`, `finally` and plain blocks add nothing.
  - `nestingPoints` evidence is `{ "constructKind": "if|loop|when|catch", "line": N, "depth": D }`.
- **`codeLines`** counts from the start of the declaration (annotations and modifiers included) through the end of its body.
- **`parameterCount`** counts the declared value parameters. The extension receiver and a lambda's implicit `it` are excluded.
- **`unitId`** has the form `<file>#<Qualified.name(Param,Types)>`:
  - member functions: `Class.method(Int,String)`;
  - extension functions: `Receiver.name(...)`;
  - secondary constructors: `Class.constructor(...)`;
  - accessors: `Class.prop.get` and `Class.prop.set`;
  - `init` blocks: `Class.<init>`.
  - Units inside a function and anonymous units (`<lambda>`, `<anonymous>`, `<object>`) end in `@<line>`. A lambda assigned directly to a property takes the property's name.
  - A repeated qualified name ends in `:<column>`.
  - Positional IDs are never used as a diagnostic's owning unit.
- **LCOM4.**
  - Applies to classes. Interfaces, enums, annotation classes and objects are not applicable.
  - State: class-body properties with a backing field or delegate, and primary-constructor `val`/`var` parameters. Companion members are excluded.
  - Nodes: member functions and custom accessors with non-empty bodies.
  - Edges, resolved syntactically by name within the class:
    - `call`: a call to a member function whose arity fits, or a read or write that runs a custom accessor;
    - `field:<name>`: two nodes touching the same state, star-connected. A default accessor and `field` touch the accessor's own property's backing field.
  - Locals and parameters shadow members unless `this.` is explicit.
  - Code inside lambdas, nested functions and nested classes is excluded.
- **Types (`TypeFact`).**
  - Counted: classes, interfaces and named objects. Abstract means interfaces and abstract or sealed classes.
  - Not counted: enums, annotation classes, companion objects and object expressions.
  - An `expect` declaration and its `actual` counterparts count once.
- **Ordering.** Every array is sorted deterministically: by path, then line, column and ID, using ordinal comparison.

## Correctness

Each requested rule is created by class name from the installed detekt-cli. The worker tries the `io.gitlab.arturbosch.detekt.rules.bugs` package (rule set `potential-bugs`), then `io.gitlab.arturbosch.detekt.rules.exceptions`, using the rule's `(Config)` constructor. The rule runs on the parsed files **without type resolution**.

- **Configuration.**
  - The rule's configuration is the request's `options`, with `active: true` and `autoCorrect: false` forced.
  - `excludes`, `includes` and severity are absent, so the rule sees every owned file.
  - The repository's `detekt.yml`, baselines and Gradle `detekt { }` settings are ignored.
- **Findings.** A finding becomes a `DiagnosticFact`:
  - `analyzerId` is the detekt rule ID;
  - `unitId` is the enclosing stable method or class (empty when there is none).
- **Missing rule.** A rule class that does not exist gives `MISSING_ANALYZER` "Analyzer X is not installed." with the evaluator ID.
- **Rule errors.** A rule that throws gives `PROJECT_LOAD_FAILED` "detekt could not analyze <configPath>: <rule> failed on <file>: <error>".
- **Suppressions.** A `@Suppress` or `@SuppressWarnings` annotation anywhere in an owned file that names the rule gives `SUPPRESSED_RULE` for that rule and context: "<file>:<line> disables <Rule>, so the rule cannot be assessed." The rule can be named by:
  - its ID or an alias;
  - its rule set, as `ruleset.Id` or `ruleset:Id`;
  - `all`, `All` or `ALL`;
  - any of these with a `detekt:` or `detekt.` prefix.

## tokenize response (`TokenizeResult`)

```json
{ "files": [ { "path": "...", "tokens": [ { "kind": "kotlin:IDENTIFIER", "text": "x", "line": 1, "column": 1, "endLine": 1, "endColumn": 2, "offset": 0 } ] } ], "failures": [] }
```

- Tokens come from the Kotlin lexer. `kind` is `kotlin:<IElementType>`.
- Comments and whitespace are dropped. Exact text is retained.
- Lines and columns are 1-based. `offset` is the start offset.
- A file that cannot be read gives `UNREADABLE_SOURCE` "Cannot tokenize <file>: <error>".

## Packaging

- `kotlin-worker/build.gradle.kts` compiles against detekt 1.23.0 and Kotlin 1.8.21 as `compileOnly`, with API and language version 1.8 and JVM target 17.
- The jar contains only CodeShield classes and is reproducible.
- The pinned Gradle wrapper builds it during setup and packaging (`kotlin-worker/dist/codeshield-kotlin-worker.jar`).
- Worker tests run against the detekt-cli version in `gradle.properties` (`detektVersion`, default 1.23.8); `-PdetektVersion=1.23.0` tests the oldest supported release.
- CI runs the Kotlin integration tests on detekt-cli 1.23.0 and 1.23.8.
- `scripts/fetch-detekt.ps1` downloads and verifies a detekt-cli release for development and CI. End users install detekt-cli themselves; nothing is downloaded at analysis time.
