using CodeShield.Abstractions;

namespace CodeShield.Core.Graph;

/// <summary>Package ownership of files: the nearest ancestor package.json owns a file, so imports across it are not runtime edges of one package.</summary>
public sealed class PackageBoundaries(IEnumerable<string> modules)
{
    private readonly IReadOnlyList<string> _packages =
        [.. modules.Where(module => module.EndsWith("package.json", StringComparison.Ordinal))];

    /// <summary>Runtime imports: not type-only and staying inside one package.</summary>
    public IReadOnlyList<(string From, string To)> RuntimeImports(ContextGraph context)
    {
        var edges = new List<(string From, string To)>();
        foreach (var import in context.FileImports)
        {
            if (!import.TypeOnly && OwnerOf(import.From) == OwnerOf(import.To))
            {
                edges.Add((import.From, import.To));
            }
        }

        return edges;
    }

    private string? OwnerOf(string file) =>
        _packages.Where(package => IsAncestor(package, file)).OrderByDescending(package => package.Length).FirstOrDefault();

    private static bool IsAncestor(string package, string file)
    {
        var slash = package.LastIndexOf('/');
        return slash < 0 || file.StartsWith(package[..(slash + 1)], StringComparison.Ordinal);
    }
}
