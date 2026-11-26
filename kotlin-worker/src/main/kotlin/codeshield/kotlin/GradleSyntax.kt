package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtAnnotatedExpression
import org.jetbrains.kotlin.psi.KtArrayAccessExpression
import org.jetbrains.kotlin.psi.KtBinaryExpression
import org.jetbrains.kotlin.psi.KtBinaryExpressionWithTypeRHS
import org.jetbrains.kotlin.psi.KtBlockExpression
import org.jetbrains.kotlin.psi.KtCallExpression
import org.jetbrains.kotlin.psi.KtClassOrObject
import org.jetbrains.kotlin.psi.KtCollectionLiteralExpression
import org.jetbrains.kotlin.psi.KtEscapeStringTemplateEntry
import org.jetbrains.kotlin.psi.KtExpression
import org.jetbrains.kotlin.psi.KtLambdaExpression
import org.jetbrains.kotlin.psi.KtNameReferenceExpression
import org.jetbrains.kotlin.psi.KtNamedFunction
import org.jetbrains.kotlin.psi.KtParenthesizedExpression
import org.jetbrains.kotlin.psi.KtPostfixExpression
import org.jetbrains.kotlin.psi.KtProperty
import org.jetbrains.kotlin.psi.KtQualifiedExpression
import org.jetbrains.kotlin.psi.KtScriptInitializer
import org.jetbrains.kotlin.psi.KtStringTemplateExpression
import org.jetbrains.kotlin.psi.KtThisExpression
import org.jetbrains.kotlin.psi.KtTypeAlias

/**
 * A small syntax tree shared by the Kotlin DSL and Groovy DSL readers. It keeps only what the static Gradle model needs:
 * calls with their arguments and trailing blocks, member chains, strings, assignments and lists. Everything else is
 * [GOther], whose children are still searched.
 */
internal sealed class GNode {
    abstract val line: Int

    open val children: List<GNode> get() = emptyList()
}

internal sealed class GPart

internal class GText(val text: String) : GPart()

internal class GExpr(val node: GNode) : GPart()

internal class GStr(override val line: Int, val parts: List<GPart>) : GNode() {
    /** The value when the string has no interpolation, otherwise null. */
    val literal: String? get() = if (parts.all { it is GText }) parts.joinToString("") { (it as GText).text } else null

    override val children: List<GNode> get() = parts.mapNotNull { (it as? GExpr)?.node }
}

internal class GRef(override val line: Int, val name: String) : GNode()

internal class GMember(override val line: Int, val receiver: GNode, val name: String) : GNode() {
    override val children: List<GNode> get() = listOf(receiver)
}

internal class GArg(val name: String?, val value: GNode)

internal class GCall(
    override val line: Int,
    val receiver: GNode?,
    val name: String,
    val args: List<GArg>,
    val block: GBlock?,
) : GNode() {
    override val children: List<GNode> get() = listOfNotNull(receiver) + args.map { it.value } + listOfNotNull(block)

    fun arg(index: Int): GNode? = args.filter { it.name == null }.getOrNull(index)?.value

    fun named(name: String): GNode? = args.firstOrNull { it.name == name }?.value
}

internal class GBlock(override val line: Int, val statements: List<GNode>) : GNode() {
    override val children: List<GNode> get() = statements
}

internal class GIndex(override val line: Int, val receiver: GNode, val indices: List<GNode>) : GNode() {
    override val children: List<GNode> get() = listOf(receiver) + indices
}

internal class GAssign(override val line: Int, val target: GNode, val value: GNode, val op: String) : GNode() {
    override val children: List<GNode> get() = listOf(target, value)
}

internal class GList(override val line: Int, val items: List<GNode>) : GNode() {
    override val children: List<GNode> get() = items
}

internal class GOther(override val line: Int, override val children: List<GNode>) : GNode()

/** One Gradle script reduced to [GNode] statements; [error] is the first syntax error as `line L, column C`. */
internal class GradleScript(val path: String, val statements: List<GNode>, val error: String?)

/** Reads `*.gradle.kts` with the Kotlin PSI parser of detekt-cli and `*.gradle` with [GroovyReader]. */
internal class GradleScripts(private val parser: KotlinParser) {
    fun read(path: String, text: String): GradleScript =
        if (path.endsWith(".kts")) readKts(path, text) else GroovyReader.read(path, text)

    private fun readKts(path: String, text: String): GradleScript {
        val parsed = parser.parse(path, text)
        val error = parsed.syntaxError
        if (error != null) return GradleScript(path, emptyList(), error)
        val reader = KtsReader(parsed.lines)
        val elements: List<PsiElement> = parsed.file.script?.blockExpression?.statements ?: parsed.file.declarations
        return GradleScript(path, elements.map { reader.node(it) }, null)
    }
}

/** Converts Kotlin script PSI into [GNode]s. */
private class KtsReader(private val lines: LineIndex) {
    fun node(element: PsiElement?): GNode {
        if (element == null) return GOther(0, emptyList())
        return reference(element) ?: value(element) ?: container(element) ?: wrapper(element) ?: other(element)
    }

