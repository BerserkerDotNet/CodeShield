using CodeShield.CSharp.Evaluation;

namespace CodeShield.CSharp.Building;

/// <summary>The implicit preprocessor symbols the SDK defines for a target framework.</summary>
internal static class FrameworkDefines
{
    private static readonly string[] LegacyCore = ["NETCOREAPP", "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER",
        "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER", "NETCOREAPP2_2_OR_GREATER",
        "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER"];

    public static IEnumerable<string> For(EvaluatedProject project)
    {
        var parsed = Version.TryParse(project.Property("TargetFrameworkVersion").TrimStart('v'), out var version);
        if (!parsed || version is null)
        {
            return [];
        }

        return project.Property("TargetFrameworkIdentifier") switch
        {
            ".NETCoreApp" when version.Major >= 5 => Modern(version),
            ".NETStandard" => Standard(version),
            _ => [],
        };
    }

    private static IEnumerable<string> Modern(Version version) =>
        ["NET", $"NET{version.Major}_{version.Minor}", .. Enumerable.Range(5, version.Major - 4).Select(major => $"NET{major}_0_OR_GREATER"), .. LegacyCore];

    private static IEnumerable<string> Standard(Version version)
    {
        string[] known = ["1_0", "1_1", "1_2", "1_3", "1_4", "1_5", "1_6", "2_0", "2_1"];
        var current = $"{version.Major}_{version.Minor}";
        var upTo = known.TakeWhile(candidate => candidate != current).Append(current);
        return ["NETSTANDARD", $"NETSTANDARD{current}", .. upTo.Select(candidate => $"NETSTANDARD{candidate}_OR_GREATER")];
    }
}
