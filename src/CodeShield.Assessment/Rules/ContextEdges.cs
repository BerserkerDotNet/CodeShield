using CodeShield.Abstractions;
using CodeShield.Core.Graph;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Rules;

/// <summary>Per-context dependency edges of the current and baseline snapshots, with context identity carried through renames.</summary>
internal static class ContextEdges
{
    /// <summary>The per-context graphs; a graph without context detail is treated as one "repository" context.</summary>
    public static IReadOnlyList<ContextGraph> ContextsOf(ModuleGraphFacts graph) => new DependencyGraph(graph).Contexts;

    public static IReadOnlyList<(string From, string To)> Of(ContextGraph context, ModuleGraphFacts graph, bool runtimeImports) =>
        runtimeImports ? new PackageBoundaries(graph.Modules).RuntimeImports(context) : [.. context.Edges.Select(edge => (edge.From, edge.To))];

    /// <summary>
    /// Baseline edges for a current context, rewritten to current paths. A context that did not exist in the baseline
    /// is compared with every baseline edge, because the dependencies themselves are not new.
    /// </summary>
    public static HashSet<(string From, string To)> Baseline(RuleInput input, string contextId, bool runtimeImports)
    {
        var graph = input.Baseline.Graph;
        var baselineId = BaselineId(input.Map, contextId);
        var all = ContextsOf(graph);
        var sameContext = all.Where(context => context.ContextId == baselineId).ToList();
        var contexts = sameContext.Count > 0 ? sameContext : all;
        return MapToCurrent(input, contexts.SelectMany(context => Of(context, graph, runtimeImports)));
    }

    /// <summary>The graph context of an analysis context: the framework of "project|framework", or the tsconfig path itself.</summary>
    public static string GraphKey(string contextId)
    {
        var bar = contextId.IndexOf('|', StringComparison.Ordinal);
        return bar < 0 ? contextId : contextId[(bar + 1)..];
    }

    /// <summary>Maps a current graph context id to the id it had in the baseline; tsconfig paths follow renames.</summary>
    public static string BaselineId(ChangeMap map, string graphKey) => map.BaselinePathOf(graphKey) ?? graphKey;

    /// <summary>Every baseline edge rewritten to current paths, across all contexts.</summary>
    public static HashSet<(string From, string To)> BaselineAll(RuleInput input, bool runtimeImports) =>
        MapToCurrent(input, ContextsOf(input.Baseline.Graph).SelectMany(context => Of(context, input.Baseline.Graph, runtimeImports)));

    private static HashSet<(string From, string To)> MapToCurrent(RuleInput input, IEnumerable<(string From, string To)> edges)
    {
        var mapped = new HashSet<(string, string)>();
        foreach (var (from, to) in edges)
        {
            if (input.Map.CurrentPathOf(from) is string a && input.Map.CurrentPathOf(to) is string b)
            {
                mapped.Add((a, b));
            }
        }

        return mapped;
    }
}

