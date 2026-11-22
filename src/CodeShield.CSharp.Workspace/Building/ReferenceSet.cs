namespace CodeShield.CSharp.Building;

/// <summary>Reference assembly paths plus descriptions of required dependencies that could not be found.</summary>
internal sealed record ReferenceSet(IReadOnlyList<string> Assemblies, IReadOnlyList<string> Missing)
{
    public static ReferenceSet Empty { get; } = new([], []);

    public static ReferenceSet OfMissing(string description) => new([], [description]);

    public ReferenceSet Merge(ReferenceSet other) =>
        new([.. Assemblies.Concat(other.Assemblies)], [.. Missing.Concat(other.Missing)]);
}
