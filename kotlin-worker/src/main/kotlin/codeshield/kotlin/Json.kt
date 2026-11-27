package codeshield.kotlin

/** A value that serializes itself as a JSON tree of maps, lists, strings, numbers, booleans and nulls. */
internal interface JsonValue {
    fun toJson(): Any?
}

/** Minimal JSON reader and writer for the worker protocol; the worker has no runtime dependency of its own. */
internal object Json {
    /** The two-character escapes; other control and non-ASCII characters become `\uXXXX`. */
    private val ESCAPES = mapOf('"' to "\\\"", '\\' to "\\\\", '\n' to "\\n", '\r' to "\\r", '\t' to "\\t")

    fun parse(text: String): Any? = JsonReader(text).document()

    /** Writes compact JSON; every non-ASCII character is escaped, so the bytes are independent of the platform encoding. */
    fun write(value: Any?): String = StringBuilder().also { append(it, value) }.toString()

    private fun append(out: StringBuilder, value: Any?) {
        when (value) {
            null -> out.append("null")
            is JsonValue -> append(out, value.toJson())
            is String -> appendString(out, value)
            is Boolean, is Int, is Long, is Short, is Byte -> out.append(value)
            is Number -> appendDouble(out, value.toDouble())
            else -> appendContainer(out, value)
        }
    }

    private fun appendContainer(out: StringBuilder, value: Any) {
        when (value) {
            is Map<*, *> -> appendObject(out, value)
            is Iterable<*> -> appendArray(out, value)
            is Array<*> -> appendArray(out, value.asIterable())
            else -> throw IllegalArgumentException("Cannot serialize ${value.javaClass.name} as JSON.")
        }
    }

    private fun appendDouble(out: StringBuilder, value: Double) {
        require(!value.isNaN() && !value.isInfinite()) { "JSON cannot represent $value." }
        if (value == Math.rint(value) && Math.abs(value) < 1e15) out.append(value.toLong()) else out.append(value)
    }

    private fun appendObject(out: StringBuilder, map: Map<*, *>) {
        out.append('{')
        var first = true
        for ((key, item) in map) {
            if (!first) out.append(',')
            first = false
            appendString(out, key as? String ?: throw IllegalArgumentException("JSON object keys must be strings."))
            out.append(':')
            append(out, item)
        }
        out.append('}')
    }

    private fun appendArray(out: StringBuilder, items: Iterable<*>) {
        out.append('[')
        var first = true
        for (item in items) {
            if (!first) out.append(',')
            first = false
            append(out, item)
        }
        out.append(']')
    }

    private fun appendString(out: StringBuilder, text: String) {
        out.append('"')
        for (ch in text) appendChar(out, ch)
        out.append('"')
    }

    private fun appendChar(out: StringBuilder, ch: Char) {
        val escape = ESCAPES[ch]
        when {
            escape != null -> out.append(escape)
            ch < ' ' || ch.code > 0x7E -> out.append("\\u").append(String.format("%04x", ch.code))
            else -> out.append(ch)
        }
    }
}

private class JsonReader(private val text: String) {
    private var index = 0

    fun document(): Any? {
        val value = value(0)
        skipWhitespace()
        if (index != text.length) fail("unexpected trailing content")
        return value
    }

    private fun value(depth: Int): Any? {
        if (depth > MAX_DEPTH) fail("nesting is too deep")
        skipWhitespace()
        if (index >= text.length) fail("unexpected end of input")
        return when (text[index]) {
            '{' -> objectValue(depth)
            '[' -> arrayValue(depth)
            else -> scalar()
        }
    }

    /** A string, `true`, `false`, `null` or a number. */
    private fun scalar(): Any? = when (text[index]) {
        '"' -> string()
        't' -> literal("true", true)
        'f' -> literal("false", false)
        'n' -> literal("null", null)
        else -> number()
    }

