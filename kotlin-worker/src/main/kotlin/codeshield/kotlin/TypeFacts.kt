package codeshield.kotlin

import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtClass
import org.jetbrains.kotlin.psi.KtClassOrObject
import org.jetbrains.kotlin.psi.KtEnumEntry
import org.jetbrains.kotlin.psi.KtObjectDeclaration

/**
 * The types counted for abstractness (docs/specification.md, "Main sequence"): classes, interfaces and named objects.
 * Interfaces and abstract or sealed classes are abstract. Enums, annotations, companions and object expressions are
 * not counted. A Kotlin Multiplatform `expect` declaration and its `actual` counterparts are one type, so each
 * qualified name is counted once, preferring the `expect` declaration.
 */
internal object TypeFacts {
    private class Candidate(val key: String, val fact: TypeFact, val expect: Boolean)

    fun collect(sources: List<SourceUnit>): List<TypeFact> {
        val chosen = HashMap<String, Candidate>()
        for (source in sources) {
            source.units.units.filter(::isType).forEach { offer(chosen, candidate(source, it as KtClassOrObject)) }
        }
        return chosen.values.map { it.fact }.sortedBy { it.unitId }
    }

    fun isType(element: PsiElement): Boolean = when (element) {
        is KtEnumEntry -> false
        is KtClass -> !element.isEnum() && !element.isAnnotation()
        is KtObjectDeclaration -> !element.isCompanion() && !element.isObjectLiteral()
        else -> false
    }

    private fun isAbstract(type: KtClassOrObject): Boolean =
        type is KtClass && (type.isInterface() || type.isSealed() || type.hasModifier(KtTokens.ABSTRACT_KEYWORD))

    private fun candidate(source: SourceUnit, type: KtClassOrObject): Candidate {
        val unitId = source.units.unitId(type)
        val key = type.fqName?.asString() ?: unitId
        return Candidate(key, TypeFact(unitId, source.path, isAbstract(type)), type.hasModifier(KtTokens.EXPECT_KEYWORD))
    }

    private fun offer(chosen: MutableMap<String, Candidate>, candidate: Candidate) {
        val current = chosen[candidate.key]
        if (current == null || preferred(candidate, current)) chosen[candidate.key] = candidate
    }

    private fun preferred(candidate: Candidate, current: Candidate): Boolean =
        if (candidate.expect != current.expect) candidate.expect else candidate.fact.unitId < current.fact.unitId
}
