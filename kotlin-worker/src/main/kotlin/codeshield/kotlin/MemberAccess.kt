package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.com.intellij.psi.tree.IElementType
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtBinaryExpression
import org.jetbrains.kotlin.psi.KtCallExpression
import org.jetbrains.kotlin.psi.KtCallableReferenceExpression
import org.jetbrains.kotlin.psi.KtClassOrObject
import org.jetbrains.kotlin.psi.KtExpression
import org.jetbrains.kotlin.psi.KtLambdaExpression
import org.jetbrains.kotlin.psi.KtNameReferenceExpression
import org.jetbrains.kotlin.psi.KtNamedDeclaration
import org.jetbrains.kotlin.psi.KtNamedFunction
import org.jetbrains.kotlin.psi.KtQualifiedExpression
import org.jetbrains.kotlin.psi.KtThisExpression
import org.jetbrains.kotlin.psi.KtUnaryExpression
import org.jetbrains.kotlin.psi.KtUserType
import org.jetbrains.kotlin.psi.KtValueArgumentName

/**
 * Where one name reference sits: the call it is the callee of, the callable reference it names, and the receiver it is
 * selected from. [outer] is the whole access (`this.x` rather than `x`), which an assignment or increment targets.
 */
private class AccessSite(val reference: KtNameReferenceExpression) {
    val call: KtCallExpression? = (reference.parent as? KtCallExpression)?.takeIf { it.calleeExpression == reference }
    val callable: KtCallableReferenceExpression? =
        (reference.parent as? KtCallableReferenceExpression)?.takeIf { it.callableReference == reference }
    private val selected: KtExpression = call ?: reference
    private val qualified: KtQualifiedExpression? =
        (selected.parent as? KtQualifiedExpression)?.takeIf { it.selectorExpression == selected }
    val outer: KtExpression = qualified ?: selected

    /** The explicit receiver, or null when the name is used unqualified. */
    val receiver: KtExpression? get() = qualified?.receiverExpression ?: callable?.receiverExpression

    val isExplicitThis: Boolean get() = receiver is KtThisExpression

    /** Unqualified, or qualified by `this` (unlabeled or labeled with the class name). */
    fun targetsClass(className: String?): Boolean {
        val target = receiver ?: return true
        if (target !is KtThisExpression) return false
        val label = target.getLabelName()
        return label == null || label == className
    }
}

/** Whether an access reads, writes or both reads and writes its target. */
private object Usage {
    private val increments = setOf<IElementType>(KtTokens.PLUSPLUS, KtTokens.MINUSMINUS)

    fun isRead(outer: KtExpression): Boolean = binaryOperator(outer) != KtTokens.EQ

    fun isWrite(outer: KtExpression): Boolean {
        val operator = binaryOperator(outer) ?: return incremented(outer)
        return operator == KtTokens.EQ || KtTokens.AUGMENTED_ASSIGNMENTS.contains(operator)
    }

    private fun binaryOperator(outer: KtExpression): IElementType? =
        (outer.parent as? KtBinaryExpression)?.takeIf { it.left == outer }?.operationToken

    private fun incremented(outer: KtExpression): Boolean =
        (outer.parent as? KtUnaryExpression)?.operationReference?.getReferencedNameElementType() in increments
}

/**
 * The instance state and sibling nodes one LCOM4 node touches, resolved by name within the class. Lambdas, nested
 * functions and nested classes are excluded, as in the other languages; locals and parameters shadow members.
 */
internal class MemberAccess(private val model: ClassModel, private val member: CohesionMember) {
    val fields = HashSet<String>()
    val calls = HashSet<String>()
    private val shadowed = HashSet<String>()
    private val references = ArrayList<KtNameReferenceExpression>()

    init {
        member.declaration.valueParameters.forEach { parameter -> parameter.name?.let(shadowed::add) }
        member.declaration.bodyExpression?.let(::collect)
        references.forEach(::record)
    }

    private fun collect(element: PsiElement) {
        if (element is KtNameReferenceExpression) references.add(element)
        if (element is KtNamedDeclaration) element.name?.let(shadowed::add)
        if (isOpaque(element)) return
        var child = element.firstChild
        while (child != null) {
            collect(child)
            child = child.nextSibling
        }
    }

    private fun isOpaque(element: PsiElement): Boolean =
        element is KtLambdaExpression || element is KtNamedFunction || element is KtClassOrObject

    private fun record(reference: KtNameReferenceExpression) {
        if (isTypeOrArgumentName(reference)) return
        val site = AccessSite(reference)
        val name = reference.getReferencedName()
        if (!site.targetsClass(model.className) || isShadowed(name, site)) return
        val field = backingFieldOf(name, site)
        if (field != null) fields.add(field) else resolve(name, site)
    }

    private fun isTypeOrArgumentName(reference: KtNameReferenceExpression): Boolean =
        reference.parent is KtUserType || reference.parent is KtValueArgumentName

    private fun isShadowed(name: String, site: AccessSite): Boolean = !site.isExplicitThis && name in shadowed

    /** `field` inside an accessor is the backing field of the accessor's own property. */
    private fun backingFieldOf(name: String, site: AccessSite): String? =
        if (name == BackingState.FIELD && site.receiver == null) member.property?.name else null

    private fun resolve(name: String, site: AccessSite) {
        val call = site.call
        when {
            call != null && model.hasFunction(name) -> calls.addAll(model.functionNodes(name, call.valueArguments.size))
            site.callable != null && model.hasFunction(name) -> calls.addAll(model.functionNodes(name, null))
            model.isProperty(name) -> property(name, site.outer)
        }
    }

    private fun property(name: String, outer: KtExpression) {
        if (Usage.isRead(outer)) touch(name, write = false)
        if (Usage.isWrite(outer)) touch(name, write = true)
    }

    /** A custom accessor is a call to its node; a default accessor touches the backing field directly. */
    private fun touch(name: String, write: Boolean) {
        val accessor = model.accessorNode(name, write)
        if (accessor != null) calls.add(accessor) else if (model.isState(name)) fields.add(name)
    }
}