    private fun objectValue(depth: Int): Map<String, Any?> {
        val result = LinkedHashMap<String, Any?>()
        index++
        skipWhitespace()
        if (peek('}')) return result
        while (true) {
            skipWhitespace()
            if (index >= text.length || text[index] != '"') fail("expected a property name")
            val key = string()
            skipWhitespace()
            expect(':')
            result[key] = value(depth + 1)
            skipWhitespace()
            if (peek('}')) return result
            expect(',')
        }
    }

    private fun arrayValue(depth: Int): List<Any?> {
        val result = ArrayList<Any?>()
        index++
        skipWhitespace()
        if (peek(']')) return result
        while (true) {
            result.add(value(depth + 1))
            skipWhitespace()
            if (peek(']')) return result
            expect(',')
        }
    }

    private fun string(): String {
        val out = StringBuilder()
        index++
        while (true) {
            if (index >= text.length) fail("unterminated string")
            val ch = text[index++]
            when {
                ch == '"' -> return out.toString()
                ch == '\\' -> escape(out)
                ch < ' ' -> fail("control character in string")
                else -> out.append(ch)
            }
        }
    }

    private fun escape(out: StringBuilder) {
        if (index >= text.length) fail("unterminated escape")
        val ch = text[index++]
        val unescaped = UNESCAPED[ch]
        when {
            unescaped != null -> out.append(unescaped)
            ch == 'u' -> out.append(unicode())
            else -> fail("invalid escape '\\$ch'")
        }
    }

    private fun unicode(): Char {
        if (index + 4 > text.length) fail("truncated unicode escape")
        val code = text.substring(index, index + 4).toIntOrNull(16) ?: fail("invalid unicode escape")
        index += 4
        return code.toChar()
    }

    private fun number(): Number {
        val start = index
        if (peekChar() == '-') index++
        digits()
        val fractional = fraction()
        val exponential = exponent()
        val literal = text.substring(start, index)
        val integer = if (fractional || exponential) null else integer(literal)
        return integer ?: literal.toDoubleOrNull() ?: fail("invalid number '$literal'")
    }

    /** Consumes a `.digits` fraction; true when there was one. */
    private fun fraction(): Boolean {
        if (peekChar() != '.') return false
        index++
        digits()
        return true
    }

    /** Consumes an exponent such as `e10` or `E-3`; true when there was one. */
    private fun exponent(): Boolean {
        if (peekChar() != 'e' && peekChar() != 'E') return false
        index++
        if (peekChar() == '+' || peekChar() == '-') index++
        digits()
        return true
    }

    private fun digits() {
        val start = index
        while (index < text.length && text[index] in '0'..'9') index++
        if (index == start) fail("expected a digit")
    }

    private fun literal(word: String, value: Any?): Any? {
        if (!text.startsWith(word, index)) fail("unexpected character '${text[index]}'")
        index += word.length
        return value
    }

    private fun skipWhitespace() {
        while (index < text.length && text[index].let { it == ' ' || it == '\t' || it == '\n' || it == '\r' }) index++
    }

    private fun peekChar(): Char? = if (index < text.length) text[index] else null

    private fun peek(ch: Char): Boolean {
        if (index < text.length && text[index] == ch) {
            index++
            return true
        }
        return false
    }

    private fun expect(ch: Char) {
        if (!peek(ch)) fail("expected '$ch'")
    }

    private fun fail(message: String): Nothing = throw IllegalArgumentException("Invalid JSON request at offset $index: $message.")

    private companion object {
        const val MAX_DEPTH = 64

        /** The character each single-character escape stands for; `\u` takes four hex digits instead. */
        val UNESCAPED = mapOf(
            '"' to '"', '\\' to '\\', '/' to '/', 'b' to '\b', 'f' to '\u000C', 'n' to '\n', 'r' to '\r', 't' to '\t',
        )

        /** [literal] as an Int when it fits, else as a Long, or null when it is out of Long range. */
        fun integer(literal: String): Number? {
            val value = literal.toLongOrNull() ?: return null
            return if (value in Int.MIN_VALUE..Int.MAX_VALUE) value.toInt() else value
        }
    }
}
