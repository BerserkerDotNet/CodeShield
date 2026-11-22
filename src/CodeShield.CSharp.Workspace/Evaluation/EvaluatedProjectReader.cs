using Microsoft.Build.Evaluation;

namespace CodeShield.CSharp.Evaluation;

/// <summary>Extracts the evaluated properties and items the adapter needs from an MSBuild project.</summary>
internal static class EvaluatedProjectReader
{
    private static readonly string[] PropertyNames =
    [
        "AssemblyName", "DefineConstants", "LangVersion", "Nullable", "AllowUnsafeBlocks", "OutputType",
        "TargetFrameworkIdentifier", "TargetFrameworkVersion", "NetCoreTargetingPackRoot", "ProjectAssetsFile",
        "MSBuildProjectDirectory",
    ];

    public static EvaluatedProject Read(Project project, string? framework)
    {
        var properties = PropertyNames.ToDictionary(name => name, project.GetPropertyValue);
        var items = new ProjectItems(
            Paths(project, "Compile"),
            Paths(project, "ProjectReference"),
            Names(project, "PackageReference"),
            TargetingPacks(project, framework),
            Usings(project),
            [.. project.Imports.Select(import => import.ImportedProject.FullPath).Distinct()]);
        return new EvaluatedProject(project.FullPath, framework, properties, items);
    }

    private static List<string> Paths(Project project, string itemType) =>
        [.. project.GetItems(itemType).Select(item => item.GetMetadataValue("FullPath")).Distinct()];

    private static List<string> Names(Project project, string itemType) =>
        [.. project.GetItems(itemType).Select(item => item.EvaluatedInclude).Distinct()];

    private static List<UsingDirective> Usings(Project project) =>
    [
        .. project.GetItems("Using").Select(item => new UsingDirective(
            item.EvaluatedInclude,
            NullIfEmpty(item.GetMetadataValue("Alias")),
            string.Equals(item.GetMetadataValue("Static"), "true", StringComparison.OrdinalIgnoreCase))),
    ];

    private static List<PackRequirement> TargetingPacks(Project project, string? framework)
    {
        var requested = Names(project, "FrameworkReference");
        var version = (framework ?? "").Split('-')[0];
        return
        [
            .. project.GetItems("KnownFrameworkReference")
                .Where(item => requested.Contains(item.EvaluatedInclude, StringComparer.OrdinalIgnoreCase)
                    && string.Equals(item.GetMetadataValue("TargetFramework"), version, StringComparison.OrdinalIgnoreCase))
                .Select(item => new PackRequirement(
                    item.GetMetadataValue("TargetingPackName"), NullIfEmpty(item.GetMetadataValue("TargetingPackVersion")))),
        ];
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
