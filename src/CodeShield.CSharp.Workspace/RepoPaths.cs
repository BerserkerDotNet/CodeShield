namespace CodeShield.CSharp;

/// <summary>Maps between absolute paths and the forward-slash repo-relative paths of a snapshot inventory.</summary>
internal sealed class RepoPaths
{
    private readonly string _root;
    private readonly Dictionary<string, string> _inventory;

    public RepoPaths(string root, IEnumerable<string> files)
    {
        _root = Path.GetFullPath(root);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _inventory = new Dictionary<string, string>(comparer);
        foreach (var file in files)
        {
            _inventory[file] = file;
        }
    }

    public string Root => _root;

    public string ToFullPath(string repoPath) => Path.GetFullPath(Path.Combine(_root, repoPath));

    /// <summary>Repo-relative path of an inventory file; null for files outside the snapshot or missing from the inventory.</summary>
    public string? ToRepoPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
        var outside = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative);
        return !outside && _inventory.TryGetValue(relative, out var canonical) ? canonical : null;
    }

    public static string Canonical(string fullPath) => Path.GetFullPath(fullPath).Replace('\\', '/');
}
