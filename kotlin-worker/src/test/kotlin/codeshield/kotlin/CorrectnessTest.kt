package codeshield.kotlin

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.DynamicTest
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestFactory
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Path

/** Every pinned detekt rule fires on its trigger and stays silent on the corrected code, with no other rule firing. */
class CorrectnessTest {
    @Test
    fun `every pinned rule has a sample`() {
        assertEquals(PinnedRules.ids.sorted(), SAMPLES.keys.sorted())
        assertEquals(emptyList<Any>(), analysis.failures(), "no rule is missing and no file fails to parse")
    }

    @TestFactory
    fun `each rule fires on its trigger only`(): List<DynamicTest> = SAMPLES.keys.map { rule ->
        DynamicTest.dynamicTest(rule) {
            val found = analysis.diagnostics(trigger(rule))
            assertEquals(setOf(rule), found.map { it["analyzerId"] }.toSet(), "diagnostics = $found")
            for (diagnostic in found) {
                assertTrue((diagnostic["unitId"] as String).startsWith(trigger(rule) + "#"), "unitId = ${diagnostic["unitId"]}")
                assertTrue((diagnostic["message"] as String).isNotBlank())
            }
        }
    }

    @TestFactory
    fun `each rule is silent on the corrected code`(): List<DynamicTest> = SAMPLES.keys.map { rule ->
        DynamicTest.dynamicTest(rule) { assertEquals(emptyList<Any>(), analysis.diagnostics(clean(rule))) }
    }

    @Test
    fun `diagnostics cover every file of a selected context, not only the changed ones`(@TempDir scratch: Path) {
        val focused = Repo(scratch.resolve("focus"))
            .file("build.gradle.kts", "")
            .file("src/main/kotlin/Changed.kt", "fun changed() = 1")
            .file("src/main/kotlin/Old.kt", "fun old() { System.gc() }")
            .analyze(focus = listOf("src/main/kotlin/Changed.kt"))

        assertEquals(listOf("src/main/kotlin/Old.kt"), focused.diagnostics().map { it["file"] })
        assertEquals(listOf("src/main/kotlin/Changed.kt"), focused.contexts.flatMap { Analysis.list(it, "files") }.map { it["path"] })
    }

    @Test
    fun `suppressing a pinned rule is reported, in every form detekt accepts`(@TempDir scratch: Path) {
        val suppressed = Repo(scratch.resolve("suppressed"))
            .file("build.gradle.kts", "")
            .file("src/main/kotlin/Forms.kt", FORMS)
            .file("src/main/kotlin/RuleSet.kt", "@file:Suppress(\"potential-bugs\")\npackage s\n\nfun loop() { for (i in 2..1) println(i) }")
            .file("src/main/kotlin/All.kt", "package s\n\n@Suppress(\"ALL\")\nfun everything() = Unit")
            .analyze()

        assertEquals(
            setOf("UselessPostfixExpression", "InvalidRange", "ExplicitGarbageCollectionCall", "ThrowingExceptionFromFinally"),
            suppressedRules(suppressed, "src/main/kotlin/Forms.kt"),
        )
        assertEquals(
            PinnedRules.ids.toSet() - setOf("ExceptionRaisedInUnexpectedLocation", "ThrowingExceptionFromFinally"),
            suppressedRules(suppressed, "src/main/kotlin/RuleSet.kt"),
            "potential-bugs disables every pinned rule of that rule set",
        )
        assertEquals(PinnedRules.ids.toSet(), suppressedRules(suppressed, "src/main/kotlin/All.kt"))
        val reason = suppressed.failures(ReasonCodes.SUPPRESSED_RULE)
            .single { (it["message"] as String).startsWith("src/main/kotlin/Forms.kt:") && (it["message"] as String).contains("InvalidRange") }
        assertEquals("src/main/kotlin/Forms.kt:8 disables InvalidRange, so the rule cannot be assessed.", reason["message"])
        assertEquals(PinnedRules.evaluatorId("InvalidRange"), reason["ruleId"])
        assertEquals("build.gradle.kts|main", reason["contextId"])
        assertEquals(emptyList<Any>(), suppressed.diagnostics(), "detekt honors the suppressions itself")
    }

