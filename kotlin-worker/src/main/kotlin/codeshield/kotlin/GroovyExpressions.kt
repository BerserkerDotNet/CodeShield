package codeshield.kotlin

/**
 * The expressions of [GroovyParser]: command expressions (`implementation project(':a')`, `id 'x' version 'y'`),
 * operators, calls with trailing closures, member chains, strings, lists and maps. Closure bodies are [statements].
 */
internal abstract class GroovyExpressionParser(tokens: List<GToken>) : GroovyCursor(tokens) {
    /** The statements up to the end of the current group. */
    protected abstract fun statements(): List<GNode>

    /** An expression statement; a bare name or member path followed by an argument is a command expression. */
    protected fun commandStatement(): GNode {
        val head = expression()
        return if (isCommandHead(head) && startsArgument()) commandChain(head) else head
    }

    private fun isCommandHead(node: GNode): Boolean = (node is GRef && node.name !in CONSTANTS) || node is GMember

    private fun startsArgument(): Boolean {
        val token = peek() ?: return false
        return when (token.kind) {
            GKind.STRING, GKind.NUMBER -> true
            GKind.IDENT -> token.text !in OPERATOR_WORDS
            else -> false
        }
    }

    /** An identifier at [index] other than the operator words `in`, `instanceof` and `as`. */
    protected fun isNameAt(index: Int): Boolean {
        val token = at(index)
        return token != null && token.kind == GKind.IDENT && token.text !in OPERATOR_WORDS
    }

    /** `a b c d` is `a(b).c(d)`; a trailing name without an argument is a property of the last call. */
    private fun commandChain(head: GNode): GNode {
        var node = call(head, commandArguments(), null)
        while (peek()?.let { it.kind == GKind.IDENT && it.text !in OPERATOR_WORDS } == true) {
            val name = take()
            val member = GMember(name.line, node, name.text)
            if (!startsArgument()) return postfix(member)
            node = call(member, commandArguments(), null)
        }
        return node
    }

    private fun commandArguments(): List<GArg> {
        val args = arrayListOf(argument())
        while (isOp(",")) {
            take()
            skipLineBreaks()
            if (peek() == null) break
            args.add(argument())
        }
        return args
    }

    /** `name: value`, `'name': value` or a positional expression. */
    private fun argument(): GArg {
        val token = peek()
        val key = when {
            token == null || !isOp(":", 1) -> null
            token.kind == GKind.IDENT -> token.text
            token.kind == GKind.STRING -> string(token).literal
            else -> null
        }
        if (key == null) return GArg(null, expression())
        take()
        take()
        skipLineBreaks()
        return GArg(key, expression())
    }

    /** Comma-separated arguments up to the end of the current group. */
    private fun arguments(): List<GArg> {
        val args = ArrayList<GArg>()
        while (significant(pos) < end) {
            val start = pos
            args.add(argument())
            if (isOp(",")) take() else if (pos == start) skipOne()
        }
        return args
    }

    /** The arguments inside the bracketed group at the current position. */
    private fun groupArguments(): List<GArg> = group(significant(pos), true) { arguments() }

    protected fun expression(): GNode {
        val left = ternary()
        val op = peek()
        if (op == null || op.kind != GKind.OP || op.text !in ASSIGN_OPS) return left
        take()
        skipLineBreaks()
        return GAssign(op.line, left, expression(), op.text)
    }

    private fun ternary(): GNode {
        val condition = binary()
        val token = peek()
        if (token == null || !(token.isOp("?") || token.isOp("?:"))) return condition
        take()
        skipLineBreaks()
        val first = ternary()
        if (token.isOp("?:")) return GOther(token.line, listOf(condition, first))
        val colon = nextLine()
        if (!isOpAt(colon, ":")) return GOther(token.line, listOf(condition, first))
        pos = colon + 1
        skipLineBreaks()
        return GOther(token.line, listOf(condition, first, ternary()))
    }

    private fun binary(): GNode {
        val first = unary()
        val operands = arrayListOf(first)
        while (takeBinaryOperator()) {
            skipLineBreaks()
            operands.add(unary())
        }
        return if (operands.size == 1) first else GOther(first.line, operands)
    }

