package codeshield.kotlin

/** A Gradle build: a settings script with the projects it includes, or a single-project build rooted at a build script. */
internal class GradleBuild(val rootDir: String, val settings: String?, val chain: List<String>, val includedBuilds: List<String>) {
    val projects = LinkedHashMap<String, GradleProject>()

    /** Declared projects whose directories lie outside the repository; references to them are not part of the graph. */
    val external = HashSet<String>()
    val failures = ArrayList<Reason>()
}

/** A Gradle project whose directory is inside the repository; [script] is null when it has no build script. */
internal class GradleProject(val build: GradleBuild, val path: String, val dir: String, val script: String?) {
    /** The graph module: the build script, or the project directory when there is none (`.` for the repository root). */
    val moduleId: String get() = script ?: dir.ifEmpty { "." }

    /** Context IDs start with the build script, or with the settings file and project path when there is no build script. */
    val contextPrefix: String get() = script ?: "${build.settings}|$path"

    /** The script that configures the project's contexts. */
    val configPath: String get() = script ?: build.settings ?: moduleId

    /** Every script whose change can change the project: settings scripts, its build script and the scripts they apply. */
    val chain = LinkedHashSet<String>()

    /** Why the project's build configuration could not be fully read. */
    val failures = ArrayList<Reason>()
}

/**
 * Finds the Gradle builds in the repository from the request's files without running Gradle: every directory with a
 * settings script, builds included with `includeBuild` and `buildSrc` directories that have only a build script, and
 * build scripts outside every other build, which Gradle runs as single-project builds.
 */
internal class BuildDiscovery(private val files: Set<String>, private val settingsReader: SettingsReader) {
    fun discover(): List<GradleBuild> {
        val builds = settingsFiles().map(::settingsBuild).toMutableList()
        addScriptRoots(builds)
        addOrphans(builds)
        return builds
    }

    /** One settings script per directory; Gradle prefers `settings.gradle` when both exist. */
    private fun settingsFiles(): List<String> =
        files.filter { RepoPath.baseName(it) in SETTINGS }
            .groupBy { RepoPath.dirOf(it) }.values
            .map { group -> group.minByOrNull { SETTINGS.indexOf(RepoPath.baseName(it)) } ?: group[0] }
            .sorted()

    private fun settingsBuild(file: String): GradleBuild {
        val model = settingsReader.read(file)
        val build = GradleBuild(model.rootDir, file, model.chain, model.includedBuilds)
        build.failures.addAll(model.failures)
        for (decl in model.projects.values) {
            val dir = decl.dir
            if (dir == null) build.external.add(decl.path) else build.projects[decl.path] = GradleProject(build, decl.path, dir, scriptOf(dir, decl.buildFileName))
        }
        return build
    }

    private fun addScriptRoots(builds: MutableList<GradleBuild>) {
        val pending = ArrayDeque(builds.flatMap(::nestedRoots))
        while (pending.isNotEmpty()) {
            val dir = pending.removeFirst()
            if (builds.any { it.rootDir == dir }) continue
            val script = defaultScript(dir) ?: continue
            val build = scriptBuild(dir, script)
            builds.add(build)
            pending.addAll(nestedRoots(build))
        }
    }

    private fun nestedRoots(build: GradleBuild): List<String> = build.includedBuilds + RepoPath.join(build.rootDir, "buildSrc")

    /** Build scripts outside every discovered build; a nested one is a separate build because Gradle finds no settings for it. */
    private fun addOrphans(builds: MutableList<GradleBuild>) {
        val claimed = builds.map { it.rootDir }
        val orphans = files.filter { RepoPath.baseName(it) in DEFAULT_SCRIPTS && defaultScript(RepoPath.dirOf(it)) == it }
            .filter { script -> claimed.none { RepoPath.isUnder(script, it) } }
        for (script in orphans.sorted()) builds.add(scriptBuild(RepoPath.dirOf(script), script))
    }

    private fun scriptBuild(dir: String, script: String): GradleBuild {
        val build = GradleBuild(dir, null, emptyList(), emptyList())
        build.projects[":"] = GradleProject(build, ":", dir, script)
        return build
    }

    private fun defaultScript(dir: String): String? = DEFAULT_SCRIPTS.map { RepoPath.join(dir, it) }.firstOrNull { it in files }

    /** A custom `buildFileName` also matches its Kotlin DSL form, as Gradle does. */
    private fun scriptOf(dir: String, buildFileName: String?): String? {
        val names = if (buildFileName == null) DEFAULT_SCRIPTS else listOf(buildFileName, "$buildFileName.kts")
        return names.mapNotNull { RepoPath.normalize(RepoPath.join(dir, it)) }.firstOrNull { it in files }
    }

    companion object {
        private val SETTINGS = listOf("settings.gradle", "settings.gradle.kts")
        private val DEFAULT_SCRIPTS = listOf("build.gradle", "build.gradle.kts")
    }
}
