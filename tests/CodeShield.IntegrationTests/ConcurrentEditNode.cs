using System.Text;

namespace CodeShield.IntegrationTests;

/// <summary>
/// A node launcher that runs the real node, then (once) appends to a workspace file after the worker has read it.
/// This reproduces a concurrent edit deterministically without any hook in the server.
/// </summary>
public sealed class ConcurrentEditNode : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "cs-node-" + Guid.NewGuid().ToString("N"));

    /// <param name="fileToEdit">File appended to (and created when absent) once.</param>
    /// <param name="afterRuns">Edit after this many worker processes have exited; a full discovery pass of the fixture uses a known count.</param>
    public ConcurrentEditNode(string fileToEdit, int afterRuns = 1)
    {
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "done");
        var counter = Path.Combine(directory, "runs");
        File.WriteAllText(Launcher, OperatingSystem.IsWindows() ? Windows(fileToEdit, marker, counter, afterRuns) : Posix(fileToEdit, marker, counter, afterRuns), new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public string Launcher => Path.Combine(directory, OperatingSystem.IsWindows() ? "node.cmd" : "node.sh");

    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string> { ["CODESHIELD_NODE"] = Launcher };

    public int Runs => File.Exists(Path.Combine(directory, "runs")) ? File.ReadAllLines(Path.Combine(directory, "runs")).Length : 0;
    private static string Windows(string file, string marker, string counter, int afterRuns) =>
        $"@echo off\r\nnode %*\r\nset RC=%ERRORLEVEL%\r\necho x>>\"{counter}\"\r\nfor /f %%c in ('find /c /v \"\" ^< \"{counter}\"') do set N=%%c\r\n" +
        $"if %N% GEQ {afterRuns} if not exist \"{marker}\" (\r\n  echo // edited during evaluation>>\"{file}\"\r\n  echo x>\"{marker}\"\r\n)\r\nexit /b %RC%\r\n";

    private static string Posix(string file, string marker, string counter, int afterRuns) =>
        $"#!/bin/sh\nnode \"$@\"\nrc=$?\necho x >> '{counter}'\nn=$(wc -l < '{counter}')\nif [ \"$n\" -ge {afterRuns} ] && [ ! -e '{marker}' ]; then echo '// edited during evaluation' >> '{file}'; echo x > '{marker}'; fi\nexit $rc\n";

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
