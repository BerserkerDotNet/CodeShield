package codeshield.kotlin

// Wire types mirroring the C# records in src/CodeShield.Abstractions (camelCase JSON).

internal const val PROTOCOL_VERSION = 1

internal const val LANGUAGE = "kotlin"

internal object ReasonCodes {
    const val MISSING_ANALYZER = "MISSING_ANALYZER"
    const val MISSING_DEPENDENCY = "MISSING_DEPENDENCY"
    const val PROJECT_LOAD_FAILED = "PROJECT_LOAD_FAILED"
    const val UNRESOLVED_OWNERSHIP = "UNRESOLVED_OWNERSHIP"
    const val SUPPRESSED_RULE = "SUPPRESSED_RULE"
    const val UNREADABLE_SOURCE = "UNREADABLE_SOURCE"
}

internal data class Reason(
    val code: String,
    val message: String,
    val ruleId: String? = null,
    val contextId: String? = null,
) : JsonValue, Comparable<Reason> {
    private val key: String get() = listOf(code, ruleId ?: "", contextId ?: "", message).joinToString("\u0000")

    override fun compareTo(other: Reason): Int = key.compareTo(other.key)

    override fun toJson(): Any = linkedMapOf("code" to code, "message" to message, "ruleId" to ruleId, "contextId" to contextId)
}

internal data class Span(val startLine: Int, val startColumn: Int, val endLine: Int, val endColumn: Int) : JsonValue {
    override fun toJson(): Any =
        linkedMapOf("startLine" to startLine, "startColumn" to startColumn, "endLine" to endLine, "endColumn" to endColumn)
}

internal data class DecisionEvidence(val decisionKind: String, val line: Int) : JsonValue {
    override fun toJson(): Any = linkedMapOf("decisionKind" to decisionKind, "line" to line)
}

internal data class NestingEvidence(val constructKind: String, val line: Int, val depth: Int) : JsonValue {
    override fun toJson(): Any = linkedMapOf("constructKind" to constructKind, "line" to line, "depth" to depth)
}

internal data class MethodFact(
    val unitId: String,
    val name: String,
    val span: Span,
    val complexity: Int,
    val nesting: Int,
    val codeLines: Int,
    val parameterCount: Int,
    val decisions: List<DecisionEvidence>,
    val nestingPoints: List<NestingEvidence>,
) : JsonValue {
    override fun toJson(): Any = linkedMapOf(
        "unitId" to unitId,
        "name" to name,
        "span" to span,
        "complexity" to complexity,
        "nesting" to nesting,
        "codeLines" to codeLines,
        "parameterCount" to parameterCount,
        "decisions" to decisions,
        "nestingPoints" to nestingPoints,
    )
}

internal data class FileFact(val path: String, val codeLines: Int, val generated: Boolean, val methods: List<MethodFact>) : JsonValue {
    override fun toJson(): Any = linkedMapOf("path" to path, "codeLines" to codeLines, "generated" to generated, "methods" to methods)
}

internal data class CohesionNode(val id: String, val name: String) : JsonValue {
    override fun toJson(): Any = linkedMapOf("id" to id, "name" to name)
}

internal data class CohesionEdge(val from: String, val to: String, val reason: String) : JsonValue {
    override fun toJson(): Any = linkedMapOf("from" to from, "to" to to, "reason" to reason)
}

internal data class ClassLocation(val file: String, val span: Span) : JsonValue {
    override fun toJson(): Any = linkedMapOf("file" to file, "span" to span)
}

internal data class ClassFact(
    val unitId: String,
    val name: String,
    val locations: List<ClassLocation>,
    val applicable: Boolean,
    val nodes: List<CohesionNode>,
    val edges: List<CohesionEdge>,
    val components: List<List<String>>,
) : JsonValue {
    override fun toJson(): Any = linkedMapOf(
        "unitId" to unitId,
        "name" to name,
        "locations" to locations,
        "applicable" to applicable,
        "nodes" to nodes,
        "edges" to edges,
        "components" to components,
    )
}

internal data class TypeFact(val unitId: String, val file: String, val isAbstract: Boolean) : JsonValue {
    override fun toJson(): Any = linkedMapOf("unitId" to unitId, "file" to file, "isAbstract" to isAbstract)
}

