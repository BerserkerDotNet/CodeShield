package codeshield.kotlin

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Path

class ProtocolTest {
    @Test
    fun `version reports the detekt-cli on the class path with its Kotlin and the running Java`() {
        val versions = Wire.call(mapOf("protocol" to 1, "command" to "version"))

        assertEquals(listOf("detekt", "java", "kotlin"), versions.keys.toList())
        assertEquals(System.getProperty("codeshield.detektVersion"), versions["detekt"])
        assertEquals(System.getProperty("java.version"), versions["java"])
        assertTrue(Regex("""\d+\.\d+\.\d+.*""").matches(versions["kotlin"] as String), "kotlin = ${versions["kotlin"]}")
    }

    @Test
    fun `a UTF-8 byte order mark before the request is ignored`() {
        val response = String(Wire.callRaw("\uFEFF{\"protocol\":1,\"command\":\"version\"}"), Charsets.UTF_8)

        assertTrue(response.startsWith("{\"detekt\":"), response)
    }

    @Test
    fun `responses escape every non-ASCII character`(@TempDir root: Path) {
        Repo(root).file("build.gradle.kts", "").file("src/main/kotlin/Ü.kt", "val café = \"naïve\"")

        val bytes = Wire.callRaw(
            Json.write(mapOf("protocol" to 1, "command" to "tokenize", "root" to root.toString(), "files" to listOf("src/main/kotlin/Ü.kt"))),
        )

        assertTrue(bytes.all { it >= 0 }, "the response must be pure ASCII")
        val tokens = Wire.call(
            mapOf("protocol" to 1, "command" to "tokenize", "root" to root.toString(), "files" to listOf("src/main/kotlin/Ü.kt")),
        )
        val file = Analysis.list(tokens, "files").single()
        assertEquals("src/main/kotlin/Ü.kt", file["path"])
        assertEquals("café", Analysis.list(file, "tokens")[1]["text"])
    }

    @Test
    fun `malformed requests are rejected with the reason`() {
        val cases = mapOf(
            "[]" to "The request must be a JSON object.",
            """{"command":"version"}""" to "Unsupported protocol null",
            """{"protocol":2,"command":"version"}""" to "Unsupported protocol 2",
            """{"protocol":1,"command":"compile"}""" to "Unknown command compile",
            """{"protocol":1,"command":"analyze","files":[]}""" to "Request requires root and files.",
            """{"protocol":1,"command":"tokenize","root":"."}""" to "Request requires root and files.",
            """{"protocol":1,"command":"tokenize","root":".","files":[1]}""" to "files must be an array of strings.",
            """{"protocol":1,"command":"analyze","root":".","files":[],"focusFiles":"a.kt"}""" to "focusFiles must be an array of strings.",
            """{"protocol":1,"command":"analyze","root":".","files":[],"correctnessRules":[{"evaluatorId":"x"}]}""" to
                "A correctness rule requires analyzerId.",
            """{"protocol":1,"command":"analyze","root":".","files":[],"correctnessRules":[{"analyzerId":"X"}]}""" to
                "A correctness rule requires evaluatorId.",
            """{"protocol":1,"command":"analyze","root":".","files":[],"correctnessRules":[1]}""" to
                "correctnessRules entries must be objects.",
        )

        for ((request, message) in cases) {
            val error = assertThrows<IllegalArgumentException>(request) { Wire.callRaw(request) }
            assertEquals(message, error.message, request)
        }
    }

    @Test
    fun `a malformed JSON document is rejected`() {
        assertThrows<IllegalArgumentException> { Wire.callRaw("""{"protocol":1,""") }
    }

    @Test
    fun `an empty snapshot has no contexts, modules or failures`(@TempDir root: Path) {
        val analysis = Repo(root).analyze()

        assertEquals("kotlin", analysis.json["language"])
        assertEquals(emptyList<Any>(), analysis.contexts)
        assertEquals(emptyList<Any>(), analysis.graph["modules"])
        assertEquals(emptyList<Any>(), analysis.json["failures"])
        assertEquals(System.getProperty("codeshield.detektVersion"), (analysis.json["analyzerVersions"] as Map<*, *>)["detekt"])
    }
}
