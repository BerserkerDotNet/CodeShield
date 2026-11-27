package codeshield.kotlin

import io.gitlab.arturbosch.detekt.api.Finding

internal class CorrectnessOutcome(val diagnostics: List<DiagnosticFact>, val failures: List<Reason>)

/**
 * Runs the pinned detekt rules over the files of one context. Rules run on syntax alone (no type resolution), exactly
 * as `detekt` does without a classpath, and with CodeShield's pinned options rather than the repository's detekt.yml.
 */
internal class Correctness(private val contextId: String, private val configPath: String) {
    fun run(sources: List<SourceUnit>, requests: List<CorrectnessRuleRequest>): CorrectnessOutcome {
        val failures = ArrayList<Reason>()
        val rules = ArrayList<LoadedRule>()
        for (request in requests) DetektRules.load(request)?.let(rules::add) ?: failures.add(missing(request))
        for (source in sources) failures.addAll(Suppressions.find(source, rules, contextId))
        val diagnostics = ArrayList<DiagnosticFact>()
        for (rule in rules) apply(rule, sources, diagnostics)?.let(failures::add)
        return CorrectnessOutcome(diagnostics.sortedWith(order), failures)
    }

    /** Runs one rule over every file; a rule that throws is reported once and stops, so it is never a silent pass. */
    private fun apply(rule: LoadedRule, sources: List<SourceUnit>, into: MutableList<DiagnosticFact>): Reason? {
        for (source in sources) {
            val error = RuleVisit.visit(rule, source, into) ?: continue
            val message = "detekt could not analyze $configPath: ${rule.request.analyzerId} failed on ${source.path}: $error"
            return Reason(ReasonCodes.PROJECT_LOAD_FAILED, message, rule.request.evaluatorId, contextId)
        }
        return null
    }

    private fun missing(request: CorrectnessRuleRequest): Reason =
        Reason(ReasonCodes.MISSING_ANALYZER, "Analyzer ${request.analyzerId} is not installed.", request.evaluatorId, contextId)

    private companion object {
        val order = compareBy<DiagnosticFact>({ it.file }, { it.span.startLine }, { it.span.startColumn }, { it.analyzerId })
    }
}

/** Runs one loaded rule over one file and converts its findings into diagnostics. */
private object RuleVisit {
    /** The error the rule threw, or null when it ran; its findings are added to [into]. */
    fun visit(rule: LoadedRule, source: SourceUnit, into: MutableList<DiagnosticFact>): Throwable? = try {
        rule.rule.visitFile(source.parsed.file)
        rule.rule.findings.forEach { finding -> into.add(diagnostic(rule, source, finding)) }
        null
    } catch (error: Exception) {
        error
    } catch (error: StackOverflowError) {
        error
    } catch (error: LinkageError) {
        error
    }

    private fun diagnostic(rule: LoadedRule, source: SourceUnit, finding: Finding): DiagnosticFact {
        val text = finding.location.text
        val start = source.code.firstCode(text.start, text.end) ?: text.start
        return DiagnosticFact(
            analyzerId = rule.request.analyzerId,
            file = source.path,
            span = source.parsed.lines.span(start, text.end),
            message = finding.messageOrDescription(),
            unitId = source.units.enclosingStableUnitId(start, CohesionClasses::isEligible),
        )
    }
}
