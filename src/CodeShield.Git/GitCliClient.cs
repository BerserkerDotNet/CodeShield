using System.Formats.Tar;
using System.Text;
using CodeShield.Abstractions;
using CodeShield.Core.Git;

namespace CodeShield.Git;

/// <summary>Read-only Git access through the <c>git</c> executable; every call uses an argument vector.</summary>
public sealed class GitCliClient(IProcessRunner runner, string gitExecutable = "git") : IGitClient
{
    public async Task<string> GetTopLevelAsync(string workspace, CancellationToken cancellationToken)
    {
        var result = await RunAsync(workspace, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        Require(result, "The workspace is not inside a Git repository");
        return Path.GetFullPath(Text(result).Trim());
    }

    public async Task<string> ResolveCommitAsync(string root, string revision, CancellationToken cancellationToken)
    {
        if (revision.StartsWith('-'))
        {
            throw new GitException(ReasonCodes.InvalidBaseline, $"'{revision}' is not a valid revision.");
        }

        var result = await RunAsync(root, ["rev-parse", "--verify", "--quiet", "--end-of-options", revision + "^{commit}"], cancellationToken)
            .ConfigureAwait(false);
        return result.ExitCode == 0
            ? Text(result).Trim()
            : throw new GitException(ReasonCodes.InvalidBaseline, $"'{revision}' does not resolve to a commit.");
    }

    public async Task<IReadOnlyList<NameStatusEntry>> GetNameStatusAsync(string root, string baseline, CancellationToken cancellationToken)
    {
        var result = await RunAsync(root, ["diff", "-M", "--name-status", "-z", "--no-ext-diff", "--no-textconv", baseline, "--"], cancellationToken)
            .ConfigureAwait(false);
        Require(result, "git diff --name-status failed");
        return NameStatusParser.Parse(Text(result));
    }

    public async Task<string> GetUnifiedDiffAsync(
        string root, string baseline, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        string[] arguments = ["diff", "-M", "--unified=0", "--no-color", "--no-ext-diff", "--no-textconv", baseline, "--", .. paths];
        var result = await RunAsync(root, arguments, cancellationToken).ConfigureAwait(false);
        Require(result, "git diff failed");
        return Text(result);
    }

    public async Task<string> GetUnifiedDiffToWorkingFileAsync(string root, string baseline, string path, CancellationToken cancellationToken)
    {
        var blob = await RunAsync(root, ["cat-file", "blob", $"{baseline}:{path}"], cancellationToken).ConfigureAwait(false);
        Require(blob, $"git cat-file failed for {path}");
        var scratch = Directory.CreateTempSubdirectory("codeshield-blob-");
        try
        {
            var baselineFile = Path.Combine(scratch.FullName, "baseline");
            await File.WriteAllBytesAsync(baselineFile, blob.Output, cancellationToken).ConfigureAwait(false);
            var working = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            string[] arguments = ["diff", "--no-index", "--unified=0", "--no-color", "--no-ext-diff", "--no-textconv", "--", baselineFile, working];
            var result = await RunAsync(root, arguments, cancellationToken).ConfigureAwait(false);
            return result.ExitCode is 0 or 1 ? Text(result) : throw new GitException(ReasonCodes.GitFailure, $"git diff --no-index failed: {result.Error.Trim()}");
        }
        finally
        {
            scratch.Delete(true);
        }
    }

    public async Task<IReadOnlyList<string>> ListUntrackedAsync(string root, CancellationToken cancellationToken) =>
        await ListAsync(root, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListBaselinePathsAsync(string root, string commit, CancellationToken cancellationToken) =>
        await ListAsync(root, ["ls-tree", "-r", "-z", "--name-only", commit], cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListCurrentPathsAsync(string root, CancellationToken cancellationToken)
    {
        var paths = await ListAsync(root, ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], cancellationToken).ConfigureAwait(false);
        return [.. paths.Distinct().Where(path => File.Exists(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))];
    }

    /// <summary>Writes the exact committed blob bytes; unlike <c>git archive</c> this ignores export-ignore and export-subst attributes.</summary>
    public async Task ExportTreeAsync(string root, string commit, string destination, CancellationToken cancellationToken)
    {
        var listing = await RunAsync(root, ["ls-tree", "-r", "-z", "--full-tree", commit], cancellationToken).ConfigureAwait(false);
        Require(listing, "git ls-tree failed");
        var entries = Text(listing).Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(TreeEntry.Parse).Where(entry => entry.IsBlob);
        await Parallel.ForEachAsync(
            entries, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            (entry, token) => new ValueTask(WriteBlobAsync(root, destination, entry, token))).ConfigureAwait(false);
    }

    private async Task WriteBlobAsync(string root, string destination, TreeEntry entry, CancellationToken cancellationToken)
    {
        var blob = await RunAsync(root, ["cat-file", "blob", entry.Sha], cancellationToken).ConfigureAwait(false);
        Require(blob, $"git cat-file failed for {entry.Path}");
        var target = Path.Combine(destination, entry.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, blob.Output, cancellationToken).ConfigureAwait(false);
    }

    private sealed record TreeEntry(string Mode, string Type, string Sha, string Path)
    {
        public bool IsBlob => Type == "blob";

        public static TreeEntry Parse(string line)
        {
            var tab = line.IndexOf('\t');
            var head = line[..tab].Split(' ');
            return new TreeEntry(head[0], head[1], head[2], line[(tab + 1)..]);
        }
    }
    private async Task<IReadOnlyList<string>> ListAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await RunAsync(root, arguments, cancellationToken).ConfigureAwait(false);
        Require(result, $"git {arguments[0]} failed");
        return [.. Text(result).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    private async Task<ProcessResult> RunAsync(string directory, string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await runner.RunAsync(gitExecutable, ["-c", "core.quotepath=off", "-C", directory, .. arguments], directory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or DirectoryNotFoundException or InvalidOperationException)
        {
            throw new GitException(ReasonCodes.GitFailure, $"Could not run git: {exception.Message}");
        }
    }

    private static void Require(ProcessResult result, string message)
    {
        if (result.ExitCode != 0)
        {
            throw new GitException(ReasonCodes.GitFailure, $"{message}: {result.Error.Trim()}");
        }
    }

    private static string Text(ProcessResult result) => Encoding.UTF8.GetString(result.Output);
}
