package codeshield.kotlin

/**
 * The token position of a [GroovyParser]: navigation within the current bracketed group, bracket matching and the
 * token-level recognition of types. Only unbalanced brackets are syntax errors.
 */
internal abstract class GroovyCursor(protected val tokens: List<GToken>) {
    private val matching = match(tokens)
    protected var pos = 0
    protected var end = tokens.size
        private set

    /** Inside parentheses and brackets line breaks do not end anything. */
    protected var skipNewlines = false
        private set

    /** The first significant token index at or after [from], or [end]. */
    protected fun significant(from: Int): Int {
        var index = from
        while (index < end && skipNewlines && tokens[index].kind == GKind.NL) index++
        return minOf(index, end)
    }

    protected fun peek(ahead: Int = 0): GToken? {
        var index = significant(pos)
        repeat(ahead) { index = significant(index + 1) }
        return at(index)
    }

    /** The text of the next significant token when it is an identifier. */
    protected fun peekWord(ahead: Int = 0): String? = peek(ahead)?.takeIf { it.kind == GKind.IDENT }?.text

    protected fun take(): GToken {
        val index = significant(pos)
        pos = index + 1
        return tokens[index]
    }

    protected fun isOp(value: String, ahead: Int = 0): Boolean = peek(ahead)?.isOp(value) == true

    /** The token at [index] when it lies inside the current group. */
    protected fun at(index: Int): GToken? = if (index < end) tokens[index] else null

    protected fun isOpAt(index: Int, value: String): Boolean = at(index)?.isOp(value) == true

    protected fun isIdentAt(index: Int): Boolean = at(index)?.kind == GKind.IDENT

    protected fun nextLine(): Int {
        var index = pos
        while (index < end && tokens[index].kind == GKind.NL) index++
        return index
    }

    protected fun skipLineBreaks() {
        pos = nextLine()
    }

    protected fun here(): Int = tokens.getOrNull(minOf(pos, tokens.size - 1))?.line ?: 0

    /** The index after the closer of the bracketed group opened at [open]. */
    protected fun afterGroup(open: Int): Int = matching[open] + 1

    /** Consumes one token, or a whole bracketed group when the token opens one. */
    protected fun skipOne() {
        val index = significant(pos)
        pos = when {
            index >= end -> end
            tokens[index].isOpener -> afterGroup(index)
            else -> index + 1
        }
    }

    /** Parses the contents of the bracketed group opened at [open], then continues after its closer. */
    protected fun <T> group(open: Int, newlines: Boolean, body: () -> T): T {
        val savedEnd = end
        val savedSkip = skipNewlines
        pos = open + 1
        end = matching[open]
        skipNewlines = newlines
        val result = body()
        end = savedEnd
        skipNewlines = savedSkip
        pos = afterGroup(open)
        return result
    }

    /** The index after a type such as `java.io.File`, `Map<String, List<X>>` or `String[]`, or null if malformed. */
    protected fun typeEnd(first: Int): Int? {
        var index = first + 1
        while (isQualifier(index)) index += 2
        if (isOpAt(index, "<")) index = genericsEnd(index) ?: return null
        while (isArraySuffix(index)) index += 2
        return index
    }

    /** `.name`, continuing a qualified type name. */
    private fun isQualifier(index: Int): Boolean = isOpAt(index, ".") && isIdentAt(index + 1)

    private fun isArraySuffix(index: Int): Boolean = isOpAt(index, "[") && isOpAt(index + 1, "]")

    private fun genericsEnd(open: Int): Int? {
        var depth = 0
        for (index in open until end) {
            val token = tokens[index]
            if (!isGenericToken(token)) return null
            depth += GENERIC_DEPTH[token.text] ?: 0
            if (depth == 0) return index + 1
            if (depth < 0) return null
        }
        return null
    }

    private companion object {
        val GENERIC_TOKENS = setOf(",", ".", "?", "<", ">", ">>", ">>>", "[", "]", "&")
        val GENERIC_DEPTH = mapOf("<" to 1, ">" to -1, ">>" to -2, ">>>" to -3)
        val CLOSERS = mapOf("(" to ")", "[" to "]", "{" to "}")

        /** Names and the punctuation of type arguments, on one line. */
        fun isGenericToken(token: GToken): Boolean =
            token.kind != GKind.NL && (token.kind == GKind.IDENT || token.text in GENERIC_TOKENS)

        fun match(tokens: List<GToken>): IntArray {
            val result = IntArray(tokens.size) { -1 }
            val open = ArrayList<Int>()
            tokens.forEachIndexed { index, token ->
                if (token.isOpener) {
                    open.add(index)
                } else if (token.kind == GKind.OP && token.text in CLOSERS.values) {
                    val opener = open.removeLastOrNull() ?: throw GroovySyntaxError(token.offset)
                    if (CLOSERS[tokens[opener].text] != token.text) throw GroovySyntaxError(token.offset)
                    result[opener] = index
                    result[index] = opener
                }
            }
            open.lastOrNull()?.let { throw GroovySyntaxError(tokens[it].offset) }
            return result
        }
    }
}
