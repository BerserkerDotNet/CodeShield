using CodeShield.Abstractions;

namespace CodeShield.Core.Git;

public sealed class GitException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public Reason ToReason() => new(Code, Message);
}

public sealed record NameStatusEntry(ChangeKind Kind, string Path, string? OldPath);

/// <summary>Git operations needed by the evaluator. Implementations must never modify the repository.</summary>
public interface IGitClient
{
    Task<string> GetTopLevelAsync(string workspace, CancellationToken cancellationToken);

    Task<string> ResolveCommitAsync(string root, string revision, CancellationToken cancellationToken);

    Task<IReadOnlyList<NameStatusEntry>> GetNameStatusAsync(string root, string baseline, CancellationToken cancellationToken);

    Task<string> GetUnifiedDiffAsync(
        string root, string baseline, IReadOnlyList<string> paths, CancellationToken cancellationToken);

    /// <summary>Unified=0 diff between the baseline blob of <paramref name="path"/> and the bytes currently in the working tree, regardless of the index. Empty when identical.</summary>
    Task<string> GetUnifiedDiffToWorkingFileAsync(string root, string baseline, string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListUntrackedAsync(string root, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListBaselinePathsAsync(string root, string commit, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListCurrentPathsAsync(string root, CancellationToken cancellationToken);

    /// <summary>Exports the commit's tree into an empty directory without touching the working tree.</summary>
    Task ExportTreeAsync(string root, string commit, string destination, CancellationToken cancellationToken);
}
