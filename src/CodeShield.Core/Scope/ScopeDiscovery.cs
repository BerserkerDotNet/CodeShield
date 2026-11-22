using CodeShield.Core.Git;

namespace CodeShield.Core.Scope;

/// <summary>Raised when tracked and untracked states of one path cannot be reconciled; surfaces as an incomplete result.</summary>
public sealed class ScopeConflictException(string message) : Exception(message);

/// <summary>Implements section 3.1 of the specification: baseline resolution, changes and path inventories.</summary>
public sealed class ScopeDiscovery(IGitClient git, SupportedFiles supported)
{
    public async Task<ChangeScope> DiscoverAsync(string workspace, string baseline, CancellationToken ct)
    {
        var root = await git.GetTopLevelAsync(workspace, ct).ConfigureAwait(false);
        var commit = await git.ResolveCommitAsync(root, baseline, ct).ConfigureAwait(false);
        var entries = await CollectEntriesAsync(root, commit, ct).ConfigureAwait(false);
        var changes = new List<FileChange>();
        foreach (var entry in entries.Items)
        {
            changes.Add(await ToChangeAsync(root, commit, entry, entries.WorkingDiffs, ct).ConfigureAwait(false));
        }

        var baselinePaths = await git.ListBaselinePathsAsync(root, commit, ct).ConfigureAwait(false);
        var currentPaths = await git.ListCurrentPathsAsync(root, ct).ConfigureAwait(false);
        var inputPaths = string.Join('\n', currentPaths.Where(supported.IsRelevant).Order(StringComparer.Ordinal));
        return new ChangeScope(root, commit, new ChangeMap(changes, baselinePaths), baselinePaths, currentPaths, inputPaths);
    }

    private sealed record Entries(List<NameStatusEntry> Items, Dictionary<string, string> WorkingDiffs);

    private async Task<Entries> CollectEntriesAsync(string root, string commit, CancellationToken ct)
    {
        var entries = new Entries([.. await git.GetNameStatusAsync(root, commit, ct).ConfigureAwait(false)], []);
        var untracked = await git.ListUntrackedAsync(root, ct).ConfigureAwait(false);
        foreach (var path in untracked.Where(path => supported.IsRelevant(path)))
        {
            await ReconcileUntrackedAsync(root, commit, entries, path, ct).ConfigureAwait(false);
        }

        return entries;
    }

    /// <summary>An untracked path that is also a tracked deletion (for example after <c>git rm --cached</c>) is judged by its working bytes against the baseline blob.</summary>
    private async Task ReconcileUntrackedAsync(string root, string commit, Entries entries, string path, CancellationToken ct)
    {
        var overlapping = entries.Items.Where(entry => entry.Path == path || entry.OldPath == path).ToList();
        if (overlapping.Count == 0)
        {
            entries.Items.Add(new NameStatusEntry(ChangeKind.Added, path, null));
            return;
        }

        var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        var isPlainDeletion = overlapping is [{ Kind: ChangeKind.Deleted, OldPath: null }];
        if (!isPlainDeletion || !File.Exists(full) || File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ScopeConflictException($"'{path}' is both tracked-and-changed and untracked; the states cannot be reconciled.");
        }

        entries.Items.Remove(overlapping[0]);
        var diff = await git.GetUnifiedDiffToWorkingFileAsync(root, commit, path, ct).ConfigureAwait(false);
        if (diff.Length > 0)
        {
            entries.Items.Add(new NameStatusEntry(ChangeKind.Modified, path, null));
            entries.WorkingDiffs[path] = diff;
        }
    }

    private async Task<FileChange> ToChangeAsync(string root, string commit, NameStatusEntry entry, Dictionary<string, string> workingDiffs, CancellationToken ct)
    {
        var needsHunks = entry.Kind is ChangeKind.Modified or ChangeKind.Renamed && supported.IsSource(entry.Path);
        if (!needsHunks)
        {
            return new FileChange(entry.Kind, entry.Path, entry.OldPath, []);
        }

        IReadOnlyList<string> paths = entry.OldPath is null ? [entry.Path] : [entry.OldPath, entry.Path];
        var text = workingDiffs.TryGetValue(entry.Path, out var overlay)
            ? overlay
            : await git.GetUnifiedDiffAsync(root, commit, paths, ct).ConfigureAwait(false);
        var diff = DiffParser.Parse(text);
        if (diff.IsBinary)
        {
            throw new GitException(Abstractions.ReasonCodes.GitFailure, $"'{entry.Path}' has an unsupported binary diff.");
        }

        return new FileChange(entry.Kind, entry.Path, entry.OldPath, diff.Hunks);
    }
}
