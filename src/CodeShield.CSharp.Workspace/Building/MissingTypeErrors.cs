using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Building;

/// <summary>Detects compiler errors that indicate the compilation lacks its external types.</summary>
internal static class MissingTypeErrors
{
    private static readonly HashSet<string> Ids = ["CS0246", "CS0234", "CS0012", "CS0518", "CS0400", "CS0117"];

    public static bool Any(Compilation compilation) => First(compilation) is not null;

    public static string? First(Compilation compilation) =>
        compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id))
            .Select(diagnostic => diagnostic.ToString())
            .FirstOrDefault();
}
