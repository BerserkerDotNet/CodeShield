package codeshield.kotlin

/**
 * A project dependency declared by a build script for project [owner]. [path] is a Gradle project path and [accessor] a
 * type-safe accessor chain (`projects.core.data` is `[core, data]`); both are null for a computed reference.
 */
internal class ProjectRef(val owner: String, val path: String?, val accessor: List<String>?, val file: String, val line: Int)

/** A source directory declared explicitly for a source set of project [owner]. */
internal class SourceDirDecl(val owner: String, val dir: String, val test: Boolean)

/** What a project's build script and the scripts it applies declare. */
internal class BuildScriptFacts(
    val chain: List<String>,
    val refs: List<ProjectRef>,
    val sourceDirs: List<SourceDirDecl>,
    val failures: List<Reason>,
)

/**
 * Reads project dependencies, explicit source directories and applied scripts from a build script without executing it.
 * Only dependencies outside test scopes count. Declarations for several projects at once (`subprojects`, `allprojects`,
 * `configure(...)`) cannot be attributed and are reported instead.
 */
internal class BuildScriptReader(
    private val source: ScriptSource,
    private val rootDir: String,
    private val projectDirs: (String) -> String?,
) {
    fun read(project: String, script: String): BuildScriptFacts {
        val walk = BuildWalk(source, rootDir, projectDirs)
        walk.script(script, BuildScope(project, emptyList(), false))
        return BuildScriptFacts(walk.chain.toList(), walk.refs, walk.sourceDirs, walk.failures.sorted())
    }
}

/** Where a statement sits: the project it configures (null for several), the enclosing block names and whether it declares dependencies. */
private class BuildScope(val owner: String?, val names: List<String>, val dependencies: Boolean) {
    fun enter(added: List<String>) = BuildScope(owner, names + added, dependencies)
}

