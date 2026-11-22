using CodeShield.Abstractions;
using CodeShield.CSharp;

namespace CodeShield.CSharp.Tests;

/// <summary>A throwaway repository on disk (outside the CodeShield repo, so no Directory.Build.props applies).</summary>
internal sealed class Fixture : IDisposable
{
    public const string ProjectXml =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
        "<Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks>" +
        "</PropertyGroup></Project>";

    public Fixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "cs-csharp-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public Fixture Write(string path, string content)
    {
        var full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public Fixture Project(string path = "App.csproj", string? xml = null) => Write(path, xml ?? ProjectXml);

    public List<string> Files() =>
        [.. Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)];

    public async Task<LanguageAnalysis> AnalyzeAsync(string[] focus, string[]? changedConfig = null, string? assetsRoot = null)
    {
        var request = new AnalysisRequest(Root, Files(), focus, changedConfig ?? [], false);
        return await new CSharpLanguageAnalyzer(assetsRoot).AnalyzeAsync(request, CancellationToken.None);
    }

    public async Task<ContextFacts> SingleAsync(params string[] focus)
    {
        var analysis = await AnalyzeAsync(focus);
        return Assert.Single(analysis.Contexts);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
            // Temp folder cleanup is best effort.
        }
    }
}

internal static class FactExtensions
{
    public static MethodFact Method(this ContextFacts context, string unitSuffix) =>
        context.Files.SelectMany(file => file.Methods).Single(method => method.UnitId.EndsWith(unitSuffix, StringComparison.Ordinal));

    public static bool Fails(this ContextFacts context, string ruleId, string code) =>
        context.Failures.Any(reason => reason.RuleId == ruleId && reason.Code == code);
}
