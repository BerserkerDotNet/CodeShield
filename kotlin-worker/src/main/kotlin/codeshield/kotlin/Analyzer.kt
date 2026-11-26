package codeshield.kotlin

/**
 * Analyzes one snapshot: Gradle builds define the modules and contexts, Kotlin sources are measured with the PSI parser
 * of the user's detekt-cli, and the pinned detekt rules supply correctness diagnostics. Gradle is never executed.
 */
internal object Analyzer {
    fun analyze(request: AnalyzeRequest): LanguageAnalysis {
        val ledger = InputLedger(RepoPaths(request.root))
        val parser = KotlinParser(request.root)
        val scripts = ScriptSource(ledger, GradleScripts(parser))
        val model = GradleModelBuilder(request.files.toSet(), scripts).build(ordinalSorted(request.files.filter(::isSource)))
        val focus = request.focusFiles.filter(::isSource).toSet()
        val changed = request.changedConfigFiles.toSet()
        val analysis = ContextAnalysis(SourceLoader(ledger, parser), focus, request.correctnessRules)
        val contexts = ContextPlan.all(model).filter { it.selected(focus, changed) }.map(analysis::analyze)
        return LanguageAnalysis(
            analyzerVersions = Versions.all(),
            contexts = contexts,
            graph = ModuleGraphFacts(model.modules, model.edges, model.unresolved, listOf(ContextGraph(ContextPlan.MAIN, model.edges))),
            inputs = ledger.snapshot(),
            unownedFiles = ordinalSorted(focus.filter { it !in model.owners }),
            failures = ledger.readFailures().map { (path, message) ->
                Reason(ReasonCodes.UNREADABLE_SOURCE, "Cannot read $path: $message", null, path)
            },
        )
    }

    /** Kotlin sources; the adapter already left out build output, so every `.kt` file in the request is a source. */
    private fun isSource(path: String): Boolean = path.endsWith(".kt", ignoreCase = true)
}