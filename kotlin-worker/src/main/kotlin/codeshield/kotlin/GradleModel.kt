package codeshield.kotlin

/** The project and role (main or test sources) that own a source file. */
internal class SourceOwner(val project: GradleProject, val test: Boolean)

/** The Gradle builds of a repository as CodeShield sees them: projects, source ownership and the project dependency graph. */
internal class GradleModel(
    val projects: List<GradleProject>,
    val owners: Map<String, SourceOwner>,
    val modules: List<String>,
    val edges: List<ModuleEdge>,
    val unresolved: List<Reason>,
)

/** Reads every build in the repository and links project dependencies within each build. */
internal class GradleModelBuilder(private val files: Set<String>, private val source: ScriptSource) {
    private val edges = sortedSetOf<ModuleEdge>()
    private val unresolved = sortedSetOf<Reason>()
    private val declared = ArrayList<Pair<GradleProject, SourceDirDecl>>()

    /** Builds the model and assigns each of [sourceFiles] to its owning project. */
    fun build(sourceFiles: List<String>): GradleModel {
        val builds = BuildDiscovery(files, SettingsReader(source)).discover()
        builds.forEach { unresolved.addAll(it.failures) }
        builds.forEach(::readScripts)
        val projects = builds.flatMap { it.projects.values }
        val ownership = SourceOwnership(projects, declared)
        val owners = LinkedHashMap<String, SourceOwner>()
        for (file in sourceFiles) ownership.ownerOf(file)?.let { owners[file] = it }
        val linked = edges.flatMap { listOf(it.from, it.to) }
        val modules = projects.filter { it.script != null }.map { it.moduleId } + owners.values.map { it.project.moduleId } + linked
        return GradleModel(projects, owners, ordinalSorted(modules), edges.toList(), unresolved.toList())
    }

    private fun readScripts(build: GradleBuild) {
        val reader = BuildScriptReader(source, build.rootDir) { build.projects[it]?.dir }
        build.projects.values.forEach { it.chain.addAll(build.chain) }
        val refs = ArrayList<ProjectRef>()
        for (project in build.projects.values) {
            val script = project.script ?: continue
            val facts = reader.read(project.path, script)
            project.chain.addAll(facts.chain)
            record(build, project, facts)
            refs.addAll(facts.refs)
        }
        refs.forEach { link(build, it) }
    }

    /** Keeps a script's failures and source directories, and adds the script to the chain of every project it configures. */
    private fun record(build: GradleBuild, project: GradleProject, facts: BuildScriptFacts) {
        unresolved.addAll(facts.failures)
        project.failures.addAll(facts.failures.filter { it.code == ReasonCodes.PROJECT_LOAD_FAILED })
        val configured = (facts.refs.map { it.owner } + facts.sourceDirs.map { it.owner }).toSet() - project.path
        configured.mapNotNull { build.projects[it] }.forEach { it.chain.addAll(facts.chain) }
        facts.sourceDirs.forEach { decl -> build.projects[decl.owner]?.let { declared.add(it to decl) } }
    }

    /** Turns a project reference into an edge between modules of the same build, or explains why it cannot. */
    private fun link(build: GradleBuild, ref: ProjectRef) {
        if (ref.owner in build.external) return
        val at = ScriptForms.line(ref.file, ref.line)
        val owner = build.projects[ref.owner] ?: return unresolve(ref, "Cannot resolve project '${ref.owner}' configured by $at.")
        val project = targetProject(build, ref) ?: return
        if (project.moduleId != owner.moduleId) edges.add(ModuleEdge(owner.moduleId, project.moduleId))
    }

    /** The project [ref] depends on, or null when it lies outside the repository or cannot be resolved (then it is reported). */
    private fun targetProject(build: GradleBuild, ref: ProjectRef): GradleProject? {
        val target = targetOf(build, ref) ?: return null
        if (target in build.external) return null
        val project = build.projects[target]
        if (project == null) unresolve(ref, "Cannot resolve project '$target' referenced by ${ScriptForms.line(ref.file, ref.line)}.")
        return project
    }

    private fun targetOf(build: GradleBuild, ref: ProjectRef): String? {
        val at = ScriptForms.line(ref.file, ref.line)
        if (ref.path != null) return ref.path
        val accessor = ref.accessor
        if (accessor == null) {
            unresolve(ref, "Cannot resolve the project referenced by $at; only literal project paths are supported.")
            return null
        }
        val name = accessor.joinToString(".")
        val target = build.projects.keys.firstOrNull { accessorOf(it) == name } ?: build.external.firstOrNull { accessorOf(it) == name }
        if (target == null) unresolve(ref, "Cannot resolve project accessor 'projects.$name' referenced by $at.")
        return target
    }

    private fun unresolve(ref: ProjectRef, message: String) {
        unresolved.add(Reason(ReasonCodes.MISSING_DEPENDENCY, message, null, ref.file))
    }

    companion object {
        /** Gradle's type-safe accessor for a project path: `:core-data:api` is `coreData.api`. */
        fun accessorOf(path: String): String = path.removePrefix(":").split(':').joinToString(".") { camelCase(it) }

        private fun camelCase(name: String): String {
            val parts = name.split('-', '_').filter { it.isNotEmpty() }
            if (parts.isEmpty()) return name
            return parts[0] + parts.drop(1).joinToString("") { part -> part.substring(0, 1).uppercase() + part.substring(1) }
        }
    }
}

/**
 * Assigns each source file to the project whose source directory contains it: the conventional
 * `<project>/src/<set>/(kotlin|java)` layout of JVM, Android and Kotlin Multiplatform projects, or a directory declared with
 * `srcDir(...)`. The deepest directory wins; ties go to the ordinal-smallest module and then to main sources.
 */
internal class SourceOwnership(projects: List<GradleProject>, private val declared: List<Pair<GradleProject, SourceDirDecl>>) {
    private val byDir = projects.groupBy { it.dir }

    fun ownerOf(file: String): SourceOwner? = (conventional(file) + explicit(file)).maxWithOrNull(ORDER)?.owner

    private fun conventional(file: String): List<SourceRoot> {
        val segments = file.split('/')
        val roots = ArrayList<SourceRoot>()
        for (index in segments.indices) {
            if (!isConventionalRoot(segments, index)) continue
            val dir = segments.take(index).joinToString("/")
            val root = segments.take(index + 3).joinToString("/")
            val test = ScriptForms.isTestName(segments[index + 1])
            byDir[dir].orEmpty().forEach { roots.add(SourceRoot(root, SourceOwner(it, test))) }
        }
        return roots
    }

    private fun isConventionalRoot(segments: List<String>, index: Int): Boolean =
        segments[index] == "src" && index + 3 < segments.size && segments[index + 2] in LANGUAGES

    private fun explicit(file: String): List<SourceRoot> =
        declared.filter { (_, decl) -> file != decl.dir && RepoPath.isUnder(file, decl.dir) }
            .map { (project, decl) -> SourceRoot(decl.dir, SourceOwner(project, decl.test)) }

    private class SourceRoot(val dir: String, val owner: SourceOwner)

    companion object {
        private val LANGUAGES = setOf("kotlin", "java")
        private val ORDER = compareBy<SourceRoot> { it.dir.length }
            .thenByDescending { it.owner.project.moduleId }
            .thenByDescending { it.owner.test }
    }
}