internal data class DiagnosticFact(val analyzerId: String, val file: String, val span: Span, val message: String, val unitId: String) :
    JsonValue {
    override fun toJson(): Any =
        linkedMapOf("analyzerId" to analyzerId, "file" to file, "span" to span, "message" to message, "unitId" to unitId)
}

internal data class ContextFacts(
    val contextId: String,
    val configPath: String,
    val modulePath: String?,
    val ownedFiles: List<String>,
    val files: List<FileFact>,
    val classes: List<ClassFact>,
    val types: List<TypeFact>,
    val diagnostics: List<DiagnosticFact>,
    val failures: List<Reason>,
) : JsonValue {
    override fun toJson(): Any = linkedMapOf(
        "contextId" to contextId,
        "language" to LANGUAGE,
        "configPath" to configPath,
        "framework" to null,
        "modulePath" to modulePath,
        "ownedFiles" to ownedFiles,
        "files" to files,
        "classes" to classes,
        "types" to types,
        "diagnostics" to diagnostics,
        "failures" to failures,
    )
}

internal data class ModuleEdge(val from: String, val to: String) : JsonValue, Comparable<ModuleEdge> {
    override fun compareTo(other: ModuleEdge): Int {
        val byFrom = from.compareTo(other.from)
        return if (byFrom != 0) byFrom else to.compareTo(other.to)
    }

    /** Gradle project dependencies are always runtime edges; Kotlin has no type-only dependency form. */
    override fun toJson(): Any = linkedMapOf("from" to from, "to" to to, "typeOnly" to false)
}

internal data class ContextGraph(val contextId: String, val edges: List<ModuleEdge>) : JsonValue {
    override fun toJson(): Any =
        linkedMapOf("contextId" to contextId, "edges" to edges, "fileImports" to emptyList<Any>(), "support" to emptyList<Any>())
}

internal data class ModuleGraphFacts(
    val modules: List<String>,
    val edges: List<ModuleEdge>,
    val unresolved: List<Reason>,
    val contexts: List<ContextGraph>,
) : JsonValue {
    override fun toJson(): Any = linkedMapOf(
        "modules" to modules,
        "edges" to edges,
        "fileImports" to emptyList<Any>(),
        "unresolved" to unresolved,
        "contexts" to contexts,
    )
}

internal data class InputRecord(val scope: String, val path: String, val sha256: String) : JsonValue {
    override fun toJson(): Any = linkedMapOf("scope" to scope, "path" to path, "sha256" to sha256)
}

internal data class LanguageAnalysis(
    val analyzerVersions: Map<String, String>,
    val contexts: List<ContextFacts>,
    val graph: ModuleGraphFacts,
    val inputs: List<InputRecord>,
    val unownedFiles: List<String>,
    val failures: List<Reason>,
) : JsonValue {
    override fun toJson(): Any = linkedMapOf(
        "language" to LANGUAGE,
        "analyzerVersions" to analyzerVersions,
        "contexts" to contexts,
        "graph" to graph,
        "inputs" to inputs,
        "unownedFiles" to unownedFiles,
        "failures" to failures,
    )
}

internal data class Token(
    val kind: String,
    val text: String,
    val line: Int,
    val column: Int,
    val endLine: Int,
    val endColumn: Int,
    val offset: Int,
) : JsonValue {
    override fun toJson(): Any = linkedMapOf(
        "kind" to kind,
        "text" to text,
        "line" to line,
        "column" to column,
        "endLine" to endLine,
        "endColumn" to endColumn,
        "offset" to offset,
    )
}

internal data class FileTokens(val path: String, val tokens: List<Token>) : JsonValue {
    override fun toJson(): Any = linkedMapOf("path" to path, "tokens" to tokens)
}

internal data class TokenizeResult(val files: List<FileTokens>, val failures: List<Reason>) : JsonValue {
    override fun toJson(): Any = linkedMapOf("files" to files, "failures" to failures)
}

/** A pinned correctness rule: the detekt rule ID, the CodeShield evaluator ID and the explicit rule options. */
internal data class CorrectnessRuleRequest(val analyzerId: String, val evaluatorId: String, val options: Map<String, Any?>)

internal data class AnalyzeRequest(
    val root: String,
    val files: List<String>,
    val focusFiles: List<String>,
    val changedConfigFiles: List<String>,
    val isBaseline: Boolean,
    val correctnessRules: List<CorrectnessRuleRequest>,
)

internal data class TokenizeRequest(val root: String, val files: List<String>)
