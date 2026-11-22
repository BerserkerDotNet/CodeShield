using CodeShield.Abstractions;
using CodeShield.CSharp.Evaluation;

namespace CodeShield.CSharp.Planning;

/// <summary>Records the project files that shaped an evaluation (the project plus everything it imported).</summary>
internal static class ProjectInputs
{
    public static void Record(ContextPlan plan, AnalysisEnvironment environment)
    {
        var paths = new[] { environment.Paths.ToFullPath(plan.CsprojPath) }
            .Concat(plan.Project?.Items.Imports ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(File.Exists))
        {
            environment.ReadInput(path);
        }
    }
}

internal static class FileKinds
{
    public static bool IsSource(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    public static bool IsProject(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    public static bool IsConfiguration(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase);
    }
}
