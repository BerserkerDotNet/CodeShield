namespace CodeShield.TypeScript;

/// <summary>
/// The Node.js versions the pinned worker dependencies support: the intersection of every production
/// dependency's engine range, restricted to Node 22 or newer (ESLint excludes 22.0-22.12 and 23).
/// </summary>
public static class NodeRequirement
{
    public const string Description = "Node.js 22.13 or newer in the 22.x line, or Node.js 24 or newer";

    public static bool IsSupported(Version version) =>
        version.Major >= 24 || (version.Major == 22 && version.Minor >= 13);

    /// <summary>Parses the output of <c>node --version</c>, for example <c>v22.20.0</c>.</summary>
    public static Version? Parse(string output)
    {
        var text = output.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }
}
