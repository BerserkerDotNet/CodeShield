package codeshield.kotlin

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** LCOM4 cohesion (docs/specification.md, "Cohesion") and the type facts behind abstractness ("Main sequence"). */
class CohesionAndTypesTest {
    @Test
    fun `members that share state form one component`() {
        val connected = analysis.classFact(COHESION, "Connected")

        assertTrue(connected["applicable"] as Boolean)
        assertEquals(listOf(listOf(id("Connected.increment()"), id("Connected.read()"))), connected["components"])
        assertEquals(
            listOf(mapOf("from" to id("Connected.increment()"), "to" to id("Connected.read()"), "reason" to "field:count")),
            connected["edges"],
        )
    }

    @Test
    fun `members with disjoint state form two components`() {
        val split = analysis.classFact(COHESION, "Split")

        assertEquals(
            listOf(listOf(id("Split.incrementA()"), id("Split.readA()")), listOf(id("Split.incrementB()"), id("Split.readB()"))),
            split["components"],
        )
    }

    @Test
    fun `a call connects members, and constructor properties and custom accessors take part`() {
        val called = analysis.classFact(COHESION, "Called")

        assertEquals(1, (called["components"] as List<*>).size, "components = ${called["components"]}")
        assertTrue(Analysis.list(called, "edges").any { it["reason"] == "call" })
        assertTrue(Analysis.list(called, "nodes").any { it["id"] == id("Called.label.get") }, "a custom getter is a node")
    }

    @Test
    fun `classes without state or with fewer than two members are not applicable`() {
        for (name in listOf("Stateless", "Single")) {
            val fact = analysis.classFact(COHESION, name)
            assertFalse(fact["applicable"] as Boolean, name)
            assertEquals(emptyList<Any>(), fact["components"], name)
        }
    }

    @Test
    fun `interfaces, objects and enums are not measured for cohesion`() {
        val measured = analysis.contexts.flatMap { Analysis.list(it, "classes") }.map { it["name"] }.toSet()

        assertEquals(setOf("Connected", "Split", "Called", "Stateless", "Single", "Abstract", "Sealed", "Leaf", "Open", "Nested"), measured)
    }

    @Test
    fun `interfaces and abstract and sealed classes are abstract, while enums, annotations, companions and object expressions are not types`() {
        val types = analysis.types().filter { it["file"] == TYPES }.associate { it["unitId"] to it["isAbstract"] }

        assertEquals(
            mapOf(
                id("Shape", TYPES) to true,
                id("Abstract", TYPES) to true,
                id("Sealed", TYPES) to true,
                id("Sealed.Leaf", TYPES) to false,
                id("Open", TYPES) to false,
                id("Open.Nested", TYPES) to false,
                id("Singleton", TYPES) to false,
            ),
            types,
        )
    }

    private fun id(qualified: String, file: String = COHESION) = "$file#$qualified"

    private companion object {
        const val COHESION = "src/main/kotlin/c/Cohesion.kt"
        const val TYPES = "src/main/kotlin/c/Types.kt"

        val analysis: Analysis by lazy {
            analyzeOnce("codeshield-cohesion") {
                file("build.gradle.kts", """plugins { kotlin("jvm") }""")
                file(COHESION, COHESION_TEXT)
                file(TYPES, TYPES_TEXT)
            }
        }

        val COHESION_TEXT = """
            package c

            class Connected {
                private var count = 0
                fun increment() { count++ }
                fun read(): Int = count
            }

            class Split {
                private var a = 0
                private var b = 0
                fun incrementA() { a++ }
                fun readA(): Int = a
                fun incrementB() { b++ }
                fun readB(): Int = b
            }

            class Called(private val prefix: String) {
                private var hits = 0
                val label: String
                    get() = prefix + hits
                fun touch() { hits++ }
                fun describe(): String = label
            }

            class Stateless {
                fun one(): Int = 1
                fun two(): Int = one() + 1
            }

            class Single {
                private var value = 0
                fun bump() { value++ }
            }
        """

        val TYPES_TEXT = """
            package c

            interface Shape
            abstract class Abstract : Shape { abstract fun area(): Double }
            sealed class Sealed { class Leaf : Sealed() }
            open class Open {
                class Nested
                companion object { fun make() = Open() }
                val listener = object : Runnable { override fun run() = Unit }
            }
            object Singleton
            enum class Color { RED, GREEN }
            annotation class Marker
        """
    }
}
