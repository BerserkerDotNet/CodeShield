package codeshield.kotlin

/** Reads Groovy DSL build scripts (`*.gradle`) into [GNode]s without a Groovy runtime. */
internal object GroovyReader {
    fun read(path: String, text: String): GradleScript = try {
        GradleScript(path, GroovyParser(GroovyLexer(text).tokenize()).script(), null)
    } catch (error: GroovySyntaxError) {
        val lines = LineIndex(text)
        GradleScript(path, emptyList(), "line ${lines.line(error.offset)}, column ${lines.column(error.offset)}")
    }
}

/**
 * A tolerant recursive-descent parser for the Groovy subset used by build scripts: statements, command expressions
 * (`implementation project(':a')`, `id 'x' version 'y'`), calls with trailing closures, member chains, strings,
 * lists, maps and assignments. Method and class definitions are skipped; unknown tokens are consumed so parsing
 * always progresses. Only unbalanced brackets are syntax errors. Statements and declarations are parsed here,
 * expressions by [GroovyExpressionParser].
 */
internal class GroovyParser(tokens: List<GToken>) : GroovyExpressionParser(tokens) {
    fun script(): List<GNode> = statements()

    // ---- statements ----

    override fun statements(): List<GNode> {
        val out = ArrayList<GNode>()
        while (true) {
            while (pos < end && tokens[pos].isSeparator) pos++
            if (pos >= end) return out
            val start = pos
            statement()?.let(out::add)
            if (pos == start) skipOne()
        }
    }

    private fun statement(): GNode? {
        skipAnnotations()
        val token = peek() ?: return null
        val keyword = if (token.kind == GKind.IDENT) keywordStatement(token) else null
        return keyword ?: declaration() ?: commandStatement()
    }

    private fun keywordStatement(token: GToken): GNode? = when (token.text) {
        "import", "package" -> skipImport(token)
        "if", "while", "for", "switch", "try", "do" -> control()
        "return", "throw", "assert", "break", "continue" -> jump()
        "case", "default" -> caseLabel()
        in TYPE_KEYWORDS -> typeDeclaration(token.line)
        else -> null
    }

    private fun skipAnnotations() {
        while (isOp("@") && peek(1)?.let { it.kind == GKind.IDENT && it.text != "interface" } == true) {
            take()
            take()
            while (isOp(".") && peek(1)?.kind == GKind.IDENT) {
                take()
                take()
            }
            if (isOp("(")) skipOne()
            skipLineBreaks()
        }
    }

    private fun skipImport(token: GToken): GNode {
        while (pos < end && !tokens[pos].isSeparator) skipOne()
        return GOther(token.line, emptyList())
    }

    private fun typeDeclaration(line: Int): GNode {
        var index = pos
        while (index < end && !tokens[index].isOp("{")) index = if (tokens[index].isOpener) afterGroup(index) else index + 1
        pos = if (index < end) afterGroup(index) else end
        return GOther(line, emptyList())
    }

    /** `if`/`else`, loops, `switch` and `try`/`catch`/`finally`: conditions and bodies become children. */
    private fun control(): GNode {
        val first = take()
        val children = ArrayList<GNode>()
        var previous: String? = null
        var keyword: String? = first.text
        while (keyword != null) {
            if (isOp("(")) children.add(GOther(first.line, group(significant(pos), true) { statements() }))
            if (!(keyword == "while" && previous == "do")) children.add(body())
            previous = keyword
            keyword = continuation(keyword)
        }
        return GOther(first.line, children)
    }

    private fun body(): GNode {
        val index = if (skipNewlines) significant(pos) else nextLine()
        if (index >= end || tokens[index].isOp(";")) return GOther(here(), emptyList())
        pos = index
        if (tokens[index].isOp("{")) return closure(index)
        return statement() ?: GOther(here(), emptyList())
    }

    private fun continuation(previous: String): String? {
        val index = nextLine()
        val word = at(index)?.takeIf { it.kind == GKind.IDENT }?.text ?: return null
        if (previous !in CONTINUED[word].orEmpty()) return null
        pos = index + 1
        return word
    }

