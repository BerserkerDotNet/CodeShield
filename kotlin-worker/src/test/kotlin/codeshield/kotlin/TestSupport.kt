package codeshield.kotlin

import java.nio.file.Files
import java.nio.file.Path

/** The worker's wire protocol, exercised through [Worker.respond] exactly as the .NET adapter calls it. */
internal object Wire {
    fun call(request: Map<String, Any?>): Map<*, *> =
        Json.parse(String(Worker.respond(Json.write(request).toByteArray(Charsets.UTF_8)), Charsets.UTF_8)) as Map<*, *>

    fun callRaw(text: String): ByteArray = Worker.respond(text.toByteArray(Charsets.UTF_8))
}

/** The pinned Kotlin correctness rules, as src/CodeShield.Correctness/CorrectnessCatalog.cs sends them. */
internal object PinnedRules {
    val ids = listOf(
        "EqualsAlwaysReturnsTrueOrFalse",
        "EqualsWithHashCodeExist",
        "ExceptionRaisedInUnexpectedLocation",
        "ExplicitGarbageCollectionCall",
        "InvalidRange",
        "IteratorHasNextCallsNextMethod",
        "IteratorNotThrowingNoSuchElementException",
        "ThrowingExceptionFromFinally",
        "UnconditionalJumpStatementInLoop",
        "UselessPostfixExpression",
        "WrongEqualsTypeParameter",
    )

    val all: List<Map<String, Any?>> = ids.map { rule(it) }

    fun rule(analyzerId: String, options: Map<String, Any?>? = null): Map<String, Any?> = mapOf(
        "analyzerId" to analyzerId,
        "evaluatorId" to evaluatorId(analyzerId),
        "options" to (options ?: defaultOptions(analyzerId)),
    )

    fun evaluatorId(analyzerId: String): String =
        "correctness.kotlin." + analyzerId.replace(Regex("(?<=[a-z0-9])(?=[A-Z])"), "-").lowercase()

    private fun defaultOptions(analyzerId: String): Map<String, Any?> =
        if (analyzerId == "ExceptionRaisedInUnexpectedLocation") mapOf("methodNames" to listOf("equals", "finalize", "hashCode", "toString"))
        else emptyMap()
}

/** A temporary repository of text files that is analyzed or tokenized through the worker protocol. */
internal class Repo(private val root: Path) {
    private val paths = sortedSetOf<String>()

    fun file(path: String, text: String): Repo {
        val target = root.resolve(path)
        Files.createDirectories(target.parent)
        Files.write(target, text.trimIndent().plus("\n").toByteArray(Charsets.UTF_8))
        paths.add(path)
        return this
    }

    fun raw(path: String, bytes: ByteArray): Repo {
        val target = root.resolve(path)
        Files.createDirectories(target.parent)
        Files.write(target, bytes)
        paths.add(path)
        return this
    }

    fun analyze(
        focus: List<String> = paths.filter { it.endsWith(".kt") },
        changedConfigFiles: List<String> = emptyList(),
        rules: List<Map<String, Any?>> = PinnedRules.all,
        files: List<String> = paths.toList(),
    ): Analysis = Analysis(
        Wire.call(
            mapOf(
                "protocol" to 1,
                "command" to "analyze",
                "root" to root.toString(),
                "files" to files,
                "focusFiles" to focus,
                "changedConfigFiles" to changedConfigFiles,
                "isBaseline" to false,
                "correctnessRules" to rules,
            ),
        ),
    )

    fun tokenize(files: List<String> = paths.toList()): Map<*, *> =
        Wire.call(mapOf("protocol" to 1, "command" to "tokenize", "root" to root.toString(), "files" to files))
}

/** Writes a temporary repository with [files], analyzes it once and deletes it; for an analysis that several tests read. */
internal fun analyzeOnce(prefix: String, files: Repo.() -> Unit): Analysis {
    val root = Files.createTempDirectory(prefix)
    try {
        return Repo(root).apply(files).analyze()
    } finally {
        root.toFile().deleteRecursively()
    }
}

/** Read access to one analyze response. */
internal class Analysis(val json: Map<*, *>) {
    val contexts: List<Map<*, *>> get() = list(json, "contexts")
    val graph: Map<*, *> get() = json["graph"] as Map<*, *>
    val unownedFiles: List<*> get() = json["unownedFiles"] as List<*>

    val contextIds: List<String> get() = contexts.map { it["contextId"] as String }

    fun context(id: String): Map<*, *> =
        contexts.firstOrNull { it["contextId"] == id } ?: throw AssertionError("No context $id in $contextIds")

    /** The method facts of one file, keyed by unit ID. */
    fun methods(file: String): Map<String, Map<*, *>> =
        fileFact(file).let { list(it, "methods") }.associateBy { it["unitId"] as String }

    fun method(file: String, qualified: String): Map<*, *> =
        methods(file)["$file#$qualified"] ?: throw AssertionError("No method $qualified in ${methods(file).keys}")

    fun fileFact(file: String): Map<*, *> =
        contexts.flatMap { list(it, "files") }.firstOrNull { it["path"] == file } ?: throw AssertionError("No file fact for $file")

    fun classFact(file: String, qualified: String): Map<*, *> =
        contexts.flatMap { list(it, "classes") }.firstOrNull { it["unitId"] == "$file#$qualified" }
            ?: throw AssertionError("No class fact for $qualified")

    fun types(): List<Map<*, *>> = contexts.flatMap { list(it, "types") }

    fun diagnostics(file: String? = null): List<Map<*, *>> =
        contexts.flatMap { list(it, "diagnostics") }.filter { file == null || it["file"] == file }

    fun failures(code: String? = null): List<Map<*, *>> =
        contexts.flatMap { list(it, "failures") }.filter { code == null || it["code"] == code }

    companion object {
        @Suppress("UNCHECKED_CAST")
        fun list(map: Map<*, *>, key: String): List<Map<*, *>> = map[key] as List<Map<*, *>>
    }
}

internal fun Map<*, *>.int(key: String): Int = (this[key] as Number).toInt()
