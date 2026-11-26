package codeshield.kotlin

import org.jetbrains.kotlin.psi.KtClass

/**
 * LCOM4 for one class (docs/specification.md, "Cohesion"): nodes are member functions and custom accessors with a
 * body; two nodes are connected when one calls the other or both touch the same instance state. Classes without
 * state, or with fewer than two nodes, are not applicable.
 */
internal object Cohesion {
    fun collect(source: SourceUnit): List<ClassFact> =
        source.units.units.filter(CohesionClasses::isEligible).map { fact(source, it as KtClass) }.sortedBy { it.unitId }

    private fun fact(source: SourceUnit, klass: KtClass): ClassFact {
        val model = ClassModel(klass, source.units)
        val nodes = model.members.map { CohesionNode(it.id, it.name) }
        val applicable = model.state.isNotEmpty() && nodes.size >= 2
        val edges = if (applicable) CohesionEdges.of(model) else emptyList()
        return ClassFact(
            unitId = source.units.unitId(klass),
            name = source.units.nameOf(klass)?.short ?: UnitNames.OBJECT,
            locations = listOf(ClassLocation(source.path, source.code.span(klass))),
            applicable = applicable,
            nodes = nodes,
            edges = edges,
            components = if (applicable) Components.of(nodes.map { it.id }, edges) else emptyList(),
        )
    }
}

/** Call edges and shared-state edges between the nodes of one class, deduplicated and sorted. */
private object CohesionEdges {
    private const val CALL = "call"
    private val order = compareBy<CohesionEdge>({ it.from }, { it.to }, { it.reason })

    fun of(model: ClassModel): List<CohesionEdge> {
        val edges = HashSet<CohesionEdge>()
        val touching = HashMap<String, MutableList<String>>()
        for (member in model.members) {
            val access = MemberAccess(model, member)
            access.calls.filter { it != member.id }.forEach { edges.add(edge(member.id, it, CALL)) }
            access.fields.forEach { field -> touching.getOrPut(field) { ArrayList() }.add(member.id) }
        }
        touching.forEach { (field, ids) -> star(ids.distinct().sorted(), "field:$field", edges) }
        return edges.sortedWith(order)
    }

    /** Connects the first node to every other one: enough for connectivity without a quadratic edge list. */
    private fun star(ids: List<String>, reason: String, into: MutableSet<CohesionEdge>) {
        ids.drop(1).forEach { into.add(edge(ids[0], it, reason)) }
    }

    private fun edge(a: String, b: String, reason: String): CohesionEdge =
        if (a < b) CohesionEdge(a, b, reason) else CohesionEdge(b, a, reason)
}

/** Connected components by union-find; each component is sorted, and components are ordered by their first node. */
private object Components {
    fun of(ids: List<String>, edges: List<CohesionEdge>): List<List<String>> {
        val parent = HashMap<String, String>()
        ids.forEach { parent[it] = it }
        edges.forEach { union(parent, it.from, it.to) }
        return ids.groupBy { root(parent, it) }.values.map { it.sorted() }.sortedBy { it.first() }
    }

    private fun root(parent: MutableMap<String, String>, id: String): String {
        var current = id
        while (parent.getValue(current) != current) current = parent.getValue(current)
        return current
    }

    private fun union(parent: MutableMap<String, String>, a: String, b: String) {
        val left = root(parent, a)
        val right = root(parent, b)
        if (left != right) parent[maxOf(left, right)] = minOf(left, right)
    }
}
