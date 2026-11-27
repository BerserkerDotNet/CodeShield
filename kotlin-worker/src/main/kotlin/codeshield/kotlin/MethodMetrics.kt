package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.com.intellij.psi.tree.IElementType
import org.jetbrains.kotlin.com.intellij.psi.util.PsiTreeUtil
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtBinaryExpression
import org.jetbrains.kotlin.psi.KtCatchClause
import org.jetbrains.kotlin.psi.KtClassOrObject
import org.jetbrains.kotlin.psi.KtIfExpression
import org.jetbrains.kotlin.psi.KtLambdaExpression
import org.jetbrains.kotlin.psi.KtLoopExpression
import org.jetbrains.kotlin.psi.KtNamedFunction
import org.jetbrains.kotlin.psi.KtTreeVisitorVoid
import org.jetbrains.kotlin.psi.KtWhenEntry
import org.jetbrains.kotlin.psi.KtWhenExpression

/** Visits one body without descending into nested functions, lambdas and classes, which are measured as their own units. */
private abstract class OwnBodyVisitor : KtTreeVisitorVoid() {
    override fun visitNamedFunction(function: KtNamedFunction) = Unit

    override fun visitLambdaExpression(lambdaExpression: KtLambdaExpression) = Unit

    override fun visitClassOrObject(classOrObject: KtClassOrObject) = Unit
}

/**
 * Complexity and nesting of one body (docs/specification.md, "Cyclomatic complexity" and "Nesting"). Kotlin `if` serves
 * as both statement and conditional expression; `when` is the switch and each non-`else` entry is a case; `?:` (elvis)
 * is the null-coalescing operator. Nested functions, lambdas and classes are measured as their own units.
 */
private class Meter(private val lines: LineIndex) : OwnBodyVisitor() {
    val decisions = ArrayList<DecisionEvidence>()
    val nestingPoints = ArrayList<NestingEvidence>()
    var maxDepth = 0
        private set
    private var depth = 0

    override fun visitIfExpression(expression: KtIfExpression) {
        decide("if", expression)
        nest("if", expression)
        at(expression.condition, depth)
        at(expression.then, depth + 1)
        val otherwise = expression.`else`
        at(otherwise, if (otherwise is KtIfExpression) depth else depth + 1)
    }

    override fun visitLoopExpression(loopExpression: KtLoopExpression) {
        decide("loop", loopExpression)
        nest("loop", loopExpression)
        visitParts(loopExpression, loopExpression.body)
    }

    override fun visitCatchSection(catchClause: KtCatchClause) {
        decide("catch", catchClause)
        nest("catch", catchClause)
        visitParts(catchClause, catchClause.catchBody)
    }

    override fun visitWhenExpression(expression: KtWhenExpression) {
        nest("when", expression)
        var child = expression.firstChild
        while (child != null) {
            at(child, if (child is KtWhenEntry) depth + 1 else depth)
            child = child.nextSibling
        }
    }

    override fun visitWhenEntry(jetWhenEntry: KtWhenEntry) {
        if (!jetWhenEntry.isElse) decide("case", jetWhenEntry)
        super.visitWhenEntry(jetWhenEntry)
    }

    override fun visitBinaryExpression(expression: KtBinaryExpression) {
        if (expression.operationToken in logical) decide("logical", expression.operationReference)
        super.visitBinaryExpression(expression)
    }

    private fun decide(kind: String, element: PsiElement) {
        decisions.add(DecisionEvidence(kind, lines.line(element)))
    }

    private fun nest(kind: String, element: PsiElement) {
        nestingPoints.add(NestingEvidence(kind, lines.line(element), depth + 1))
        maxDepth = maxOf(maxDepth, depth + 1)
    }

    /** Visits [element] at nesting [level], then restores the current depth. */
    private fun at(element: PsiElement?, level: Int) {
        val saved = depth
        depth = level
        element?.accept(this)
        depth = saved
    }

    /** Visits the children of a construct; the child holding [body] is one level deeper. */
    private fun visitParts(construct: PsiElement, body: PsiElement?) {
        var child = construct.firstChild
        while (child != null) {
            at(child, if (body != null && PsiTreeUtil.isAncestor(child, body, false)) depth + 1 else depth)
            child = child.nextSibling
        }
    }

    private companion object {
        val logical = setOf<IElementType>(KtTokens.ANDAND, KtTokens.OROR, KtTokens.ELVIS)
    }
}

/** One MethodFact per executable unit with a body: functions, constructors, accessors, `init` blocks and lambdas. */
internal object MethodMetrics {
    private val order = compareBy<MethodFact>({ it.span.startLine }, { it.span.startColumn }).thenBy { it.unitId }

    fun collect(source: SourceUnit): List<MethodFact> =
        source.units.units.filter(Units::isMethod).map { fact(source, it) }.sortedWith(order)

    private fun fact(source: SourceUnit, unit: PsiElement): MethodFact {
        val meter = Meter(source.parsed.lines)
        Units.body(unit)?.accept(meter)
        val range = unit.textRange
        return MethodFact(
            unitId = source.units.unitId(unit),
            name = source.units.nameOf(unit)?.short ?: UnitNames.ANONYMOUS,
            span = source.code.span(unit),
            complexity = 1 + meter.decisions.size,
            nesting = meter.maxDepth,
            codeLines = source.code.count(range.startOffset, range.endOffset),
            parameterCount = Units.parameterCount(unit),
            decisions = meter.decisions.sortedBy { it.line },
            nestingPoints = meter.nestingPoints.sortedWith(compareBy({ it.line }, { it.depth })),
        )
    }
}
