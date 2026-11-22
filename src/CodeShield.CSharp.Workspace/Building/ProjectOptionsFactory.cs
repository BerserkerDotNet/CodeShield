using CodeShield.CSharp.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeShield.CSharp.Building;

/// <summary>Translates evaluated MSBuild properties into Roslyn parse and compilation options.</summary>
internal static class ProjectOptionsFactory
{
    private static readonly Dictionary<int, LanguageVersion> CoreDefaults = new()
    {
        [10] = LanguageVersion.CSharp14, [9] = LanguageVersion.CSharp13, [8] = LanguageVersion.CSharp12,
        [7] = LanguageVersion.CSharp11, [6] = LanguageVersion.CSharp10, [5] = LanguageVersion.CSharp9,
        [3] = LanguageVersion.CSharp8,
    };

    public static CSharpParseOptions Parse(EvaluatedProject project) =>
        new(LanguageVersionOf(project), DocumentationMode.None, SourceCodeKind.Regular, DefineConstants(project));

    public static CSharpCompilationOptions Compilation(EvaluatedProject project) =>
        new CSharpCompilationOptions(OutputKindOf(project.Property("OutputType")))
            .WithNullableContextOptions(NullableOf(project.Property("Nullable")))
            .WithAllowUnsafe(string.Equals(project.Property("AllowUnsafeBlocks"), "true", StringComparison.OrdinalIgnoreCase));

    public static string[] DefineConstants(EvaluatedProject project) =>
        [.. project.Property("DefineConstants")
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(FrameworkDefines.For(project))
            .Distinct(StringComparer.Ordinal)];

    private static LanguageVersion LanguageVersionOf(EvaluatedProject project)
    {
        var declared = project.Property("LangVersion");
        var explicitVersion = LanguageVersionFacts.TryParse(declared, out var parsed) && parsed != LanguageVersion.Default;
        return explicitVersion ? parsed : DefaultFor(project);
    }

    private static LanguageVersion DefaultFor(EvaluatedProject project)
    {
        var version = Version.TryParse(project.Property("TargetFrameworkVersion").TrimStart('v'), out var parsed) ? parsed : new Version(0, 0);
        return project.Property("TargetFrameworkIdentifier") switch
        {
            ".NETCoreApp" => CoreDefaults.GetValueOrDefault(version.Major, LanguageVersion.CSharp7_3),
            ".NETStandard" when version >= new Version(2, 1) => LanguageVersion.CSharp8,
            _ => LanguageVersion.CSharp7_3,
        };
    }

    private static OutputKind OutputKindOf(string outputType) => outputType.ToLowerInvariant() switch
    {
        "exe" => OutputKind.ConsoleApplication,
        "winexe" => OutputKind.WindowsApplication,
        _ => OutputKind.DynamicallyLinkedLibrary,
    };

    private static NullableContextOptions NullableOf(string value) => value.ToLowerInvariant() switch
    {
        "enable" => NullableContextOptions.Enable,
        "warnings" => NullableContextOptions.Warnings,
        "annotations" => NullableContextOptions.Annotations,
        _ => NullableContextOptions.Disable,
    };
}
