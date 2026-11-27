package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement

/** Physical-line accounting for the code tokens (not whitespace or comments) of one parsed file. */
internal class CodeLines(private val parsed: ParsedFile) {
    private val tokens = KotlinSyntax.codeTokens(parsed.text)
    private val starts = IntArray(tokens.size) { tokens[it].start }

    /** Distinct lines holding at least one code token that starts inside [start, end). */
    fun count(start: Int, end: Int): Int {
        var lines = 0
        var last = -1
        var index = firstAtOrAfter(start)
        while (index < tokens.size && starts[index] < end) {
            val token = tokens[index]
            val first = maxOf(parsed.lines.lineIndex(token.start), last + 1)
            val final = parsed.lines.lineIndex(maxOf(token.end - 1, token.start))
            if (final >= first) {
                lines += final - first + 1
                last = final
            }
            index++
        }
        return lines
    }

    fun total(): Int = count(0, Int.MAX_VALUE)

    /** The span of an element from its first code token, so leading KDoc and comments are not part of it. */
    fun span(element: PsiElement): Span {
        val range = element.textRange
        return parsed.lines.span(firstCode(range.startOffset, range.endOffset) ?: range.startOffset, range.endOffset)
    }

    /** The offset of the first code token inside [start, end), or null when the range holds only trivia. */
    fun firstCode(start: Int, end: Int): Int? {
        val index = firstAtOrAfter(start)
        return if (index < starts.size && starts[index] < end) starts[index] else null
    }

    private fun firstAtOrAfter(offset: Int): Int {
        var low = 0
        var high = starts.size
        while (low < high) {
            val mid = (low + high) ushr 1
            if (starts[mid] < offset) low = mid + 1 else high = mid
        }
        return low
    }
}

/** One parsed Kotlin source with its code-line index and unit names. */
internal class SourceUnit(val parsed: ParsedFile) {
    val path: String get() = parsed.path
    val code = CodeLines(parsed)
    val units = UnitNamer(parsed, code)
}
