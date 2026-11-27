package codeshield.kotlin

/** A syntax error in a Groovy build script, at [offset] in its text. */
internal class GroovySyntaxError(val offset: Int) : RuntimeException(null, null, false, false)

internal enum class GKind { IDENT, NUMBER, STRING, OP, NL }

/** A piece of a Groovy string: literal text, a `$a.b` property path or the tokens of a `${...}` expression. */
internal sealed class Segment

internal class TextSegment(val text: String) : Segment()

internal class PathSegment(val line: Int, val names: List<String>) : Segment()

internal class CodeSegment(val tokens: List<GToken>) : Segment()

internal class GToken(
    val kind: GKind,
    val text: String,
    val offset: Int,
    val line: Int,
    val segments: List<Segment> = emptyList(),
) {
    fun isOp(value: String): Boolean = kind == GKind.OP && text == value

    fun isIdent(value: String): Boolean = kind == GKind.IDENT && text == value

    val isOpener: Boolean get() = kind == GKind.OP && (text == "(" || text == "[" || text == "{")

    /** A line break or `;`, which ends a statement. */
    val isSeparator: Boolean get() = kind == GKind.NL || isOp(";")
}

/** How a string literal ends and what it may contain. */
private class Quote(val delimiter: String, val multiline: Boolean, val interpolates: Boolean, val slashy: Boolean)

/**
 * Splits Groovy source into identifiers, numbers, strings (with their interpolations), operators and line breaks.
 * Comments and whitespace are dropped; an unterminated string or comment raises [GroovySyntaxError].
 */
internal class GroovyLexer(private val text: String) {
    private val lines = LineIndex(text)
    private var pos = 0

    fun tokenize(): List<GToken> {
        if (text.startsWith("#!")) skipLine()
        val out = ArrayList<GToken>()
        while (pos < text.length) {
            val token = next(out.lastOrNull()) ?: continue
            out.add(token)
        }
        return out
    }

    /** The tokens of a `${...}` interpolation, up to the `}` that closes it. */
    private fun interpolated(): List<GToken> {
        val start = pos
        val out = ArrayList<GToken>()
        var depth = 0
        while (pos < text.length) {
            val token = next(out.lastOrNull()) ?: continue
            if (depth == 0 && token.isOp("}")) return out
            depth += depthChange(token)
            out.add(token)
        }
        throw GroovySyntaxError(start - 2)
    }

    /** The token at the current position, or null after skipping whitespace or a comment. */
    private fun next(previous: GToken?): GToken? {
        val ch = text[pos]
        return when {
            ch in LINE_BREAKS -> newline()
            ch in BLANKS -> skip(1)
            ch == '\\' -> backslash()
            ch == '/' -> slash(previous)
            else -> lexeme(ch)
        }
    }

    /** A backslash before a line break joins the lines; elsewhere it is an operator. */
    private fun backslash(): GToken? {
        if (!isLineBreak(pos + 1)) return operator()
        return skip(if (text.startsWith("\r\n", pos + 1)) 3 else 2)
    }

    /** A comment, a slashy string where an operand is expected, or a division operator. */
    private fun slash(previous: GToken?): GToken? = when {
        text.startsWith("//", pos) -> skipLine()
        text.startsWith("/*", pos) -> blockComment()
        slashyAllowed(previous) -> string(pos, Quote("/", multiline = true, interpolates = true, slashy = true))
        else -> operator()
    }

    /** A quoted string, an identifier, a number or an operator. */
    private fun lexeme(ch: Char): GToken = when {
        ch == '\'' || ch == '"' -> quoted(ch)
        Character.isJavaIdentifierStart(ch) -> identifier()
        ch.isDigit() -> number()
        else -> operator()
    }

    private fun skip(count: Int): GToken? {
        pos += count
        return null
    }

    private fun isLineBreak(index: Int): Boolean = index < text.length && (text[index] == '\n' || text[index] == '\r')

    private fun token(kind: GKind, start: Int, segments: List<Segment> = emptyList()): GToken =
        GToken(kind, text.substring(start, pos), start, lines.line(start), segments)

    private fun newline(): GToken {
        val start = pos
        pos += if (text.startsWith("\r\n", pos)) 2 else 1
        return GToken(GKind.NL, "\n", start, lines.line(start))
    }

    private fun skipLine(): GToken? {
        while (pos < text.length && !isLineBreak(pos)) pos++
        return null
    }

    /** A comment spanning lines separates statements like a line break does. */
    private fun blockComment(): GToken? {
        val start = pos
        val close = text.indexOf("*/", pos + 2)
        if (close < 0) throw GroovySyntaxError(start)
        pos = close + 2
        val spansLines = text.substring(start, pos).any { it == '\n' || it == '\r' }
        return if (spansLines) GToken(GKind.NL, "\n", start, lines.line(start)) else null
    }

    private fun identifier(): GToken {
        val start = pos
        while (pos < text.length && Character.isJavaIdentifierPart(text[pos])) pos++
        return token(GKind.IDENT, start)
    }

    private fun number(): GToken {
        val start = pos
        val hex = text.startsWith("0x", start, ignoreCase = true)
        var seenDot = false
        while (pos < text.length) {
            val decimal = !seenDot && isDecimalPoint(pos)
            if (!decimal && !isNumberPart(pos, hex)) break
            seenDot = seenDot || decimal
            pos++
        }
        return token(GKind.NUMBER, start)
    }

    private fun isDecimalPoint(index: Int): Boolean = text[index] == '.' && text.getOrNull(index + 1)?.isDigit() == true

    /** A digit, letter or `_`, or the sign of a decimal exponent such as `1e-3`. */
    private fun isNumberPart(index: Int, hex: Boolean): Boolean {
        val ch = text[index]
        return ch.isLetterOrDigit() || ch == '_' || (!hex && ch in SIGNS && text[index - 1] in EXPONENTS)
    }

