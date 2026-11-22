using CodeShield.Abstractions;

namespace CodeShield.Core.Graph;

/// <summary>Typed queries over one snapshot's per-context dependency graph; a graph without context detail is one "repository" context.</summary>
public sealed class DependencyGraph
{
    public const string RepositoryContext = "repository";

    public DependencyGraph(ModuleGraphFacts graph)
    {
        Contexts = graph.Contexts.Count > 0
            ? graph.Contexts
            : [new ContextGraph(RepositoryContext, graph.Edges, graph.FileImports)];
    }

    public IReadOnlyList<ContextGraph> Contexts { get; }

    /// <summary>Distinct modules in any context that depend on the module.</summary>
    public int Afferent(string module) =>
        Contexts.SelectMany(context => context.Edges).Where(edge => edge.To == module && edge.From != module).Select(edge => edge.From).Distinct().Count();

    /// <summary>Distinct dependencies of the module in one context; an unknown context falls back to all contexts.</summary>
    public int Efferent(string contextId, string module)
    {
        var own = Contexts.FirstOrDefault(context => context.ContextId == contextId)?.Edges
            ?? [.. Contexts.SelectMany(context => context.Edges)];
        return own.Where(edge => edge.From == module && edge.To != module).Select(edge => edge.To).Distinct().Count();
    }
}