private class BuildWalk(
    private val source: ScriptSource,
    private val rootDir: String,
    private val projectDirs: (String) -> String?,
) {
    val chain = ArrayList<String>()
    val refs = ArrayList<ProjectRef>()
    val sourceDirs = ArrayList<SourceDirDecl>()
    val failures = ArrayList<Reason>()

    fun script(path: String, scope: BuildScope) {
        if (path in chain) return
        chain.add(path)
        val loaded = source.load(path) ?: return
        loaded.failure?.let { failures.add(it) }
        statements(loaded.statements, path, scope)
    }

    private fun statements(nodes: List<GNode>, file: String, scope: BuildScope) = nodes.forEach { visit(it, file, scope) }

    private fun visit(node: GNode, file: String, scope: BuildScope) {
        when {
            node is GCall -> call(node, file, scope)
            node is GAssign && !scope.dependencies && BuildForms.isSourceDirs(node.target) -> assignSourceDirs(node, scope)
            else -> statements(node.children, file, scope)
        }
    }

    private fun call(call: GCall, file: String, scope: BuildScope) {
        when {
            scope.dependencies || (call.receiver as? GRef)?.name == "dependencies" -> dependency(call, file, scope)
            BuildForms.isSkippedBlock(call) -> Unit
            else -> configure(call, file, scope)
        }
    }

    /** A call outside `dependencies`: an applied script, a source directory, a block opening a new scope, or a nested call. */
    private fun configure(call: GCall, file: String, scope: BuildScope) {
        val applied = ScriptForms.appliedTargets(call)
        if (applied.isNotEmpty()) return applied.forEach { apply(it, file, scope) }
        if (call.name in BuildForms.SOURCE_DIR_CALLS) {
            return declareSourceDirs(scope.names + BuildForms.receiverNames(call.receiver), call.args.map { it.value }, scope)
        }
        val block = call.block ?: return visitArguments(call, file, scope)
        val opened = BuildForms.openedScope(call, scope)
        if (opened != null) return statements(block.statements, file, opened)
        visitArguments(call, file, scope)
        statements(block.statements, file, scope.enter(BuildForms.scopeNames(call)))
    }

    private fun visitArguments(call: GCall, file: String, scope: BuildScope) {
        call.receiver?.let { visit(it, file, scope) }
        for (arg in call.args) visit(arg.value, file, scope)
    }

    /** One statement of a `dependencies` block: the configuration call and the project references in its arguments. */
    private fun dependency(call: GCall, file: String, scope: BuildScope) {
        if (call.name in BuildForms.SKIPPED || BuildForms.isTestScope(call, scope)) return
        val found = BuildForms.projectRefs(call)
        val block = call.block
        if (found.isEmpty() && block != null) return statements(block.statements, file, scope.enter(listOf(call.name)))
        found.forEach { record(it, file, scope) }
    }

    private fun record(node: GNode, file: String, scope: BuildScope) {
        val owner = scope.owner
        if (owner == null) {
            failures.add(Reason(ReasonCodes.MISSING_DEPENDENCY,
                "Cannot attribute the project dependency in ${ScriptForms.line(file, node.line)} to a single project; " +
                    "dependencies declared for several projects at once are not modelled.", null, file))
            return
        }
        if (node is GMember) return refs.add(ProjectRef(owner, null, BuildForms.accessorChain(node), file, node.line)).let { }
        val call = node as GCall
        val literal = ((call.arg(0) ?: call.named("path")) as? GStr)?.literal
        val base = if ((call.receiver as? GRef)?.name == "rootProject") ":" else owner
        refs.add(ProjectRef(owner, literal?.let { ScriptForms.projectPath(it, base) }, null, file, node.line))
    }

    /**
     * Follows `apply from` for the project being configured. Gradle resolves a relative path against each configured
     * project's directory, so inside `subprojects { }` only root-anchored paths can be followed.
     */
    private fun apply(target: GNode, file: String, scope: BuildScope) {
        val dir = scope.owner?.let(projectDirs)
        val paths = PathEvaluator(dir, dir, rootDir)
        if (scope.owner == null && !PathEvaluator.isUrl(target) && paths.single(target)?.path == null) {
            failures.add(Reason(ReasonCodes.MISSING_DEPENDENCY,
                "Cannot resolve the script applied by ${ScriptForms.line(file, target.line)} for several projects at once; " +
                    "only paths anchored at the root directory are supported there.", null, file))
            return
        }
        val applied = ScriptForms.resolveApplied(target, paths, source, file) ?: return
        applied.failure?.let { failures.add(it) }
        applied.path?.let { script(it, scope) }
    }

    private fun assignSourceDirs(node: GAssign, scope: BuildScope) {
        val target = node.target
        val names = scope.names + if (target is GMember) BuildForms.receiverNames(target.receiver) else emptyList()
        declareSourceDirs(names, listOf(node.value), scope)
    }

    /** Accepts `sourceSets.<set>.(kotlin|java).srcDir(...)` in any nesting of blocks and member chains. */
    private fun declareSourceDirs(names: List<String>, values: List<GNode>, scope: BuildScope) {
        val owner = scope.owner ?: return
        val set = BuildForms.sourceSetOf(names) ?: return
        val dir = projectDirs(owner) ?: return
        val paths = PathEvaluator(dir, dir, rootDir)
        for (value in values) {
            for (path in paths.paths(value).orEmpty()) path.path?.let { sourceDirs.add(SourceDirDecl(owner, it, ScriptForms.isTestName(set))) }
        }
    }
}

/** Recognisers for the build-script forms [BuildWalk] follows; none of them depends on what the walk has collected. */
private object BuildForms {
    val SKIPPED = setOf("buildscript", "plugins", "pluginManagement", "configurations", "constraints")
    val SOURCE_DIR_CALLS = setOf("srcDir", "srcDirs", "setSrcDirs")
    private val CROSS_PROJECT = setOf("subprojects", "allprojects", "childProjects")
    private val PROJECT_LOOKUPS = setOf("project", "findProject")
    private val PROJECT_RECEIVERS = setOf("project", "rootProject", "this")
    private val NAMED_LOOKUPS = setOf("getByName", "named", "create", "maybeCreate", "register")
    private val TRANSPARENT = setOf(
        "apply", "run", "with", "also", "let", "configure", "invoke", "configureEach", "all", "forEach", "each", "matching",
        "whenObjectAdded", "withType", "get",
    )

    /** A top-level block such as `plugins { }` whose content declares nothing the model reads. */
    fun isSkippedBlock(call: GCall): Boolean = call.block != null && call.receiver == null && call.name in SKIPPED