    private fun jump(): GNode {
        val keyword = take()
        val children = ArrayList<GNode>()
        if (!atStatementEnd()) children.add(expression())
        if (keyword.text == "assert" && isOp(":")) {
            take()
            children.add(expression())
        }
        return GOther(keyword.line, children)
    }

    private fun atStatementEnd(): Boolean {
        val token = peek()
        return token == null || token.isSeparator
    }

    private fun caseLabel(): GNode {
        val keyword = take()
        val value = if (keyword.text == "case" && !isOp(":")) listOf(expression()) else emptyList()
        if (isOp(":") || isOp("->")) take()
        return GOther(keyword.line, value)
    }

    // ---- declarations ----

    /** `def x = ...`, `String x = ...`, `final x`, method definitions (skipped) and modified type declarations. */
    private fun declaration(): GNode? {
        val start = pos
        val line = peek()?.line ?: return null
        val modified = skipModifiers()
        if (peekWord() in TYPE_KEYWORDS) return typeDeclaration(line)
        if (modified && isOp("(")) return multipleAssignment(line)
        val name = declaredName(modified)
        if (name == null) {
            pos = start
            return null
        }
        return declarator(line, name)
    }

    /** Consumes modifiers such as `def` or `private static`; true when there was at least one. */
    private fun skipModifiers(): Boolean {
        var modified = false
        while (peekWord() in MODIFIERS) {
            take()
            modified = true
        }
        return modified
    }

    /** The index of the declared name after an optional type, or null when the statement is not a declaration. */
    private fun declaredName(modified: Boolean): Int? {
        val first = significant(pos)
        if (!isNameAt(first)) return null
        val afterType = typeEnd(first)
        if (afterType != null && isTypedDeclarator(first, afterType, modified)) return afterType
        return if (modified) first else null
    }

    /** A name at [name] after the type starting at [first], ending a declarator such as `String name = ...`. */
    private fun isTypedDeclarator(first: Int, name: Int, modified: Boolean): Boolean =
        isNameAt(name) && declaresWithType(tokens[first], modified) && endsDeclarator(name + 1)

    private fun declaresWithType(type: GToken, modified: Boolean): Boolean =
        modified || type.text in PRIMITIVES || type.text.first().isUpperCase()

    private fun endsDeclarator(index: Int): Boolean {
        if (index >= end) return true
        val token = tokens[index]
        return token.kind == GKind.NL || token.isOp(";") || token.isOp("=") || token.isOp("(") || token.isOp(",")
    }

    private fun declarator(line: Int, nameIndex: Int): GNode {
        val name = tokens[nameIndex]
        pos = nameIndex + 1
        if (isOp("(")) return methodDefinition(line)
        if (!isOp("=")) return GOther(line, emptyList())
        take()
        skipLineBreaks()
        return GAssign(line, GRef(name.line, name.text), expression(), "=")
    }

    private fun methodDefinition(line: Int): GNode {
        skipOne()
        if (peek()?.isIdent("throws") == true) {
            take()
            while (peek()?.let { it.kind == GKind.IDENT || it.isOp(".") || it.isOp(",") } == true) take()
        }
        val index = nextLine()
        if (isOpAt(index, "{")) pos = afterGroup(index)
        return GOther(line, emptyList())
    }

    private fun multipleAssignment(line: Int): GNode {
        skipOne()
        if (!isOp("=")) return GOther(line, emptyList())
        take()
        skipLineBreaks()
        return GOther(line, listOf(expression()))
    }

    private companion object {
        val MODIFIERS = setOf(
            "def", "var", "final", "static", "private", "public", "protected", "abstract",
            "transient", "volatile", "native", "strictfp", "synchronized",
        )
        val TYPE_KEYWORDS = setOf("class", "interface", "enum", "trait")
        val PRIMITIVES = setOf("void", "boolean", "byte", "char", "short", "int", "long", "float", "double")

        /** The clauses that continue a control statement, with the keywords each may follow. */
        val CONTINUED = mapOf(
            "else" to setOf("if"),
            "catch" to setOf("try", "catch"),
            "finally" to setOf("try", "catch"),
            "while" to setOf("do"),
        )
    }
}
