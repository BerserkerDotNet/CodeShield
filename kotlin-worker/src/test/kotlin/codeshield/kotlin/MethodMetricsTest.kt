package codeshield.kotlin

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** Conformance of the per-unit metrics with docs/specification.md, including the values on both sides of each default limit. */
class MethodMetricsTest {
    @Test
    fun `parameter count is the declared value parameters, at and above the default limit of 5`() {
        assertEquals(5, analysis.method(PARAMS, "five(Int,Int,Int,Int,Int)").int("parameterCount"))
        assertEquals(6, analysis.method(PARAMS, "six(Int,Int,Int,Int,Int,Int)").int("parameterCount"))
        val extension = analysis.method(PARAMS, "String.shout(Int)")
        assertEquals(1, extension.int("parameterCount"), "the extension receiver is not a parameter")
        assertEquals("shout", extension["name"])
    }

    @Test
    fun `complexity counts if, at and above the default limit of 6`() {
        val six = analysis.method(COMPLEXITY, "six(Int)")
        assertEquals(6, six.int("complexity"))
        assertEquals(listOf(4, 5, 6, 7, 8), decisions(six).map { it.second })
        assertEquals(7, analysis.method(COMPLEXITY, "seven(Int)").int("complexity"))
    }

    @Test
    fun `complexity counts elvis, logical operators, when entries other than else, and catch`() {
        val branches = analysis.method(COMPLEXITY, "branches(Int?,Boolean,Boolean)")

        assertEquals(8, branches.int("complexity"))
        assertEquals(
            listOf("logical" to 23, "if" to 24, "logical" to 24, "logical" to 24, "case" to 27, "case" to 28, "catch" to 31),
            decisions(branches),
        )
    }

    @Test
    fun `nesting counts if, loops, when and catch, at and above the default limit of 2`() {
        val two = analysis.method(NESTING, "two(List<Int>)")
        assertEquals(2, two.int("nesting"))
        assertEquals(listOf(Triple("loop", 4, 1), Triple("if", 5, 2)), nesting(two))

        val three = analysis.method(NESTING, "three(List<Int>)")
        assertEquals(3, three.int("nesting"))
        assertEquals(listOf(Triple("loop", 10, 1), Triple("if", 11, 2), Triple("loop", 12, 3)), nesting(three))
    }

    @Test
    fun `an else-if chain stays at one level while each if is a decision`() {
        val chain = analysis.method(NESTING, "chain(Int)")

        assertEquals(1, chain.int("nesting"))
        assertEquals(4, chain.int("complexity"))
        assertEquals(listOf(Triple("if", 18, 1), Triple("if", 20, 1), Triple("if", 22, 1)), nesting(chain))
    }

    @Test
    fun `when entries are one level deeper than the when`() {
        val inWhen = analysis.method(NESTING, "whenNesting(Int)")

        assertEquals(2, inWhen.int("nesting"))
        assertEquals(listOf(Triple("when", 30, 1), Triple("if", 31, 2)), nesting(inWhen))
        assertEquals(3, inWhen.int("complexity"))
    }

    @Test
    fun `init blocks, secondary constructors, accessors, lambdas and local functions are their own units`() {
        val methods = analysis.methods(UNITS).mapValues { (_, fact) -> fact["name"] }

        assertEquals(
            mapOf(
                "$UNITS#Holder.<init>" to "<init>",
                "$UNITS#Holder.constructor(Int)" to "constructor",
                "$UNITS#Holder.positive.get" to "positive.get",
                "$UNITS#Holder.sum()" to "sum",
                "$UNITS#Holder.sum().<lambda>@19" to "<lambda>",
                "$UNITS#Holder.sum().handler@20" to "handler",
                "$UNITS#Holder.sum().local(Int)@21" to "local",
                "$UNITS#Holder.Companion.create()" to "create",
            ),
            methods,
        )
        assertEquals(2, analysis.method(UNITS, "Holder.<init>").int("complexity"))
        assertEquals(2, analysis.method(UNITS, "Holder.positive.get").int("complexity"))
        assertEquals(1, analysis.method(UNITS, "Holder.sum()").int("complexity"), "the lambda's if belongs to the lambda")
        assertEquals(2, analysis.method(UNITS, "Holder.sum().<lambda>@19").int("complexity"))
        assertEquals(1, analysis.method(UNITS, "Holder.sum().handler@20").int("parameterCount"))
        assertEquals(1, analysis.method(UNITS, "Holder.constructor(Int)").int("parameterCount"))
    }

    @Test
    fun `method length counts lines with code, at and above the default limit of 40`() {
        assertEquals(40, analysis.method(LENGTH, "forty()").int("codeLines"))
        assertEquals(41, analysis.method(LENGTH, "fortyOne()").int("codeLines"))
        val span = analysis.method(LENGTH, "forty()")["span"] as Map<*, *>
        assertEquals(4, span.int("startLine"), "the span starts at the first code token, after the KDoc")
    }

    @Test
    fun `file length counts lines with code, at and above the default limit of 400`() {
        assertEquals(400, analysis.fileFact(FILE_400).int("codeLines"))
        assertEquals(401, analysis.fileFact(FILE_401).int("codeLines"))
    }

