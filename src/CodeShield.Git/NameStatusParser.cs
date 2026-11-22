using CodeShield.Core.Git;

namespace CodeShield.Git;

/// <summary>Parses <c>git diff --name-status -z</c> output.</summary>
public static class NameStatusParser
{
    public static IReadOnlyList<NameStatusEntry> Parse(string output)
    {
        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var entries = new List<NameStatusEntry>();
        for (var index = 0; index < parts.Length;)
        {
            var status = parts[index][0];
            var pathCount = status is 'R' or 'C' ? 2 : 1;
            if (index + pathCount >= parts.Length)
            {
                break;
            }

            entries.Add(ToEntry(status, parts.AsSpan(index + 1, pathCount)));
            index += 1 + pathCount;
        }

        return entries;
    }

    private static NameStatusEntry ToEntry(char status, ReadOnlySpan<string> paths) => status switch
    {
        'A' or 'C' => new NameStatusEntry(ChangeKind.Added, paths[^1], null),
        'D' => new NameStatusEntry(ChangeKind.Deleted, paths[0], null),
        'R' => new NameStatusEntry(ChangeKind.Renamed, paths[1], paths[0]),
        _ => new NameStatusEntry(ChangeKind.Modified, paths[0], null),
    };
}
