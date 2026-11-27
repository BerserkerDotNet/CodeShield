package codeshield.kotlin

/** A project declared by a settings script; [dir] is null when the project lies outside the repository. */
internal class ProjectDecl(val path: String, var dir: String?) {
    var buildFileName: String? = null
}

/** What one settings script, and the scripts it applies, declares about its build. */
internal class SettingsModel(
    val file: String,
    val rootDir: String,
    val chain: List<String>,
    val projects: Map<String, ProjectDecl>,
    val includedBuilds: List<String>,
    val failures: List<Reason>,
)

/**
 * Reads `include`, `includeFlat`, `includeBuild`, `project(...).projectDir`, `buildFileName` and `apply from` from a
 * settings script without executing it. Every occurrence counts, including those inside conditions and loops; values that
 * are not literal paths make the build incomplete rather than silently wrong.
 */
internal class SettingsReader(private val source: ScriptSource) {
    fun read(file: String): SettingsModel = SettingsWalk(file, source).run()
}

private class SettingsWalk(private val file: String, private val source: ScriptSource) {
    private val rootDir = RepoPath.dirOf(file)
    private val paths = PathEvaluator(rootDir, rootDir, rootDir)
    private val chain = ArrayList<String>()
    private val failures = ArrayList<Reason>()
    private val projects = LinkedHashMap<String, ProjectDecl>()
    private val dirs = ArrayList<Pair<String, ScriptPath>>()
    private val buildFiles = ArrayList<Pair<String, String>>()
    private val includedBuilds = ArrayList<String>()

    fun run(): SettingsModel {
        projects[":"] = ProjectDecl(":", rootDir)
        script(file)
        for ((path, dir) in dirs) projects[path]?.dir = dir.path
        for ((path, name) in buildFiles) projects[path]?.buildFileName = name
        return SettingsModel(file, rootDir, chain.toList(), projects, includedBuilds.distinct(), failures.sorted())
    }

    private fun script(path: String) {
        if (path in chain) return
        chain.add(path)
        val loaded = source.load(path) ?: return
        loaded.failure?.let { failures.add(it) }
        ScriptForms.walk(loaded.statements) { visit(it, path) }
    }

    private fun visit(node: GNode, script: String) {
        if (node is GAssign && node.op == "=") assign(node, script)
        if (node is GCall) call(node, script)
    }

    private fun call(call: GCall, script: String) {
        val property = SETTERS[call.name]
        when {
            property != null -> setter(call, property, script)
            SettingsForms.isUnqualified(call) -> declaration(call, script)
        }
    }

    private fun declaration(call: GCall, script: String) {
        when (call.name) {
            "include" -> include(call, script, flat = false)
            "includeFlat" -> include(call, script, flat = true)
            "includeBuild" -> includeBuild(call)
            "apply" -> ScriptForms.appliedTargets(call).forEach { apply(it, script) }
        }
    }

    private fun include(call: GCall, script: String, flat: Boolean) {
        for (item in call.args.filter { it.name == null }.flatMap { SettingsForms.items(it.value) }) {
            val literal = (item as? GStr)?.literal
            if (literal == null) {
                fail(call.name, script, item.line)
                continue
            }
            val path = ScriptForms.projectPath(literal) ?: continue
            if (flat) addFlat(path) else addNested(path)
        }
    }

    private fun includeBuild(call: GCall) {
        val dir = call.arg(0)?.let { paths.single(it) }?.path ?: return
        includedBuilds.add(dir)
    }

    /** `include("a:b")` declares `:a` and `:a:b` with the default directories `a` and `a/b`. */
    private fun addNested(path: String) {
        val segments = path.removePrefix(":").split(':')
        for (count in 1..segments.size) {
            val project = ":" + segments.take(count).joinToString(":")
            if (project in projects) continue
            projects[project] = ProjectDecl(project, PathEvaluator.resolve(rootDir, segments.take(count).joinToString("/")).path)
        }
    }

    /** `includeFlat("a")` declares `:a` next to the root directory. */
    private fun addFlat(path: String) {
        val name = path.substringAfterLast(':')
        if (path !in projects) projects[path] = ProjectDecl(path, PathEvaluator.resolve(rootDir, "../$name").path)
    }

    private fun assign(node: GAssign, script: String) {
        val target = node.target as? GMember ?: return
        if (target.name == "projectDir" || target.name == "buildFileName") configure(target.receiver, target.name, node.value, script, node.line)
    }

    private fun setter(call: GCall, property: String, script: String) {
        val value = call.arg(0) ?: return fail(property, script, call.line)
        configure(call.receiver, property, value, script, call.line)
    }

    private fun configure(receiver: GNode?, property: String, value: GNode, script: String, line: Int) {
        val project = receiver?.let { SettingsForms.projectOf(it) } ?: return fail(property, script, line)
        if (property == "projectDir") {
            val dir = paths.single(value) ?: return fail(property, script, line)
            dirs.add(project to dir)
            return
        }
        val name = (value as? GStr)?.literal ?: return fail(property, script, line)
        buildFiles.add(project to name)
    }

    private fun fail(what: String, script: String, line: Int) {
        failures.add(cannotEvaluate(what, script, line))
    }

    private fun apply(target: GNode, script: String) {
        val applied = ScriptForms.resolveApplied(target, paths, source, script) ?: return
        applied.failure?.let { failures.add(it) }
        applied.path?.let { script(it) }
    }

    private fun cannotEvaluate(what: String, script: String, line: Int) = Reason(
        ReasonCodes.PROJECT_LOAD_FAILED,
        "Cannot evaluate $what in ${ScriptForms.line(script, line)}; only literal values are supported.",
        null,
        script,
    )

    companion object {
        /** The settings setters and the project property each one sets. */
        private val SETTERS = mapOf("setProjectDir" to "projectDir", "setBuildFileName" to "buildFileName")
    }
}

/** Recognisers for the settings-script forms [SettingsWalk] reads; none of them depends on what the walk has collected. */
private object SettingsForms {
    private val LIST_BUILDERS = setOf("listOf", "setOf", "arrayOf", "mutableListOf")
    private val PROJECT_LOOKUPS = setOf("project", "findProject")

    /** A call on the settings object itself: without a receiver or through `settings`, `this` or `project`. */
    fun isUnqualified(call: GCall): Boolean = call.receiver == null || ScriptForms.isSelf(call.receiver)

    /** The values one `include` argument lists: the items of a list literal or `listOf(...)`, or the value itself. */
    fun items(node: GNode): List<GNode> = when {
        node is GList -> node.items
        node is GCall && node.receiver == null && node.name in LIST_BUILDERS -> node.args.map { it.value }
        else -> listOf(node)
    }

    /** The project that `rootProject` or `project(':x')` refers to, or null for anything else. */
    fun projectOf(node: GNode): String? = when {
        node is GRef && node.name == "rootProject" -> ":"
        node is GCall && isProjectLookup(node) -> (node.arg(0) as? GStr)?.literal?.let { ScriptForms.projectPath(it) }
        else -> null
    }

    private fun isProjectLookup(call: GCall): Boolean = call.name in PROJECT_LOOKUPS && isUnqualified(call)
}
