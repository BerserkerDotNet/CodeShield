using CodeShield.Abstractions;
using CodeShield.Core.Git;

namespace CodeShield.Core.Scope;

/// <summary>A changed path between the baseline and the working tree.</summary>
public sealed record FileChange(ChangeKind Kind, string Path, string? OldPath, IReadOnlyList<Hunk> Hunks)
{
    public bool IsNew => Kind == ChangeKind.Added;
}

/// <summary>Maps current paths, lines and spans to the baseline using Git's change information.</summary>
public sealed class ChangeMap
{
    private readonly Dictionary<string, FileChange> _byCurrent;
    private readonly Dictionary<string, FileChange> _byBaseline;
    private readonly HashSet<string> _baselinePaths;

    public ChangeMap(IEnumerable<FileChange> changes, IEnumerable<string> baselinePaths)
    {
        Changes = [.. changes.OrderBy(change => change.Path, StringComparer.Ordinal)];
        _byCurrent = Changes.Where(change => change.Kind != ChangeKind.Deleted).ToDictionary(change => change.Path);
        _byBaseline = Changes.Where(change => change.OldPath is not null || change.Kind == ChangeKind.Deleted)
            .ToDictionary(change => change.OldPath ?? change.Path);
        _baselinePaths = [.. baselinePaths];
    }

    public IReadOnlyList<FileChange> Changes { get; }

    public FileChange? ForCurrent(string path) => _byCurrent.GetValueOrDefault(path);

    /// <summary>The baseline path of a current file, or null when the file did not exist in the baseline.</summary>
    public string? BaselinePathOf(string currentPath)
    {
        if (_byCurrent.TryGetValue(currentPath, out var change))
        {
            return change.IsNew ? null : change.OldPath ?? change.Path;
        }

        return _baselinePaths.Contains(currentPath) ? currentPath : null;
    }

    /// <summary>The current path of a baseline file, or null when deleted or moved without recognition.</summary>
    public string? CurrentPathOf(string baselinePath)
    {
        if (_byBaseline.TryGetValue(baselinePath, out var change))
        {
            return change.Kind == ChangeKind.Deleted ? null : change.Path;
        }

        return _baselinePaths.Contains(baselinePath) ? baselinePath : null;
    }

    public bool IsNewFile(string currentPath) => _byCurrent.TryGetValue(currentPath, out var c) && c.IsNew;

    public bool IsChanged(string currentPath) => _byCurrent.ContainsKey(currentPath);

    /// <summary>True when the span holds added/modified lines or a deletion inside it.</summary>
    public bool IsSpanAffected(string currentPath, SourceSpan span)
    {
        if (!_byCurrent.TryGetValue(currentPath, out var change))
        {
            return false;
        }

        return change.IsNew || change.Hunks.Any(hunk => HunkTouches(hunk, span));
    }

    public bool IsLineChanged(string currentPath, int line)
    {
        if (!_byCurrent.TryGetValue(currentPath, out var change))
        {
            return false;
        }

        return change.IsNew || change.Hunks.Any(hunk => hunk.NewCount > 0 && line >= hunk.NewStart && line <= hunk.NewEnd);
    }

    /// <summary>Maps an unchanged current line to its baseline line; null when the line is added or modified.</summary>
    public int? MapLineToBaseline(string currentPath, int line)
    {
        if (!_byCurrent.TryGetValue(currentPath, out var change))
        {
            return _baselinePaths.Contains(currentPath) ? line : null;
        }

        if (change.IsNew || IsLineChanged(currentPath, line))
        {
            return null;
        }

        return line + change.Hunks.Where(hunk => hunk.IsPureDeletion ? hunk.NewStart < line : hunk.NewEnd < line)
            .Sum(hunk => hunk.OldCount - hunk.NewCount);
    }

    private static bool HunkTouches(Hunk hunk, SourceSpan span) => hunk.IsPureDeletion
        ? hunk.NewStart >= span.StartLine && hunk.NewStart + 1 <= span.EndLine
        : span.IntersectsLines(hunk.NewStart, hunk.NewEnd);
}
