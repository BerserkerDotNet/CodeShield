using System.Globalization;
using System.Text.RegularExpressions;

namespace CodeShield.Core.Scope;

/// <summary>A unified=0 hunk. For pure insertions/deletions the start is the line before the change.</summary>
public sealed record Hunk(int OldStart, int OldCount, int NewStart, int NewCount)
{
    public int NewEnd => NewStart + NewCount - 1;

    public bool IsPureDeletion => NewCount == 0;
}

public sealed record ParsedDiff(IReadOnlyList<Hunk> Hunks, bool IsBinary);

public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -(?<os>\d+)(?:,(?<oc>\d+))? \+(?<ns>\d+)(?:,(?<nc>\d+))? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();

    public static ParsedDiff Parse(string diff)
    {
        var hunks = new List<Hunk>();
        var binary = false;
        foreach (var line in diff.Split('\n'))
        {
            binary |= IsBinaryMarker(line);
            var match = HunkHeader().Match(line);
            if (match.Success)
            {
                hunks.Add(ToHunk(match));
            }
        }

        return new ParsedDiff(hunks, binary);
    }

    private static bool IsBinaryMarker(string line) =>
        line.StartsWith("Binary files ", StringComparison.Ordinal) ||
        line.StartsWith("GIT binary patch", StringComparison.Ordinal);

    private static Hunk ToHunk(Match match) => new(
        Number(match, "os", 1), Number(match, "oc", 1), Number(match, "ns", 1), Number(match, "nc", 1));

    private static int Number(Match match, string group, int defaultValue)
    {
        var value = match.Groups[group];
        return value.Success ? int.Parse(value.Value, CultureInfo.InvariantCulture) : defaultValue;
    }
}
