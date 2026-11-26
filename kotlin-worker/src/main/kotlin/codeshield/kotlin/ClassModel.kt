package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.com.intellij.psi.util.PsiTreeUtil
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtBlockExpression
import org.jetbrains.kotlin.psi.KtClass
import org.jetbrains.kotlin.psi.KtDeclarationWithBody
import org.jetbrains.kotlin.psi.KtEnumEntry
import org.jetbrains.kotlin.psi.KtNameReferenceExpression
import org.jetbrains.kotlin.psi.KtNamedFunction
import org.jetbrains.kotlin.psi.KtProperty
import org.jetbrains.kotlin.psi.KtPropertyAccessor

/** Classes measured by LCOM4. Interfaces, enums, annotations and objects (singletons) are not. */
internal object CohesionClasses {
    fun isEligible(element: PsiElement): Boolean =
        element is KtClass && element !is KtEnumEntry && !element.isInterface() && !element.isEnum() && !element.isAnnotation()
}

/** Whether a class-body property holds instance state: a backing field or a delegate. */
internal object BackingState {
    fun of(property: KtProperty): Boolean =
        isConcrete(property) && (holdsValue(property) || hasDefaultAccessor(property) || usesField(property))

    private fun isConcrete(property: KtProperty): Boolean =
        !property.hasModifier(KtTokens.ABSTRACT_KEYWORD) && property.receiverTypeReference == null

    private fun holdsValue(property: KtProperty): Boolean = property.hasInitializer() || property.hasDelegate()

    /** A missing or bodiless accessor is the default one, which reads or writes the backing field. */
    private fun hasDefaultAccessor(property: KtProperty): Boolean {
        val getter = property.getter
        if (getter == null || !getter.hasBody()) return true
        val setter = property.setter
        return property.isVar && (setter == null || !setter.hasBody())
    }

    private fun usesField(property: KtProperty): Boolean = property.accessors.any { mentions(it, FIELD) }

    fun mentions(scope: PsiElement, name: String): Boolean =
        PsiTreeUtil.findChildrenOfType(scope, KtNameReferenceExpression::class.java).any { it.getReferencedName() == name }

    const val FIELD = "field"
}

/** An LCOM4 node: a concrete member function or property accessor with a non-empty body. */
internal class CohesionMember(val id: String, val name: String, val declaration: KtDeclarationWithBody, val property: KtProperty?)

/** The instance state and LCOM4 nodes of one class, with syntactic name resolution for its members. */
internal class ClassModel(klass: KtClass, private val units: UnitNamer) {
    val className: String? = klass.name
    val state = HashSet<String>()
    private val properties = HashSet<String>()
    private val functions = HashMap<String, MutableList<KtNamedFunction>>()
    private val nodeIds = HashMap<PsiElement, String>()
    private val getters = HashMap<String, String>()
    private val setters = HashMap<String, String>()
    private val nodes = ArrayList<CohesionMember>()

    /** The nodes sorted by unit ID. */
    val members: List<CohesionMember>

    init {
        klass.primaryConstructorParameters.filter { it.hasValOrVar() }.forEach { addState(it.name) }
        klass.declarations.forEach(::addDeclaration)
        members = nodes.sortedBy { it.id }
    }

    fun hasFunction(name: String): Boolean = name in functions

    fun isProperty(name: String): Boolean = name in properties

    fun isState(name: String): Boolean = name in state

    /** Node IDs of the member functions named [name] that accept [arguments] arguments (any count when null). */
    fun functionNodes(name: String, arguments: Int?): List<String> =
        functions[name].orEmpty().filter { arguments == null || Arity.accepts(it, arguments) }.mapNotNull { nodeIds[it] }

    /** The node ID of the accessor a read or write of property [name] invokes, or null when it is a default accessor. */
    fun accessorNode(name: String, write: Boolean): String? = (if (write) setters else getters)[name]

    private fun addState(name: String?) {
        if (name == null) return
        properties.add(name)
        state.add(name)
    }

    private fun addDeclaration(declaration: PsiElement) {
        when (declaration) {
            is KtNamedFunction -> addFunction(declaration)
            is KtProperty -> addProperty(declaration)
        }
    }

    private fun addFunction(function: KtNamedFunction) {
        val name = function.name ?: return
        functions.getOrPut(name) { ArrayList() }.add(function)
        addNode(function, null)
    }

    private fun addProperty(property: KtProperty) {
        val name = property.name ?: return
        properties.add(name)
        if (BackingState.of(property)) state.add(name)
        property.accessors.forEach { accessor -> addAccessor(name, property, accessor) }
    }

    private fun addAccessor(name: String, property: KtProperty, accessor: KtPropertyAccessor) {
        val id = addNode(accessor, property) ?: return
        (if (accessor.isGetter) getters else setters)[name] = id
    }

    private fun addNode(declaration: KtDeclarationWithBody, property: KtProperty?): String? {
        if (!hasContent(declaration.bodyExpression)) return null
        val id = units.unitId(declaration)
        nodeIds[declaration] = id
        nodes.add(CohesionMember(id, units.nameOf(declaration)?.short ?: UnitNames.ANONYMOUS, declaration, property))
        return id
    }

    private fun hasContent(body: PsiElement?): Boolean = body != null && (body !is KtBlockExpression || body.statements.isNotEmpty())
}

/** Whether a call with a given number of arguments can bind to a function, ignoring types. */
internal object Arity {
    fun accepts(function: KtNamedFunction, arguments: Int): Boolean {
        val parameters = function.valueParameters
        val required = parameters.count { !it.hasDefaultValue() && !it.isVarArg }
        if (parameters.any { it.isVarArg }) return arguments >= required
        return arguments in required..parameters.size
    }
}