    @Test
    fun `rules that are not pinned correctness rules of this detekt-cli are missing analyzers`(@TempDir scratch: Path) {
        val requested = listOf("NotARule", "MagicNumber", "bad.Id", "InvalidRange").map { PinnedRules.rule(it) }

        val result = Repo(scratch.resolve("missing"))
            .file("build.gradle.kts", "")
            .file("src/main/kotlin/Loop.kt", "fun loop() { for (i in 2..1) println(i) }")
            .analyze(rules = requested)

        val missing = result.failures(ReasonCodes.MISSING_ANALYZER)
        assertEquals(
            listOf("bad.Id", "MagicNumber", "NotARule").map { "Analyzer $it is not installed." },
            missing.map { it["message"] }.sortedBy { (it as String).lowercase() },
        )
        assertEquals(setOf("build.gradle.kts|main"), missing.map { it["contextId"] }.toSet())
        assertEquals(listOf("InvalidRange"), result.diagnostics().map { it["analyzerId"] })
    }

    @Test
    fun `rule options come from the request`(@TempDir scratch: Path) {
        val repo = Repo(scratch.resolve("options")).file("build.gradle.kts", "")
        repo.file("src/main/kotlin/Text.kt", SAMPLES.getValue("ExceptionRaisedInUnexpectedLocation").first)

        val pinned = repo.analyze(rules = listOf(PinnedRules.rule("ExceptionRaisedInUnexpectedLocation")))
        val narrowed = repo.analyze(
            rules = listOf(PinnedRules.rule("ExceptionRaisedInUnexpectedLocation", mapOf("methodNames" to listOf("equals")))),
        )

        assertEquals(1, pinned.diagnostics().size)
        assertEquals(emptyList<Any>(), narrowed.diagnostics())
    }

