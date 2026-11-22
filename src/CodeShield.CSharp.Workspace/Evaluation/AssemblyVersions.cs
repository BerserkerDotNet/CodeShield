using System.Reflection;

namespace CodeShield.CSharp.Evaluation;

/// <summary>Reads the build version of a loaded assembly.</summary>
internal static class AssemblyVersions
{
    public static string Describe(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
