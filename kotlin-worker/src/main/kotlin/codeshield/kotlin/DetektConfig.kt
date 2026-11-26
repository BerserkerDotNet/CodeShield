package codeshield.kotlin

import io.gitlab.arturbosch.detekt.api.Config

/**
 * The configuration detekt rules read in place of a detekt.yml: the requested rule is active, never auto-corrects, and
 * reads the pinned options from the correctness request. Every other key yields the rule's own default, so a user's
 * detekt configuration never changes what CodeShield measures. `excludes`, `includes` and `severity` are absent.
 */
internal class DetektConfig private constructor(
    private val path: String,
    options: Map<String, Any?>,
    private val rule: Pair<String, Map<String, Any?>>?,
) : PinnedValues(options) {
    override val parentPath: String? get() = path

    override fun subConfig(key: String): Config =
        DetektConfig("$path$SEPARATOR$key", rule?.takeIf { it.first == key }?.second.orEmpty(), null)

    companion object {
        private const val SEPARATOR = ">"

        /** The rule-set configuration; its parent path is the rule set ID, which detekt uses for suppression IDs. */
        fun forRule(ruleSetId: String, ruleId: String, options: Map<String, Any?>): Config =
            DetektConfig(ruleSetId, emptyMap(), ruleId to options)
    }
}

/** The value lookups of a [DetektConfig] level: fixed activation keys, then the pinned options, then the rule default. */
internal abstract class PinnedValues(private val options: Map<String, Any?>) : Config {
    @Suppress("UNCHECKED_CAST")
    override fun <T : Any> valueOrDefault(key: String, default: T): T = (fixed(key) ?: option(key, default) ?: default) as T

    @Suppress("UNCHECKED_CAST")
    override fun <T : Any> valueOrNull(key: String): T? = (fixed(key) ?: options[key].takeUnless { key in absent }) as T?

    private fun fixed(key: String): Boolean? = when (key) {
        ACTIVE -> true
        AUTO_CORRECT -> false
        else -> null
    }

    private fun option(key: String, default: Any): Any? = options[key]?.let { OptionValues.convert(it, default) }

    private companion object {
        const val ACTIVE = "active"
        const val AUTO_CORRECT = "autoCorrect"
        val absent = setOf("excludes", "includes", "severity")
    }
}

/** Converts a JSON option value to the type of the rule's default, or null when it cannot be represented. */
internal object OptionValues {
    fun convert(value: Any, default: Any): Any? = when (default) {
        is String -> value as? String
        is Boolean -> value as? Boolean
        is Number -> (value as? Number)?.let { number(it, default) }
        is List<*> -> (value as? List<*>)?.takeIf { list -> list.all { it is String } }
        else -> null
    }

    private fun number(value: Number, default: Number): Number? = when (default) {
        is Int -> value.takeIf { it is Int }
        is Long -> value.takeIf { it is Int || it is Long }?.toLong()
        is Double -> value.toDouble()
        else -> null
    }
}
