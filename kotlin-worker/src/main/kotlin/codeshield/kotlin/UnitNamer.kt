package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.psi.KtAnonymousInitializer
import org.jetbrains.kotlin.psi.KtClassOrObject
import org.jetbrains.kotlin.psi.KtDeclarationWithBody
import org.jetbrains.kotlin.psi.KtFunctionLiteral
import org.jetbrains.kotlin.psi.KtNamedFunction
import org.jetbrains.kotlin.psi.KtObjectDeclaration
import org.jetbrains.kotlin.psi.KtParameter
import org.jetbrains.kotlin.psi.KtProperty
import org.jetbrains.kotlin.psi.KtPropertyAccessor
import org.jetbrains.kotlin.psi.KtSecondaryConstructor
import org.jetbrains.kotlin.psi.KtTypeReference

/**
 * The name of one unit. [qualified] is unique within its file; [stable] is false when the name embeds a position
 * (anonymous units, units declared inside a function, duplicate names), so it would change when lines move.
 */
internal data class UnitName(val qualified: String, val short: String, val stable: Boolean)

/** Executable bodies and named declarations that receive unit IDs. */
internal object Units {
    /** The body of an executable unit: a function, constructor, accessor, lambda or `init` block. */
    fun body(element: PsiElement): PsiElement? = when (element) {
        is KtDeclarationWithBody -> element.bodyExpression
        is KtAnonymousInitializer -> element.body
        else -> null
    }

    fun isMethod(element: PsiElement): Boolean = body(element) != null

    fun isUnit(element: PsiElement): Boolean = element is KtClassOrObject || isMethod(element)

    /** Declared value parameters; an extension receiver and a lambda's implicit `it` are not parameters. */
    fun parameterCount(element: PsiElement): Int = (element as? KtDeclarationWithBody)?.valueParameters?.size ?: 0
}

/** The local (scope-relative) and short names of a unit. */
internal object UnitNames {
    const val ANONYMOUS = "<anonymous>"
    const val LAMBDA = "<lambda>"
    const val OBJECT = "<object>"
    private const val INIT = "<init>"
    private val anonymous = setOf(ANONYMOUS, LAMBDA, OBJECT)

    fun local(element: PsiElement): String = when (element) {
        is KtClassOrObject -> className(element)
        is KtNamedFunction -> functionName(element)
        is KtSecondaryConstructor -> "constructor" + signature(element.valueParameters)
        is KtPropertyAccessor -> accessorName(element)
        is KtFunctionLiteral -> lambdaName(element)
        else -> INIT
    }

    fun short(element: PsiElement, local: String): String = when (element) {
        is KtNamedFunction -> element.name ?: local
        is KtSecondaryConstructor -> "constructor"
        else -> local
    }

    fun isAnonymous(local: String): Boolean = local in anonymous

    private fun className(element: KtClassOrObject): String =
        element.name ?: if (element is KtObjectDeclaration && element.isCompanion()) "Companion" else OBJECT

    private fun functionName(function: KtNamedFunction): String {
        val name = function.name ?: return inferred(function) ?: ANONYMOUS
        val receiver = function.receiverTypeReference?.let { typeText(it) + "." } ?: ""
        return receiver + name + signature(function.valueParameters)
    }

    private fun lambdaName(literal: KtFunctionLiteral): String = inferred(literal.parent) ?: LAMBDA

    private fun accessorName(accessor: KtPropertyAccessor): String =
        "${accessor.property.name ?: "?"}.${if (accessor.isGetter) "get" else "set"}"

    private fun signature(parameters: List<KtParameter>): String =
        parameters.joinToString(",", "(", ")") { typeText(it.typeReference) + if (it.isVarArg) "..." else "" }

    private fun typeText(reference: KtTypeReference?): String =
        reference?.let { (it.typeElement ?: it).text.filterNot(Char::isWhitespace) } ?: "?"

    /** The property name when [expression] is the whole initializer of a property, as in `val handler = { ... }`. */
    private fun inferred(expression: PsiElement?): String? {
        val property = expression?.parent as? KtProperty ?: return null
        return if (property.initializer == expression) property.name else null
    }
}

/**
 * Assigns every class, object and executable unit of a file a unit ID `path#Qualified.name(Param,Types)`. Units declared
 * inside a function and anonymous units carry `@line`; a repeated name carries `:column`.
 */
internal class UnitNamer(private val parsed: ParsedFile, private val code: CodeLines) {
    private class Scope(val qualified: String, val isFunction: Boolean, val stable: Boolean)

    private val names = HashMap<PsiElement, UnitName>()
    private val used = HashSet<String>()
    private val ordered = ArrayList<PsiElement>()

    /** Every unit in source order. */
    val units: List<PsiElement> get() = ordered

    init {
        walk(parsed.file, Scope("", isFunction = false, stable = true))
    }

    fun nameOf(element: PsiElement): UnitName? = names[element]

    fun unitId(element: PsiElement): String = "${parsed.path}#${names[element]?.qualified ?: MODULE}"

    /**
     * The unit ID of the innermost stable method or [eligible] class around [offset], or "" when there is none.
     * Positional IDs are skipped because they cannot be matched against a baseline.
     */
    fun enclosingStableUnitId(offset: Int, eligible: (PsiElement) -> Boolean): String {
        var element: PsiElement? = parsed.file.findElementAt(offset)
        while (element != null) {
            if (isStableOwner(element, eligible)) return unitId(element)
            element = element.parent
        }
        return ""
    }

    private fun isStableOwner(element: PsiElement, eligible: (PsiElement) -> Boolean): Boolean {
        val name = names[element] ?: return false
        return name.stable && (Units.isMethod(element) || eligible(element))
    }

    private fun walk(element: PsiElement, scope: Scope) {
        var child = element.firstChild
        while (child != null) {
            visit(child, scope)
            child = child.nextSibling
        }
    }

    private fun visit(element: PsiElement, scope: Scope) {
        if (!Units.isUnit(element)) return walk(element, scope)
        val name = makeName(element, scope)
        names[element] = name
        ordered.add(element)
        walk(element, Scope(name.qualified, Units.isMethod(element), name.stable))
    }

    private fun makeName(element: PsiElement, scope: Scope): UnitName {
        val local = UnitNames.local(element)
        val positional = isPositional(local, scope)
        val span = code.span(element)
        val base = join(scope.qualified, local) + if (positional) "@${span.startLine}" else ""
        val duplicate = !used.add(base)
        val qualified = if (duplicate) "$base:${span.startColumn}" else base
        return UnitName(qualified, UnitNames.short(element, local), scope.stable && !positional && !duplicate)
    }

    private fun isPositional(local: String, scope: Scope): Boolean = UnitNames.isAnonymous(local) || scope.isFunction

    private fun join(parent: String, local: String): String = if (parent.isEmpty()) local else "$parent.$local"

    private companion object {
        const val MODULE = "<module>"
    }
}