    private companion object {
        val analysis: Analysis by lazy {
            analyzeOnce("codeshield-correctness") {
                file("build.gradle.kts", """plugins { kotlin("jvm") }""")
                for ((rule, sample) in SAMPLES) {
                    file(trigger(rule), "package r\n\n" + sample.first.trimIndent())
                    file(clean(rule), "package r\n\n" + sample.second.trimIndent())
                }
            }
        }

        fun suppressedRules(result: Analysis, file: String): Set<String> =
            result.failures(ReasonCodes.SUPPRESSED_RULE)
                .filter { (it["message"] as String).startsWith("$file:") }
                .map { reason -> PinnedRules.ids.single { PinnedRules.evaluatorId(it) == reason["ruleId"] } }
                .toSet()

        fun trigger(rule: String) = "src/main/kotlin/r/${rule}Trigger.kt"

        fun clean(rule: String) = "src/main/kotlin/r/${rule}Clean.kt"

        val FORMS = """
            package s

            class Forms {
                private var counter = 0

                @Suppress("UselessPostfixExpression")
                fun bump() { counter = counter++ }
                @Suppress("detekt:InvalidRange")
                fun loop() { for (i in 2..1) println(i) }

                @Suppress("detekt.ExplicitGarbageCollectionCall")
                fun collect() { System.gc() }

                @SuppressWarnings("exceptions:ThrowingExceptionFromFinally")
                fun risky() { try { println() } finally { throw IllegalStateException() } }

                @Suppress("UNUSED_PARAMETER", "MagicNumber")
                fun unrelated(x: Int) = 42
            }
        """

        /** Rule ID to (trigger, corrected) Kotlin code. */
        val SAMPLES: Map<String, Pair<String, String>> = linkedMapOf(
            "EqualsAlwaysReturnsTrueOrFalse" to (
                """
                class AlwaysEqual {
                    override fun equals(other: Any?): Boolean {
                        return true
                    }
                    override fun hashCode(): Int = 1
                }
                """ to """
                class SometimesEqual {
                    override fun equals(other: Any?): Boolean {
                        return other is SometimesEqual
                    }
                    override fun hashCode(): Int = 1
                }
                """
                ),
            "EqualsWithHashCodeExist" to (
                """
                class OnlyEquals {
                    override fun equals(other: Any?): Boolean = other is OnlyEquals
                }
                """ to """
                class BothOverridden {
                    override fun equals(other: Any?): Boolean = other is BothOverridden
                    override fun hashCode(): Int = 7
                }
                """
                ),
            "ExceptionRaisedInUnexpectedLocation" to (
                """
                class Text {
                    override fun toString(): String {
                        throw IllegalStateException("no text")
                    }
                }
                """ to """
                class Describer {
                    fun describe(): String {
                        throw IllegalStateException("not yet")
                    }
                }
                """
                ),
            "ExplicitGarbageCollectionCall" to (
                """
                fun collectGarbage() {
                    System.gc()
                }
                """ to """
                fun clearCache(cache: MutableList<Int>) {
                    cache.clear()
                }
                """
                ),
            "InvalidRange" to (
                """
                fun countDown() {
                    for (i in 10..1) println(i)
                }
                """ to """
                fun countUp() {
                    for (i in 1..10) println(i)
                    for (i in 10 downTo 1) println(i)
                }
                """
                ),
            "IteratorHasNextCallsNextMethod" to (
                """
                class Peeking(private var left: Int) : Iterator<Int> {
                    override fun hasNext(): Boolean {
                        next()
                        return left > 0
                    }
                    override fun next(): Int {
                        if (left <= 0) throw NoSuchElementException()
                        left -= 1
                        return left
                    }
                }
                """ to """
                class Counting(private var left: Int) : Iterator<Int> {
                    override fun hasNext(): Boolean = left > 0
                    override fun next(): Int {
                        if (left <= 0) throw NoSuchElementException()
                        left -= 1
                        return left
                    }
                }
                """
                ),
            "IteratorNotThrowingNoSuchElementException" to (
                """
                class Endless : Iterator<Int> {
                    override fun hasNext(): Boolean = true
                    override fun next(): Int = 1
                }
                """ to """
                class Bounded(private var left: Int) : Iterator<Int> {
                    override fun hasNext(): Boolean = left > 0
                    override fun next(): Int {
                        if (left <= 0) throw NoSuchElementException()
                        left -= 1
                        return left
                    }
                }
                """
                ),
            "ThrowingExceptionFromFinally" to (
                """
                fun risky(): Int {
                    try {
                        return 1
                    } finally {
                        throw IllegalStateException("boom")
                    }
                }
                """ to """
                fun careful(): Int {
                    try {
                        return 1
                    } finally {
                        println("done")
                    }
                }
                """
                ),
            "UnconditionalJumpStatementInLoop" to (
                """
                fun first(values: List<Int>): Int {
                    for (v in values) {
                        return v
                    }
                    return 0
                }
                """ to """
                fun firstPositive(values: List<Int>): Int {
                    for (v in values) {
                        if (v > 0) return v
                    }
                    return 0
                }
                """
                ),
            "UselessPostfixExpression" to (
                """
                class LostIncrement {
                    private var counter = 0
                    fun bump() {
                        counter = counter++
                    }
                }
                """ to """
                class KeptIncrement {
                    private var counter = 0
                    fun bump() {
                        counter++
                    }
                }
                """
                ),
            "WrongEqualsTypeParameter" to (
                """
                class Name(private val value: String) {
                    fun equals(other: String): Boolean = value == other
                }
                """ to """
                class Label(private val value: String) {
                    fun matches(other: String): Boolean = value == other
                }
                """
                ),
        )
    }
}
