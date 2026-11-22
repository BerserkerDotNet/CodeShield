using CodeShield.Abstractions;

namespace CodeShield.CSharp.Cohesion;

/// <summary>Connected components over the cohesion nodes (union-find), in a deterministic order.</summary>
internal static class ComponentGraph
{
    public static IReadOnlyList<IReadOnlyList<string>> Components(IReadOnlyList<CohesionNode> nodes, IEnumerable<CohesionEdge> edges)
    {
        var parent = nodes.ToDictionary(node => node.Id, node => node.Id, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            parent[Find(parent, edge.From)] = Find(parent, edge.To);
        }

        return [.. nodes
            .GroupBy(node => Find(parent, node.Id))
            .Select(group => (IReadOnlyList<string>)[.. group.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal)])
            .OrderBy(component => component[0], StringComparer.Ordinal)];
    }

    private static string Find(Dictionary<string, string> parent, string id)
    {
        while (parent[id] != id)
        {
            parent[id] = parent[parent[id]];
            id = parent[id];
        }

        return id;
    }
}