    @Test
    fun `a generator marker in the first ten lines marks the file as generated`() {
        assertTrue(analysis.fileFact(GENERATED)["generated"] as Boolean)
        assertFalse(analysis.fileFact(LATE_MARKER)["generated"] as Boolean)
        assertFalse(analysis.fileFact(PARAMS)["generated"] as Boolean)
    }

    private fun decisions(method: Map<*, *>): List<Pair<String, Int>> =
        Analysis.list(method, "decisions").map { it["decisionKind"] as String to it.int("line") }

    private fun nesting(method: Map<*, *>): List<Triple<String, Int, Int>> =
        Analysis.list(method, "nestingPoints").map { Triple(it["constructKind"] as String, it.int("line"), it.int("depth")) }

    private companion object {
        const val PARAMS = "src/main/kotlin/m/Params.kt"
        const val COMPLEXITY = "src/main/kotlin/m/Complexity.kt"
        const val NESTING = "src/main/kotlin/m/Nesting.kt"
        const val UNITS = "src/main/kotlin/m/Units.kt"
        const val LENGTH = "src/main/kotlin/m/Length.kt"
        const val FILE_400 = "src/main/kotlin/m/File400.kt"
        const val FILE_401 = "src/main/kotlin/m/File401.kt"
        const val GENERATED = "src/main/kotlin/m/Generated.kt"
        const val LATE_MARKER = "src/main/kotlin/m/Late.kt"

        val analysis: Analysis by lazy {
            analyzeOnce("codeshield-metrics") {
                file("build.gradle.kts", """plugins { kotlin("jvm") }""")
                file(PARAMS, PARAMS_TEXT)
                file(COMPLEXITY, COMPLEXITY_TEXT)
                file(NESTING, NESTING_TEXT)
                file(UNITS, UNITS_TEXT)
                file(LENGTH, lengthText())
                file(FILE_400, fileOf(400))
                file(FILE_401, fileOf(401))
                file(GENERATED, "// Code generated by protoc. <auto-generated>\npackage m\n\nobject Generated")
                file(LATE_MARKER, "package m\n" + "\n".repeat(10) + "// @generated\nobject Late")
            }
        }

        val PARAMS_TEXT = """
            package m

            fun five(a: Int, b: Int, c: Int, d: Int, e: Int): Int = a + b + c + d + e

            fun six(a: Int, b: Int, c: Int, d: Int, e: Int, f: Int): Int = a + b + c + d + e + f

            fun String.shout(times: Int): String = repeat(times)
        """

        val COMPLEXITY_TEXT = """
            package m

            fun six(x: Int): Int {
                if (x > 0) return 1
                if (x > 1) return 2
                if (x > 2) return 3
                if (x > 3) return 4
                if (x > 4) return 5
                return 0
            }

            fun seven(x: Int): Int {
                if (x > 0) return 1
                if (x > 1) return 2
                if (x > 2) return 3
                if (x > 3) return 4
                if (x > 4) return 5
                if (x > 5) return 6
                return 0
            }

            fun branches(x: Int?, flag: Boolean, other: Boolean): Int {
                val y = x ?: 0
                if (flag && other || y > 3) return 1
                try {
                    return when (y) {
                        1 -> 10
                        2, 3 -> 20
                        else -> 30
                    }
                } catch (e: IllegalStateException) {
                    return -1
                }
            }
        """

        val NESTING_TEXT = """
            package m

            fun two(items: List<Int>) {
                for (i in items) {
                    if (i > 0) println(i)
                }
            }

            fun three(items: List<Int>) {
                for (i in items) {
                    if (i > 0) {
                        while (i > 10) println(i)
                    }
                }
            }

            fun chain(x: Int): String {
                if (x == 0) {
                    return "zero"
                } else if (x == 1) {
                    return "one"
                } else if (x == 2) {
                    return "two"
                } else {
                    return "many"
                }
            }

            fun whenNesting(x: Int): Int {
                when (x) {
                    1 -> if (x > 0) return 1
                    else -> return 0
                }
                return 2
            }
        """

        val UNITS_TEXT = """
            package m

            class Holder(private val values: List<Int>) {
                private var total = 0

                init {
                    if (values.isEmpty()) total = -1
                }

                constructor(single: Int) : this(listOf(single)) { total = single }

                val positive: Int
                    get() {
                        if (total > 0) return total
                        return 0
                    }

                fun sum(): Int {
                    values.forEach { if (it > 0) total += it }
                    val handler = { x: Int -> x * 2 }
                    fun local(y: Int): Int = y + 1
                    return handler(local(total))
                }

                companion object {
                    fun create(): Holder = Holder(emptyList())
                }
            }
        """

        /** `forty` has 38 statement lines and `fortyOne` 39; blank and comment lines inside them do not count. */
        fun lengthText(): String {
            fun body(statements: Int) = (1..statements).joinToString("\n") { "    println($it)" }
            return "package m\n\n/** KDoc is not code. */\nfun forty() {\n    // a comment\n\n" + body(38) + "\n}\n\n" +
                "fun fortyOne() {\n" + body(39) + "\n}"
        }

        /** A file with [lines] code lines and comment and blank lines that do not count. */
        fun fileOf(lines: Int): String =
            "// header comment\npackage m\n\n" + (1 until lines).joinToString("\n") { "val v$it = $it /* trailing */" }
    }
}
