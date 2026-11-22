namespace CodeShield.CSharp.Evaluation;

internal sealed record UsingDirective(string Namespace, string? Alias, bool IsStatic);

/// <summary>A targeting pack requested by a FrameworkReference for the evaluated target framework.</summary>
internal sealed record PackRequirement(string PackName, string? Version);

/// <summary>Everything the adapter needs from one evaluated project/target-framework combination.</summary>
internal sealed record EvaluatedProject(
    string FullPath,
    string? Framework,
    IReadOnlyDictionary<string, string> Properties,
    ProjectItems Items)
{
    public string Property(string name) => Properties.TryGetValue(name, out var value) ? value : "";
}

internal sealed record ProjectItems(
    IReadOnlyList<string> Compile,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<PackRequirement> TargetingPacks,
    IReadOnlyList<UsingDirective> Usings,
    IReadOnlyList<string> Imports);

/// <summary>One framework context of a project: either evaluated or failed with a message.</summary>
internal sealed record ContextEvaluation(string? Framework, EvaluatedProject? Project, string? Error);

internal sealed record ProjectEvaluation(string FullPath, IReadOnlyList<ContextEvaluation> Contexts);
