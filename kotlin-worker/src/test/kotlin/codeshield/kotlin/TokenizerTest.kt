package codeshield.kotlin

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Path

/** Duplication tokens: Kotlin lexer tokens without trivia, positioned in the original text. */
class TokenizerTest {
    @TempDir
    lateinit var root: Path

    @Test
    fun `comments and whitespace are dropped and kinds carry the kotlin prefix`() {
        val tokens = tokens("fun a() = 1 // note\n/* block */ val b = \"x\"\n/** doc */\n")

        assertEquals(
            listOf("fun", "IDENTIFIER", "LPAR", "RPAR", "EQ", "INTEGER_LITERAL", "val", "IDENTIFIER", "EQ", "OPEN_QUOTE", "REGULAR_STRING_PART", "CLOSING_QUOTE")
                .map { "kotlin:$it" },
            tokens.map { it["kind"] },
        )
        assertEquals(listOf("fun", "a", "(", ")", "=", "1", "val", "b", "=", "\"", "x", "\""), tokens.map { it["text"] })
        assertEquals(position(1, 1, 1, 4, 0), positionOf(tokens[0]))
        assertEquals(position(2, 17, 2, 18, 36), positionOf(tokens[7]))
    }

    @Test
    fun `windows and old mac line breaks keep offsets in the original text`() {
        val crlf = tokens("val a = 1\r\nval b = 2\r\n")
        val cr = tokens("val a = 1\rval b = 2\r")

        assertEquals(position(2, 5, 2, 6, 15), positionOf(crlf.single { it["text"] == "b" }))
        assertEquals(position(2, 5, 2, 6, 14), positionOf(cr.single { it["text"] == "b" }))
        assertEquals(crlf.map { it["kind"] }, cr.map { it["kind"] })
    }

    @Test
    fun `a raw string line break is its own token, ending at the start of the next line`() {
        val parts = tokens("val s = \"\"\"one\ntwo\"\"\"").filter { it["kind"] == "kotlin:REGULAR_STRING_PART" }

        assertEquals(listOf("one", "\n", "two"), parts.map { it["text"] })
        assertEquals(position(1, 15, 2, 1, 14), positionOf(parts[1]))
        assertEquals(position(2, 1, 2, 4, 15), positionOf(parts[2]))
    }

    @Test
    fun `a byte order mark is not part of the text`() {
        Repo(root).raw("Bom.kt", byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte()) + "val x = 1".toByteArray(Charsets.UTF_8))

        val first = Analysis.list(Analysis.list(Repo(root).tokenize(listOf("Bom.kt")), "files").single(), "tokens").first()

        assertEquals("val", first["text"])
        assertEquals(position(1, 1, 1, 4, 0), positionOf(first))
    }

    @Test
    fun `files are tokenized in ordinal order and an unreadable file is a failure`() {
        val repo = Repo(root).file("b.kt", "val b = 1").file("a.kt", "val a = 1").file("B.kt", "val c = 1")

        val result = repo.tokenize(listOf("b.kt", "missing.kt", "a.kt", "B.kt"))

        assertEquals(listOf("B.kt", "a.kt", "b.kt"), Analysis.list(result, "files").map { it["path"] })
        val failure = Analysis.list(result, "failures").single()
        assertEquals(ReasonCodes.UNREADABLE_SOURCE, failure["code"])
        assertTrue((failure["message"] as String).startsWith("Cannot tokenize missing.kt: "), "$failure")
        assertEquals("missing.kt", failure["contextId"])
    }

    private fun tokens(text: String): List<Map<*, *>> {
        Repo(root).raw("T.kt", text.toByteArray(Charsets.UTF_8))
        return Analysis.list(Analysis.list(Repo(root).tokenize(listOf("T.kt")), "files").single(), "tokens")
    }

    private fun positionOf(token: Map<*, *>) =
        listOf(token.int("line"), token.int("column"), token.int("endLine"), token.int("endColumn"), token.int("offset"))

    private fun position(line: Int, column: Int, endLine: Int, endColumn: Int, offset: Int) = listOf(line, column, endLine, endColumn, offset)
}