    private fun takeBinaryOperator(): Boolean {
        val token = peek() ?: return false
        val negatedWord = token.isOp("!") && peekWord(1) in WORD_OPS
        if (!negatedWord && !isBinaryOperator(token)) return false
        take()
        if (negatedWord) take()
        return true
    }

    private fun unary(): GNode {
        val token = peek() ?: return GOther(here(), emptyList())
        if (token.kind == GKind.OP && token.text in PREFIX_OPS) {
            take()
            return GOther(token.line, listOf(unary()))
        }
        return postfix(primary())
    }

    private fun primary(): GNode {
        val index = significant(pos)
        if (index >= end) return GOther(here(), emptyList())
        val token = tokens[index]
        if (token.kind == GKind.OP) return bracketed(index, token)
        pos = index + 1
        return when {
            token.kind == GKind.STRING -> string(token)
            token.isIdent("new") -> construct(token)
            token.kind == GKind.IDENT -> GRef(token.line, token.text)
            else -> GOther(token.line, emptyList())
        }
    }

    private fun bracketed(index: Int, token: GToken): GNode = when (token.text) {
        "(" -> group(index, true) { expressions(token.line) }
        "[" -> group(index, true) { listOrMap(token.line) }
        "{" -> closure(index)
        else -> {
            skipOne()
            GOther(token.line, emptyList())
        }
    }

    private fun expressions(line: Int): GNode {
        val items = arguments().map { it.value }
        return items.singleOrNull() ?: GOther(line, items)
    }

    /** `[a, b]` is a list; `[:]` and `[k: v]` are maps, represented as calls named `[:]` with named arguments. */
    private fun listOrMap(line: Int): GNode {
        if (isOp(":") && peek(1) == null) {
            take()
            return GCall(line, null, "[:]", emptyList(), null)
        }
        val args = arguments()
        return if (args.any { it.name != null }) GCall(line, null, "[:]", args, null) else GList(line, args.map { it.value })
    }

    protected fun closure(open: Int): GBlock {
        val line = tokens[open].line
        return group(open, false) {
            skipClosureParameters()
            GBlock(line, statements())
        }
    }

    private fun skipClosureParameters() {
        for (index in pos until end) {
            val token = tokens[index]
            if (token.isOp("->")) {
                pos = index + 1
                return
            }
            val allowed = token.kind == GKind.IDENT || token.kind == GKind.NL || token.text in CLOSURE_PARAMETER_TOKENS
            if (!allowed) return
        }
    }

    /** `new a.b.Type<X>(args) { ... }` becomes a call named after the simple type name. */
    private fun construct(keyword: GToken): GNode {
        val first = significant(pos)
        if (!isIdentAt(first)) return GOther(keyword.line, emptyList())
        val afterType = typeEnd(first) ?: (first + 1)
        val name = typeName(first, afterType)
        pos = afterType
        if (!isOp("(")) return GOther(keyword.line, if (isOp("[")) listOf(primary()) else emptyList())
        return GCall(keyword.line, null, name, groupArguments(), trailingClosure())
    }

    /** The last identifier of the type tokens that does not start in lower case, else the first token. */
    private fun typeName(first: Int, afterType: Int): String =
        tokens.subList(first, afterType).lastOrNull { it.kind == GKind.IDENT && !it.text.first().isLowerCase() }?.text
            ?: tokens[first].text

    private fun string(token: GToken): GStr = GStr(token.line, token.segments.map { segment ->
        when (segment) {
            is TextSegment -> GText(segment.text)
            is PathSegment -> GExpr(segment.names.drop(1).fold<String, GNode>(GRef(segment.line, segment.names[0])) { receiver, name ->
                GMember(segment.line, receiver, name)
            })
            is CodeSegment -> GExpr(GroovyParser(segment.tokens).script().let { it.singleOrNull() ?: GOther(token.line, it) })
        }
    })

    private fun postfix(start: GNode): GNode {
        var node = start
        while (true) {
            continueAcrossLineBreak()
            val token = peek() ?: return node
            node = suffix(node, token) ?: return node
        }
    }

