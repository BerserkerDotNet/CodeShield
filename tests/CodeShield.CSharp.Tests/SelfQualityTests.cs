using CodeShield.Abstractions;

namespace CodeShield.CSharp.Tests;

/// <summary>The adapter is held to the quality bar it enforces: it analyzes its own sources.</summary>
public sealed class SelfQualityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static bool IsBuildOutput(string path) =>
        path.Split('/').Any(part => part is "bin" or "obj");

    private static async Task<ContextFacts> AnalyzeSelfAsync()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal) && !IsBuildOutput(path))
            .Append("Directory.Build.props").Append("Directory.Packages.props")
            .ToList();
        var focus = files.Where(path => path.StartsWith("src/CodeShield.CSharp/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        var analysis = await new CSharpLanguageAnalyzer().AnalyzeAsync(new AnalysisRequest(root, files, focus, [], false), CancellationToken.None);
        return Assert.Single(analysis.Contexts, c => c.ContextId.StartsWith("src/CodeShield.CSharp/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Adapter_sources_meet_the_quality_bar()
    {
        var context = await AnalyzeSelfAsync();
        Assert.Empty(context.Failures);
        Assert.Empty(context.Diagnostics);
        var methods = context.Files.SelectMany(f => f.Methods).ToList();
        Assert.True(methods.Count > 0);
        var violations = methods.Where(m => m.Complexity > 6).Select(m => $"{m.UnitId} complexity {m.Complexity}")
            .Concat(methods.Where(m => m.Nesting > 2).Select(m => $"{m.UnitId} nesting {m.Nesting}"))
            .Concat(methods.Where(m => m.CodeLines > 40).Select(m => $"{m.UnitId} lines {m.CodeLines}"))
            .Concat(methods.Where(m => m.ParameterCount > 5).Select(m => $"{m.UnitId} parameters {m.ParameterCount}"))
            .Concat(context.Files.Where(f => f.CodeLines > 400).Select(f => $"{f.Path} lines {f.CodeLines}"))
            .Concat(context.Classes.Where(c => c.Applicable && c.Components.Count > 1).Select(c => $"{c.UnitId} components {c.Components.Count}"));
        Assert.True(!violations.Any(), string.Join("\n", violations));
    }
}
