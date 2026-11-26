package codeshield.kotlin

/** A Gradle script reduced to statements; [failure] explains why it could not be parsed or loaded (then it has no statements). */
internal class LoadedScript(val path: String, val statements: List<GNode>, val failure: Reason?)

/** Reads and parses each Gradle script once, recording it as an analysis input. */
internal class ScriptSource(private val ledger: InputLedger, private val scripts: GradleScripts) {
    private val cache = HashMap<String, LoadedScript?>()

    /** The parsed script, or null when the file is absent or unreadable (unreadable files are reported by the ledger). */
    fun load(path: String): LoadedScript? = if (cache.containsKey(path)) cache[path] else parse(path).also { cache[path] = it }

    private fun parse(path: String): LoadedScript? {
        val text = ledger.readRepoText(path) ?: return null
        val script = try {
            scripts.read(path, text)
        } catch (error: Exception) {
            return failed(path, "Cannot load $path: ${error.message ?: error.javaClass.simpleName}")
        } catch (error: StackOverflowError) {
            return failed(path, "Cannot load $path: the script is nested too deeply.")
        }
        val syntax = script.error ?: return LoadedScript(path, script.statements, null)
        return failed(path, "Cannot parse $path: syntax error at $syntax.")
    }

    private fun failed(path: String, message: String) =
        LoadedScript(path, emptyList(), Reason(ReasonCodes.PROJECT_LOAD_FAILED, message, null, path))
}

/** Shared recognisers for the script forms both settings and build scripts use. */
internal object ScriptForms {
    /** The `from` targets of `apply from: x`, `apply(from = x)` and `apply { from(x) }`. */
    fun appliedTargets(call: GCall): List<GNode> {
        if (call.name != "apply" || !(call.receiver == null || isSelf(call.receiver))) return emptyList()
        val named = call.named("from")
        if (named != null) return listOf(named)
        return call.block?.statements.orEmpty()
            .filterIsInstance<GCall>()
            .filter { it.name == "from" && it.receiver == null }
            .mapNotNull { it.arg(0) }
    }

    fun isSelf(node: GNode?): Boolean = node is GRef && node.name in setOf("project", "this", "settings")

    /** Every node below [nodes] in document order, including the nodes themselves. */
    fun walk(nodes: List<GNode>, visit: (GNode) -> Unit) {
        for (node in nodes) {
            visit(node)
            walk(node.children, visit)
        }
    }

    /** Joins a Gradle project path to a parent: `":app"` + `"core"` is `":app:core"`; absolute paths are kept. */
    fun projectPath(path: String, parent: String = ":"): String? {
        val trimmed = path.trim()
        if (trimmed.isEmpty()) return null
        if (trimmed.startsWith(":")) return trimmed.trimEnd(':').ifEmpty { ":" }
        return if (parent == ":") ":${trimmed.trimEnd(':')}" else "$parent:${trimmed.trimEnd(':')}"
    }

    fun line(script: String, line: Int): String = "$script at line $line"

    /** Test source sets and configurations: `test`, `testImplementation`, `androidTest`, `jvmTest`, `debugUnitTest`, ... */
    fun isTestName(name: String): Boolean = name.startsWith("test") || name.contains("Test")

    /**
     * Follows `apply from`: the repository script to read next, or the reason it cannot be followed. Remote scripts
     * (`https://...`) yield null because they are not part of the repository.
     */
    fun resolveApplied(target: GNode, paths: PathEvaluator, source: ScriptSource, script: String): AppliedScript? {
        if (PathEvaluator.isUrl(target)) return null
        val resolved = paths.single(target)
        val path = resolved?.path
        val at = line(script, target.line)
        val problem = when {
            resolved == null -> "Cannot resolve the script applied by $at; only literal paths are supported."
            path == null -> "Cannot read the script applied by $at because it is outside the repository."
            source.load(path) == null -> "Cannot read script '$path' applied by $at."
            else -> return AppliedScript(path, null)
        }
        return AppliedScript(null, Reason(ReasonCodes.MISSING_DEPENDENCY, problem, null, script))
    }
}

/** An `apply from` target: the script [path] to follow, or the [failure] explaining why it cannot be followed. */
internal class AppliedScript(val path: String?, val failure: Reason?)
