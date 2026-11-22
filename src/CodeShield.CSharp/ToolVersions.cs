using System.Reflection;
using CodeShield.CSharp.Evaluation;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp;

/// <summary>Version strings reported as analyzerVersions.</summary>
internal static class ToolVersions
{
    public static string Roslyn() => AssemblyVersions.Describe(typeof(Compilation).Assembly);

    public static string NetAnalyzers()
    {
        var pinned = typeof(ToolVersions).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "NetAnalyzersVersion");
        return pinned?.Value ?? "unknown";
    }
}

internal static class AnalyzerVersions
{
    public static IReadOnlyDictionary<string, string> Current() => new Dictionary<string, string>
    {
        ["roslyn"] = ToolVersions.Roslyn(),
        ["net-analyzers"] = ToolVersions.NetAnalyzers(),
        ["msbuild"] = MsBuildBootstrap.MsBuildVersion(),
        ["dotnet-sdk"] = MsBuildBootstrap.SdkVersion,
    };
}
