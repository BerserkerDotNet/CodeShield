using System.Diagnostics;
using System.Text;
using CodeShield.Core.Evaluation;
using CodeShield.Git;

namespace CodeShield.Core.Tests;

/// <summary>Real-Git checks of baseline materialization in an isolated temporary repository.</summary>
public sealed class BaselineSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codeshield-bl-" + Guid.NewGuid().ToString("N"));

    public BaselineSnapshotTests()
    {
        Directory.CreateDirectory(_root);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "user.name", "Fixture");
        Git("config", "core.autocrlf", "false");
        Git("config", "commit.gpgsign", "false");
    }

    [Fact]
    public async Task ExportIgnoresExportAttributesAndKeepsCommitBytes()
    {
        Write(".gitattributes", "ignored.ts export-ignore\nsubst.ts export-subst\n");
        Write("ignored.ts", "export const a = 1;\n");
        Write("subst.ts", "export const id = '$Format:%H$';\n");
        Write("a/b.ts", "export const b = 2;\r\n");
        var commit = Commit();

        using var snapshot = await MaterializeAsync(commit, []);

        Assert.Equal("export const a = 1;\n", File.ReadAllText(Path.Combine(snapshot.Path, "ignored.ts")));
        Assert.Equal("export const id = '$Format:%H$';\n", File.ReadAllText(Path.Combine(snapshot.Path, "subst.ts")));
        Assert.Equal("export const b = 2;\r\n", File.ReadAllText(Path.Combine(snapshot.Path, "a", "b.ts")));
    }

    [Fact]
    public async Task ExportedFilesAreReadOnlyAndRemovedOnDispose()
    {
        Write("x.ts", "export const x = 1;\n");
        var snapshot = await MaterializeAsync(Commit(), []);
        var file = Path.Combine(snapshot.Path, "x.ts");

        Assert.True(new FileInfo(file).IsReadOnly);
        snapshot.Dispose();
        Assert.False(Directory.Exists(snapshot.Path));
    }

    [Fact]
    public async Task NodeModulesAreLinkedWithoutSealingOrDeletingWorkspaceContent()
    {
        Write("package.json", "{}\n");
        Write(".gitignore", "node_modules\n");
        var commit = Commit();
        var installed = Path.Combine(_root, "node_modules", "dep", "index.js");
        Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
        File.WriteAllText(installed, "module.exports = 1;\n");

        var snapshot = await MaterializeAsync(commit, ["package.json"]);
        Assert.True(File.Exists(Path.Combine(snapshot.Path, "node_modules", "dep", "index.js")));
        snapshot.Dispose();

        Assert.True(File.Exists(installed));
        Assert.False(new FileInfo(installed).IsReadOnly);
    }

    [Fact]
    public async Task ProjectAssetsAreCopiedForCsprojBaselines()
    {
        Write("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write(".gitignore", "obj\n");
        var commit = Commit();
        Directory.CreateDirectory(Path.Combine(_root, "App", "obj"));
        File.WriteAllText(Path.Combine(_root, "App", "obj", "project.assets.json"), "{\"version\":3}");

        using var snapshot = await MaterializeAsync(commit, ["App/App.csproj"]);

        Assert.Equal("{\"version\":3}", File.ReadAllText(Path.Combine(snapshot.Path, "App", "obj", "project.assets.json")));
    }

    [Fact]
    public async Task CleanupFailureIsReportedNotSwallowed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Write("x.ts", "export const x = 1;\n");
        var snapshot = await MaterializeAsync(Commit(), []);
        using var lockFile = new FileStream(Path.Combine(snapshot.Path, "x.ts"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(snapshot.Dispose);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private Task<BaselineSnapshot> MaterializeAsync(string commit, IReadOnlyList<string> paths) =>
        new SnapshotMaterializer(new GitCliClient(new ProcessRunner())).MaterializeAsync(_root, commit, paths, CancellationToken.None);

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private string Commit()
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", "fixture");
        return Git("rev-parse", "HEAD").Trim();
    }

    private string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed");
    }
}
