package codeshield.kotlin

import java.io.IOException
import java.nio.file.Files
import java.nio.file.Paths

/**
 * Lexer tokens for duplication detection; comments and whitespace are dropped. Kinds carry a `kotlin:` prefix so Kotlin
 * sequences can never match another language's tokens.
 */
internal object Tokenizer {
    fun tokenize(request: TokenizeRequest): TokenizeResult {
        val paths = RepoPaths(request.root)
        val files = ArrayList<FileTokens>()
        val failures = ArrayList<Reason>()
        for (file in ordinalSorted(request.files)) {
            try {
                val bytes = Files.readAllBytes(Paths.get(paths.toAbs(file)))
                files.add(tokenizeText(file, String(bytes, Charsets.UTF_8).removePrefix("\uFEFF")))
            } catch (error: IOException) {
                failures.add(Reason(ReasonCodes.UNREADABLE_SOURCE, "Cannot tokenize $file: $error", null, file))
            }
        }
        return TokenizeResult(files, failures)
    }

    fun tokenizeText(file: String, text: String): FileTokens {
        val lines = LineIndex(text)
        val tokens = KotlinSyntax.codeTokens(text).map {
            Token(
                kind = "kotlin:${it.type}",
                text = text.substring(it.start, it.end),
                line = lines.line(it.start),
                column = lines.column(it.start),
                endLine = lines.line(it.end),
                endColumn = lines.column(it.end),
                offset = it.start,
            )
        }
        return FileTokens(file, tokens)
    }
}
