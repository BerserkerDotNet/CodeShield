using System.Diagnostics;
using System.Text;

namespace CodeShield.IntegrationTests;

/// <summary>An isolated temporary Git repository; the only place tests mutate.</summary>
public sealed class GitFixture : IDisposable
{
    public GitFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "codeshield-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "user.name", "Fixture");
        Git("config", "commit.gpgsign", "false");
        Git("config", "core.autocrlf", "false");
    }

    public string Root { get; }

    public void Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    public void Delete(string relativePath) => File.Delete(Path.Combine(Root, relativePath));

    public void Stage(params string[] paths) => Git(["add", "--", .. paths]);

    /// <summary>Test setup only: restores a fixture project the way a developer would before asking for an evaluation.</summary>
    public void Restore(string project)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", ["restore", project])
        {
            WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet restore {project} failed.");
        }
    }

    public string Commit(IReadOnlyDictionary<string, string> files, string message = "fixture")
    {
        foreach (var (path, content) in files)
        {
            Write(path, content);
        }

        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    public string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}