    /** Calls, names, `this` and member or index chains. */
    private fun reference(element: PsiElement): GNode? = when (element) {
        is KtCallExpression -> call(null, element)
        is KtQualifiedExpression -> qualified(element)
        is KtNameReferenceExpression -> GRef(lines.line(element), element.getReferencedName())
        is KtThisExpression -> GRef(lines.line(element), "this")
        is KtArrayAccessExpression ->
            GIndex(lines.line(element), node(element.arrayExpression), element.indexExpressions.map { node(it) })
        else -> null
    }

    /** Strings, assignments, collection literals and properties. */
    private fun value(element: PsiElement): GNode? = when (element) {
        is KtStringTemplateExpression -> string(element)
        is KtBinaryExpression -> binary(element)
        is KtCollectionLiteralExpression -> GList(lines.line(element), element.getInnerExpressions().map { node(it) })
        is KtProperty -> property(element)
        else -> null
    }

    /** Statements of the script and of blocks and lambdas; nested functions, classes and type aliases are not read. */
    private fun container(element: PsiElement): GNode? = when (element) {
        is KtScriptInitializer -> node(element.body)
        is KtLambdaExpression -> block(element.bodyExpression, lines.line(element))
        is KtBlockExpression -> block(element, lines.line(element))
        is KtNamedFunction, is KtClassOrObject, is KtTypeAlias -> GOther(lines.line(element), emptyList())
        else -> null
    }

    /** Expressions that only wrap another one: parentheses, annotations, casts, `!!`. */
    private fun wrapper(element: PsiElement): GNode? = when (element) {
        is KtParenthesizedExpression -> node(element.expression)
        is KtAnnotatedExpression -> node(element.baseExpression)
        is KtBinaryExpressionWithTypeRHS -> node(element.left)
        is KtPostfixExpression -> node(element.baseExpression)
        else -> null
    }

    private fun other(element: PsiElement): GNode = GOther(lines.line(element), element.children.map { node(it) })

    private fun qualified(element: KtQualifiedExpression): GNode {
        val receiver = node(element.receiverExpression)
        return when (val selector = element.selectorExpression) {
            is KtCallExpression -> call(receiver, selector)
            is KtNameReferenceExpression -> GMember(lines.line(element), receiver, selector.getReferencedName())
            else -> GOther(lines.line(element), listOf(receiver, node(selector)))
        }
    }

    private fun call(receiver: GNode?, element: KtCallExpression): GNode {
        val line = lines.line(element)
        val args = element.valueArgumentList?.arguments.orEmpty().map {
            GArg(it.getArgumentName()?.asName?.asString(), node(it.getArgumentExpression()))
        }
        val lambda = element.lambdaArguments.lastOrNull()?.getLambdaExpression()
        val block = lambda?.let { block(it.bodyExpression, lines.line(it)) }
        val name = calleeName(element.calleeExpression)
            ?: return GOther(line, listOfNotNull(receiver, node(element.calleeExpression)) + args.map { it.value } + listOfNotNull(block))
        return GCall(line, receiver, name, args, block)
    }

    /** `name(...)`, or the string form `"implementation"(...)` that Gradle accepts for any configuration. */
    private fun calleeName(callee: KtExpression?): String? = when (callee) {
        is KtNameReferenceExpression -> callee.getReferencedName()
        is KtStringTemplateExpression -> string(callee).literal
        else -> null
    }

    private fun block(body: KtBlockExpression?, line: Int): GBlock = GBlock(line, body?.statements.orEmpty().map { node(it) })

    private fun string(element: KtStringTemplateExpression): GStr {
        val parts = element.entries.map { entry ->
            val expression = entry.expression
            when {
                expression != null -> GExpr(node(expression))
                entry is KtEscapeStringTemplateEntry -> GText(entry.unescapedValue)
                else -> GText(entry.text)
            }
        }
        return GStr(lines.line(element), parts)
    }

    private fun binary(element: KtBinaryExpression): GNode {
        val line = lines.line(element)
        val left = node(element.left)
        val right = node(element.right)
        return when (element.operationToken) {
            KtTokens.EQ -> GAssign(line, left, right, "=")
            KtTokens.PLUSEQ -> GAssign(line, left, right, "+=")
            else -> GOther(line, listOf(left, right))
        }
    }

    /** `val x by getting { ... }` configures the source set `x`; `val x = value` is kept as an assignment. */
    private fun property(element: KtProperty): GNode {
        val line = lines.line(element)
        val name = element.name ?: return other(element)
        val delegate = element.delegateExpression
        if (delegate is KtCallExpression) {
            val lambda = delegate.lambdaArguments.lastOrNull()?.getLambdaExpression()
            return GCall(line, null, name, emptyList(), lambda?.let { block(it.bodyExpression, lines.line(it)) })
        }
        if (delegate != null) return GCall(line, null, name, emptyList(), null)
        val initializer = element.initializer ?: return GOther(line, emptyList())
        return GAssign(line, GRef(line, name), node(initializer), "=")
    }
}
