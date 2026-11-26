package codeshield.kotlin

import io.gitlab.arturbosch.detekt.api.Config
import io.gitlab.arturbosch.detekt.api.Rule
import org.jetbrains.kotlin.com.intellij.psi.util.PsiTreeUtil
import org.jetbrains.kotlin.psi.KtAnnotationEntry
import org.jetbrains.kotlin.psi.ValueArgument

/** A detekt rule instantiated from the user's detekt-cli installation for one correctness request. */
internal class LoadedRule(val request: CorrectnessRuleRequest, ruleSetId: String, val rule: Rule) {
    /** The `@Suppress` arguments that disable this rule, as detekt accepts them (after removing `detekt.`/`detekt:`). */
    val suppressionIds: Set<String> = buildSet {
        val id = request.analyzerId
        addAll(listOf(id, "ALL", "all", "All", ruleSetId, "$ruleSetId.$id", "$ruleSetId:$id"))
        addAll(rule.aliases)
    }
}

/** Loads pinned rules by class name from the detekt rule sets that hold correctness rules. */
internal object DetektRules {
    private val ruleSets = listOf("bugs" to "potential-bugs", "exceptions" to "exceptions")
    private val identifier = Regex("[A-Z][A-Za-z0-9]*")

    /** The rule, or null when this detekt installation does not provide it. */
    fun load(request: CorrectnessRuleRequest): LoadedRule? {
        if (!identifier.matches(request.analyzerId)) return null
        return ruleSets.firstNotNullOfOrNull { (pkg, ruleSetId) -> instantiate(request, pkg, ruleSetId) }
    }

    private fun instantiate(request: CorrectnessRuleRequest, pkg: String, ruleSetId: String): LoadedRule? {
        val type = ruleClass("io.gitlab.arturbosch.detekt.rules.$pkg.${request.analyzerId}") ?: return null
        val rule = construct(type, DetektConfig.forRule(ruleSetId, request.analyzerId, request.options)) ?: return null
        return if (rule.ruleId == request.analyzerId) LoadedRule(request, ruleSetId, rule) else null
    }

    private fun ruleClass(name: String): Class<*>? = try {
        Class.forName(name, true, DetektRules::class.java.classLoader).takeIf { Rule::class.java.isAssignableFrom(it) }
    } catch (missing: ClassNotFoundException) {
        null
    } catch (broken: LinkageError) {
        null
    }

    /** Every pinned rule has a `(Config)` constructor; a rule without one is treated as not installed. */
    private fun construct(type: Class<*>, config: Config): Rule? = try {
        type.getConstructor(Config::class.java).newInstance(config) as Rule
    } catch (failed: ReflectiveOperationException) {
        null
    } catch (broken: LinkageError) {
        null
    }
}

/** SUPPRESSED_RULE failures for loaded rules that `@Suppress` or `@SuppressWarnings` disables in a file. */
internal object Suppressions {
    private val annotations = setOf("Suppress", "kotlin.Suppress", "SuppressWarnings", "java.lang.SuppressWarnings")
    private val detektPrefix = Regex("detekt[:.]")

    fun find(source: SourceUnit, rules: List<LoadedRule>, contextId: String): List<Reason> =
        PsiTreeUtil.findChildrenOfType(source.parsed.file, KtAnnotationEntry::class.java)
            .filter { it.typeReference?.text in annotations }
            .flatMap { entry -> reasons(source, entry, rules, contextId) }

    private fun reasons(source: SourceUnit, entry: KtAnnotationEntry, rules: List<LoadedRule>, contextId: String): List<Reason> {
        val ids = entry.valueArguments.mapNotNull(::suppressionId).toSet()
        val line = source.parsed.lines.line(entry)
        return rules.filter { rule -> rule.suppressionIds.any(ids::contains) }.map { rule ->
            val message = "${source.path}:$line disables ${rule.request.analyzerId}, so the rule cannot be assessed."
            Reason(ReasonCodes.SUPPRESSED_RULE, message, rule.request.evaluatorId, contextId)
        }
    }

    private fun suppressionId(argument: ValueArgument): String? =
        argument.getArgumentExpression()?.text?.replace("\"", "")?.replace(detektPrefix, "")
}