    /** The scope the block of [call] opens when it declares dependencies or configures other projects, otherwise null. */
    fun openedScope(call: GCall, scope: BuildScope): BuildScope? = when {
        call.name == "dependencies" -> dependencyScope(call, scope)
        call.name == "project" -> BuildScope(configuredProject(call, scope), emptyList(), false)
        isCrossProject(call) -> BuildScope(null, emptyList(), false)
        else -> null
    }

    /** `project(':x') { ... }` configures `:x`; a computed path configures an unknown project. */
    private fun configuredProject(call: GCall, scope: BuildScope): String? {
        val owner = scope.owner ?: return null
        return (call.arg(0) as? GStr)?.literal?.let { ScriptForms.projectPath(it, owner) }
    }

    /** `dependencies { }` declares for the enclosing project, `project(':x').dependencies { }` for `:x`. */
    private fun dependencyScope(call: GCall, scope: BuildScope): BuildScope {
        val receiver = call.receiver
        val owner = if (receiver is GCall && receiver.name == "project") configuredProject(receiver, scope) else scope.owner
        return BuildScope(owner, scope.names + receiverNames(receiver), true)
    }

    private fun isCrossProject(call: GCall): Boolean =
        call.name in CROSS_PROJECT || (call.name == "configure" && call.args.isNotEmpty()) ||
            receiverNames(call.receiver).any { it in CROSS_PROJECT }

    /** Whether a dependency declaration belongs to a test source set or configuration. */
    fun isTestScope(call: GCall, scope: BuildScope): Boolean =
        (scope.names + receiverNames(call.receiver) + configurationName(call)).any(ScriptForms::isTestName)

    /** `add("testImplementation", ...)` names its configuration in the first argument. */
    private fun configurationName(call: GCall): String =
        if (call.name == "add") (call.arg(0) as? GStr)?.literal ?: call.name else call.name

    /** The project references in the receiver and arguments of a dependency declaration. */
    fun projectRefs(call: GCall): List<GNode> {
        val found = ArrayList<GNode>()
        call.receiver?.let { collectRefs(it, found) }
        call.args.forEach { collectRefs(it.value, found) }
        return found
    }

    private fun collectRefs(node: GNode, out: MutableList<GNode>) {
        when {
            isProjectCall(node) || (node is GMember && accessorChain(node) != null) -> out.add(node)
            node is GCall -> {
                node.receiver?.let { collectRefs(it, out) }
                node.args.forEach { collectRefs(it.value, out) }
            }
            else -> node.children.forEach { collectRefs(it, out) }
        }
    }

    private fun isProjectCall(node: GNode): Boolean {
        if (node !is GCall || node.name !in PROJECT_LOOKUPS) return false
        val receiver = node.receiver
        return receiver == null || (receiver is GRef && receiver.name in PROJECT_RECEIVERS)
    }

    /** `projects.core.data` as `[core, data]`, or null when [node] is not a type-safe project accessor. */
    fun accessorChain(node: GMember): List<String>? {
        val names = ArrayList<String>()
        var current: GNode = node
        while (current is GMember) {
            names.add(current.name)
            current = current.receiver
        }
        return if (current is GRef && current.name == "projects") names.reversed() else null
    }

    fun isSourceDirs(target: GNode): Boolean =
        (target is GMember && target.name == "srcDirs") || (target is GRef && target.name == "srcDirs")

    fun sourceSetOf(names: List<String>): String? {
        val index = names.lastIndexOf("sourceSets")
        if (index < 0 || names.size != index + 3) return null
        return if (names[index + 2] == "kotlin" || names[index + 2] == "java") names[index + 1] else null
    }

    fun scopeNames(call: GCall): List<String> = receiverNames(call.receiver) + ownName(call)

    private fun ownName(call: GCall): List<String> = when (call.name) {
        in NAMED_LOOKUPS -> listOfNotNull((call.arg(0) as? GStr)?.literal)
        in TRANSPARENT -> emptyList()
        else -> listOf(call.name)
    }

    fun receiverNames(node: GNode?): List<String> = when (node) {
        is GRef -> if (ScriptForms.isSelf(node)) emptyList() else listOf(node.name)
        is GMember -> receiverNames(node.receiver) + node.name
        is GIndex -> receiverNames(node.receiver) + listOfNotNull((node.indices.singleOrNull() as? GStr)?.literal)
        is GCall -> receiverNames(node.receiver) + ownName(node)
        else -> emptyList()
    }
}