    /** [node] extended by the member access, call, index, `++`/`--` or cast at [token], or null when it ends. */
    private fun suffix(node: GNode, token: GToken): GNode? = when {
        token.kind == GKind.OP -> operatorSuffix(node, token)
        token.isIdent("as") -> node.also { skipCastType() }
        else -> null
    }

    private fun operatorSuffix(node: GNode, token: GToken): GNode? = when (token.text) {
        in MEMBER_OPS -> member(node)
        "(" -> call(node, groupArguments(), trailingClosure())
        "{" -> closureCall(node)
        "[" -> GIndex(token.line, node, groupArguments().map { it.value })
        "++", "--" -> node.also { take() }
        else -> null
    }

    /** `name { ... }` and `a.b { ... }` pass the closure to the name; after other expressions a `{` ends them. */
    private fun closureCall(node: GNode): GNode? =
        if (node is GRef || node is GMember) call(node, emptyList(), closure(significant(pos))) else null

    /** A line that starts with `.` or `?.` continues the expression on the previous line. */
    private fun continueAcrossLineBreak() {
        if (skipNewlines || at(pos)?.kind != GKind.NL) return
        val index = nextLine()
        if (isMemberOp(at(index))) pos = index
    }

    private fun trailingClosure(): GBlock? = if (isOp("{")) closure(significant(pos)) else null

    private fun skipCastType() {
        take()
        val first = significant(pos)
        if (isIdentAt(first)) pos = typeEnd(first) ?: (first + 1)
    }

    private fun member(receiver: GNode): GNode {
        val op = take()
        val token = peek()
        if (token == null || (token.kind != GKind.IDENT && token.kind != GKind.STRING)) return GOther(op.line, listOf(receiver))
        take()
        if (token.kind == GKind.IDENT) return GMember(token.line, receiver, token.text)
        val name = string(token)
        return name.literal?.let { GMember(token.line, receiver, it) } ?: GOther(token.line, listOf(receiver, name))
    }

    /** A call of a name, a member or a literal string; a closure passed as the last argument becomes the block. */
    private fun call(target: GNode, args: List<GArg>, trailing: GBlock?): GNode {
        val lastClosure = args.lastOrNull()?.takeIf { trailing == null && it.name == null && it.value is GBlock }
        if (lastClosure == null) return callOf(target, args, trailing)
        return callOf(target, args.dropLast(1), lastClosure.value as GBlock)
    }

    private fun callOf(target: GNode, args: List<GArg>, block: GBlock?): GNode = when {
        target is GRef -> GCall(target.line, null, target.name, args, block)
        target is GMember -> GCall(target.line, target.receiver, target.name, args, block)
        target is GStr && target.literal != null -> GCall(target.line, null, target.literal!!, args, block)
        else -> GOther(target.line, listOf(target) + args.map { it.value } + listOfNotNull(block))
    }

    private companion object {
        val OPERATOR_WORDS = setOf("in", "instanceof", "as")
        val WORD_OPS = setOf("in", "instanceof")
        val MEMBER_OPS = setOf(".", "?.", "*.", ".&", ".@", "::")
        val ASSIGN_OPS = setOf("=", "+=", "-=", "*=", "/=", "%=", "<<=", ">>=", ">>>=", "&=", "|=", "^=", "**=", "?=")
        val BINARY_OPS = setOf(
            "||", "&&", "|", "^", "&", "==", "!=", "===", "!==", "<=>", "=~", "==~", "<", "<=", ">", ">=",
            "<<", ">>", ">>>", "..", "..<", "+", "-", "*", "/", "%", "**",
        )
        val PREFIX_OPS = setOf("!", "-", "+", "~", "++", "--", "*")
        val CLOSURE_PARAMETER_TOKENS = setOf(",", ".", "<", ">", ">>", "?", "[", "]", "=")
        val CONSTANTS = setOf("this", "super", "true", "false", "null")

        fun isBinaryOperator(token: GToken): Boolean =
            (token.kind == GKind.OP && token.text in BINARY_OPS) || (token.kind == GKind.IDENT && token.text in WORD_OPS)

        fun isMemberOp(token: GToken?): Boolean = token != null && token.kind == GKind.OP && token.text in MEMBER_OPS
    }
}