    private fun operator(): GToken {
        val start = pos
        pos += OPERATORS.firstOrNull { text.startsWith(it, pos) }?.length ?: 1
        return token(GKind.OP, start)
    }

    /** A `/` starts a slashy string where an operand is expected; after a value it is division. */
    private fun slashyAllowed(previous: GToken?): Boolean = opensSlashy(text.getOrNull(pos + 1)) && expectsOperand(previous)

    private fun quoted(quote: Char): GToken {
        val triple = "$quote$quote$quote"
        val interpolates = quote == '"'
        return if (text.startsWith(triple, pos)) {
            string(pos, Quote(triple, multiline = true, interpolates = interpolates, slashy = false))
        } else {
            string(pos, Quote(quote.toString(), multiline = false, interpolates = interpolates, slashy = false))
        }
    }

    private fun string(start: Int, quote: Quote): GToken {
        pos += quote.delimiter.length
        val builder = SegmentBuilder()
        while (!text.startsWith(quote.delimiter, pos)) {
            if (pos >= text.length || (!quote.multiline && isLineBreak(pos))) throw GroovySyntaxError(start)
            stringPart(quote, builder)
        }
        pos += quote.delimiter.length
        return token(GKind.STRING, start, builder.segments())
    }

    private fun stringPart(quote: Quote, builder: SegmentBuilder) {
        val ch = text[pos]
        when {
            ch == '\\' && quote.slashy -> builder.text(slashyEscape())
            ch == '\\' -> builder.text(escape())
            ch == '$' && quote.interpolates -> interpolation(builder)
            else -> {
                builder.text(ch.toString())
                pos++
            }
        }
    }

    private fun slashyEscape(): String {
        val escapedSlash = text.getOrNull(pos + 1) == '/'
        pos += if (escapedSlash) 2 else 1
        return if (escapedSlash) "/" else "\\"
    }

    private fun escape(): String {
        val ch = text.getOrNull(pos + 1) ?: throw GroovySyntaxError(pos)
        pos += 2
        return when (ch) {
            'u' -> unicode()
            '\r' -> lineContinuation()
            else -> ESCAPES[ch] ?: ch.toString()
        }
    }

    private fun lineContinuation(): String {
        if (text.getOrNull(pos) == '\n') pos++
        return ""
    }

    private fun unicode(): String {
        val digits = text.substring(pos, minOf(pos + 4, text.length))
        val code = if (digits.length == 4) digits.toIntOrNull(16) else null
        if (code == null) return "u"
        pos += 4
        return code.toChar().toString()
    }

    /** `${expression}` or a `$name.path` property path; a lone `$` is text. */
    private fun interpolation(builder: SegmentBuilder) {
        val start = pos
        pos++
        val ch = text.getOrNull(pos)
        when {
            ch == '{' -> {
                pos++
                builder.add(CodeSegment(interpolated()))
            }
            ch != null && ch != '$' && Character.isJavaIdentifierStart(ch) -> builder.add(PathSegment(lines.line(start), dottedPath()))
            else -> builder.text("$")
        }
    }

    private fun dottedPath(): List<String> {
        val names = arrayListOf(pathName())
        while (text.getOrNull(pos) == '.' && text.getOrNull(pos + 1)?.let { it != '$' && Character.isJavaIdentifierStart(it) } == true) {
            pos++
            names.add(pathName())
        }
        return names
    }

    private fun pathName(): String {
        val start = pos
        while (pos < text.length && text[pos] != '$' && Character.isJavaIdentifierPart(text[pos])) pos++
        return text.substring(start, pos)
    }

    private class SegmentBuilder {
        private val parts = ArrayList<Segment>()
        private val pending = StringBuilder()

        fun text(value: String) {
            pending.append(value)
        }

        fun add(segment: Segment) {
            flush()
            parts.add(segment)
        }

        fun segments(): List<Segment> {
            flush()
            return parts
        }

        private fun flush() {
            if (pending.isEmpty()) return
            parts.add(TextSegment(pending.toString()))
            pending.setLength(0)
        }
    }

    private companion object {
        /** Longest first, so `>>>=` wins over `>>` and `?.` over `?`. */
        val OPERATORS = listOf(
            ">>>=", "...", "..<", "<=>", "===", "!==", ">>>", "**=", "<<=", ">>=", "==~",
            "?.", "*.", ".&", ".@", "?:", "?=", "=~", "**", "->", "==", "!=", "<=", ">=", "&&", "||", "++", "--",
            "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "::", "..",
        )

        val VALUE_CLOSERS = setOf(")", "]", "}")

        val LINE_BREAKS = setOf('\n', '\r')

        val BLANKS = setOf(' ', '\t', '\u000C')

        val SIGNS = setOf('+', '-')

        val EXPONENTS = setOf('e', 'E')

        /** Single-character escapes; `\u`, and `\` before a CRLF line break, need more than one character. */
        val ESCAPES = mapOf('n' to "\n", 't' to "\t", 'b' to "\b", 'r' to "\r", 'f' to "\u000C", '\n' to "")

        fun depthChange(token: GToken): Int = when {
            token.isOp("{") -> 1
            token.isOp("}") -> -1
            else -> 0
        }

        /** A `/` followed by `=`, whitespace or nothing is an operator. */
        fun opensSlashy(after: Char?): Boolean = after != null && after != '=' && !after.isWhitespace()

        /** At the start of a line or after an operator other than a closing bracket. */
        fun expectsOperand(previous: GToken?): Boolean =
            previous == null || previous.kind == GKind.NL || (previous.kind == GKind.OP && previous.text !in VALUE_CLOSERS)
    }
}
