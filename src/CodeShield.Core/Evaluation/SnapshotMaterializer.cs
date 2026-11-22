using System.Diagnostics;
using CodeShield.Core.Git;

namespace CodeShield.Core.Evaluation;

/// <summary>A temporary read-only copy of the baseline tree; deleted on dispose.</summary>
public sealed class BaselineSnapshot(string path) : IDisposable
{
    public string Path { get; } = path;

    public List<string> Links { get; } = [];

    public void Dispose()
    {
        try
        {
            foreach (var link in Links.Where(Directory.Exists))
            {
                Directory.Delete(link, recursive: false);
            }

            ClearReadOnly(Path);
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The temporary baseline '{Path}' could not be removed: {exception.Message}", exception);
        }
    }

    /// <summary>Marks every exported file read-only so analyzers cannot alter the baseline.</summary>
    public void Seal()
    {
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }
    }

    private static void ClearReadOnly(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}

public interface ISnapshotMaterializer
{
    Task<BaselineSnapshot> MaterializeAsync(string root, string commit, IReadOnlyList<string> baselinePaths, CancellationToken cancellationToken);
}

/// <summary>Exports the baseline tree and makes the workspace's installed dependencies visible to it.</summary>
public sealed class SnapshotMaterializer(IGitClient git) : ISnapshotMaterializer
{
    public async Task<BaselineSnapshot> MaterializeAsync(
        string root, string commit, IReadOnlyList<string> baselinePaths, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(Path.GetTempPath(), "codeshield-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var snapshot = new BaselineSnapshot(destination);
        try
        {
            await git.ExportTreeAsync(root, commit, destination, cancellationToken).ConfigureAwait(false);
            snapshot.Seal();
            foreach (var path in baselinePaths)
            {
                LinkDependencies(root, snapshot, path);
            }

            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private static void LinkDependencies(string root, BaselineSnapshot snapshot, string path)
    {
        var destination = snapshot.Path;
        var directory = Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        if (path.EndsWith("/package.json", StringComparison.Ordinal) || path == "package.json")
        {
            LinkDirectory(Path.Combine(root, directory, "node_modules"), Path.Combine(destination, directory, "node_modules"), snapshot);
        }
        else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            CopyAssets(Path.Combine(root, directory, "obj"), Path.Combine(destination, directory, "obj"));
        }
    }

    private static void CopyAssets(string source, string target)
    {
        var assets = Path.Combine(source, "project.assets.json");
        if (!File.Exists(assets))
        {
            return;
        }

        Directory.CreateDirectory(target);
        File.Copy(assets, Path.Combine(target, "project.assets.json"), overwrite: true);
    }

    private static void LinkDirectory(string source, string link, BaselineSnapshot snapshot)
    {
        if (!Directory.Exists(source) || Directory.Exists(link))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            LinkJunction(source, link);
        }
        else
        {
            Directory.CreateSymbolicLink(link, source);
        }

        snapshot.Links.Add(link);
    }

    private static void LinkJunction(string source, string link)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, source })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Could not start cmd.exe to link dependencies.");
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
        {
            throw new IOException($"Could not link dependencies '{source}' into the baseline (exit code {process.ExitCode}).");
        }
    }
}
