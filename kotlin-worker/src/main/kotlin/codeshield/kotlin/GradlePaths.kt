package codeshield.kotlin

/** A path produced by a build script: [path] is repository-relative, or null when it points outside the repository. */
internal class ScriptPath(val path: String?)

/**
 * Evaluates the path expressions build scripts use for project directories, source directories and applied scripts:
 * string literals, `"$rootDir/x"` templates, `file(...)`, `rootProject.file(...)`, `File(dir, "x")`, `files(...)`,
 * `layout.projectDirectory.dir(...)` and list builders. Anything computed is not evaluated (null).
 *
 * Directories are repository-relative (`""` is the repository root) or null when they lie outside the repository.
 */
internal class PathEvaluator(private val baseDir: String?, private val projectDir: String?, private val rootDir: String?) {
    /** The paths of [node], or null when it is not a supported path expression. */
    fun paths(node: GNode?): List<ScriptPath>? = when (node) {
        null -> null
        is GList -> all(node.items)
        is GCall -> call(node)
        else -> single(node)?.let { listOf(it) }
    }

    /** The single path of [node], or null when it is not exactly one supported path expression. */
    fun single(node: GNode?): ScriptPath? = when (node) {
        is GStr -> string(node)
        is GCall -> paths(node)?.singleOrNull()
        is GMember -> member(node)
        is GRef -> anchor(node.name, projectDir)?.let { ScriptPath(it.dir) }
        else -> null
    }

    private fun all(nodes: List<GNode>): List<ScriptPath>? {
        val out = ArrayList<ScriptPath>()
        for (node in nodes) out.addAll(paths(node) ?: return null)
        return out
    }

    private fun call(node: GCall): List<ScriptPath>? = when (node.name) {
        "File" -> javaFile(node)?.let { listOf(it) }
        in LIST_BUILDERS -> listBuilder(node)
        else -> receiverCall(node)
    }

    /** `listOf(...)` and the other builders; a qualified call such as `x.listOf(...)` is not one. */
    private fun listBuilder(node: GCall): List<ScriptPath>? = if (node.receiver == null) all(node.args.map { it.value }) else null

    /** `file(...)`, `files(...)` and `x.dir(...)`, resolved against the directory the receiver supplies. */
    private fun receiverCall(node: GCall): List<ScriptPath>? {
        val receiverDir = receiverDir(node.receiver) ?: return null
        return when (node.name) {
            "files" -> all(node.args.map { it.value })?.takeIf { node.block == null }
            "file", "dir" -> node.args.singleOrNull()?.value?.let { relativeTo(receiverDir, it) }?.let { listOf(it) }
            else -> null
        }
    }

    /** The base directory a call's receiver supplies: the evaluator's base for `file(...)`, a directory for `x.dir(...)`. */
    private fun receiverDir(receiver: GNode?): Anchor? = when {
        receiver == null || (receiver is GRef && receiver.name in SELF) -> Anchor(baseDir)
        receiver is GRef && receiver.name == "rootProject" -> Anchor(rootDir)
        else -> single(receiver)?.let { Anchor(it.path) }
    }

    /** `File(parent, "child")`, or `File("$rootDir/x")` when the single argument is anchored. */
    private fun javaFile(node: GCall): ScriptPath? {
        val args = node.args.map { it.value }
        if (args.size == 1) return (args[0] as? GStr)?.let { anchored(it) }
        if (args.size != 2) return null
        val parent = single(args[0]) ?: return null
        return relativeTo(Anchor(parent.path), args[1])
    }

    private fun relativeTo(base: Anchor, node: GNode): ScriptPath? {
        if (node !is GStr) return single(node)
        return anchored(node) ?: node.literal?.let { resolve(base.dir, it) }
    }

    private fun string(node: GStr): ScriptPath? = anchored(node) ?: node.literal?.let { resolve(baseDir, it) }

    /** A template that starts with an anchor such as `$rootDir` or `${project.projectDir}`, followed by literal text. */
    private fun anchored(node: GStr): ScriptPath? {
        val first = node.parts.firstOrNull() as? GExpr ?: return null
        val rest = node.parts.drop(1)
        if (rest.any { it !is GText }) return null
        val dir = anchorOf(first.node) ?: return null
        val suffix = rest.joinToString("") { (it as GText).text }.trimStart('/', '\\')
        return resolve(dir.dir, suffix)
    }

    private fun member(node: GMember): ScriptPath? {
        if (node.name in FILE_VIEWS) return single(node.receiver)
        return anchorOf(node)?.let { ScriptPath(it.dir) }
    }

    /** `rootDir`, `project.projectDir`, `rootProject.projectDir`, `layout.projectDirectory`, `settings.settingsDir`, ... */
    private fun anchorOf(node: GNode): Anchor? = when (node) {
        is GRef -> anchor(node.name, projectDir)
        is GMember -> memberAnchor(node)
        else -> null
    }

    private fun memberAnchor(node: GMember): Anchor? {
        if (node.name in FILE_VIEWS) return anchorOf(node.receiver)
        val receiver = node.receiver
        if (receiver is GMember) return rootLayoutAnchor(receiver, node.name)
        return when ((receiver as? GRef)?.name) {
            in SELF -> anchor(node.name, projectDir)
            "rootProject" -> anchor(node.name, rootDir)
            "layout" -> layoutAnchor(node.name, projectDir)
            else -> null
        }
    }

    /** `rootProject.layout.<name>`; [layout] is the receiver of the member. */
    private fun rootLayoutAnchor(layout: GMember, name: String): Anchor? {
        val isRootLayout = layout.name == "layout" && (layout.receiver as? GRef)?.name == "rootProject"
        return if (isRootLayout) layoutAnchor(name, rootDir) else null
    }

    private fun anchor(name: String, ownDir: String?): Anchor? = when (name) {
        "rootDir", "settingsDir" -> Anchor(rootDir)
        "projectDir" -> Anchor(ownDir)
        "buildDir" -> Anchor(ownDir?.let { RepoPath.join(it, "build") })
        else -> null
    }

    private fun layoutAnchor(name: String, ownDir: String?): Anchor? = when (name) {
        "projectDirectory" -> Anchor(ownDir)
        "settingsDirectory" -> Anchor(rootDir)
        "buildDirectory" -> Anchor(ownDir?.let { RepoPath.join(it, "build") })
        else -> null
    }

    private class Anchor(val dir: String?)

    companion object {
        private val SELF = setOf("project", "this", "settings")
        private val LIST_BUILDERS = setOf("listOf", "setOf", "arrayOf", "mutableListOf", "mutableSetOf")
        private val FILE_VIEWS = setOf("asFile", "absolutePath", "path", "canonicalPath", "absoluteFile", "canonicalFile")
        private val URL = Regex("^[A-Za-z][A-Za-z0-9+.-]*://")

        /** True for `https://...` style strings, which name remote scripts rather than repository files. */
        fun isUrl(node: GNode?): Boolean = (node as? GStr)?.literal?.let { URL.containsMatchIn(it) } == true

        /** Joins [relative] to [dir]; absolute paths and paths leaving the repository are outside it. */
        fun resolve(dir: String?, relative: String): ScriptPath {
            val posix = RepoPath.toPosix(relative)
            if (dir == null || RepoPath.isAbsolute(posix)) return ScriptPath(null)
            return ScriptPath(RepoPath.normalize(RepoPath.join(dir, posix)))
        }
    }
}
