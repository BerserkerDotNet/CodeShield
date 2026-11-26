using CodeShield.Abstractions;
using CodeShield.Workers;

namespace CodeShield.Kotlin;

/// <summary>
/// Kotlin adapter: delegates analysis and tokenization to the CodeShield Kotlin worker jar, run on the user's Java runtime with the
/// user's detekt-cli 1.23.x on the class path (it supplies the Kotlin parser and the correctness rules). Gradle builds define the
/// modules and contexts; Gradle itself is never executed.
/// </summary>
public sealed class KotlinAnalyzer(KotlinLocation location, ICorrectnessSelection correctness) : WorkerLanguageAnalyzer(correctness)
{
    private const string MainClass = "codeshield.kotlin.Main";

    /// <summary>The worker exits with this code when the detekt-cli classes it needs are missing or incompatible.</summary>
    private const int MissingAnalyzerExit = 3;

    private static readonly string[] ConfigurationSuffixes = [".gradle", ".gradle.kts"];

    public KotlinAnalyzer()
        : this(KotlinLocation.Discover(), CorrectnessCatalog.Pinned)
    {
    }

    public override string Language => Languages.Kotlin;

    public override bool IsSource(string repoRelativePath) =>
        repoRelativePath.EndsWith(".kt", StringComparison.OrdinalIgnoreCase) && !InBuildOutput(repoRelativePath);

    /// <summary>Gradle settings, build and applied scripts (<c>*.gradle</c>, <c>*.gradle.kts</c>) outside build output.</summary>
    public override bool IsConfiguration(string repoRelativePath) =>
        ConfigurationSuffixes.Any(suffix => repoRelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) && !InBuildOutput(repoRelativePath);

    /// <summary>The worker's version handshake: the detekt, Kotlin and Java versions analysis would use, or why the worker cannot run.</summary>
    public async Task<(IReadOnlyDictionary<string, string>? Versions, Reason? Failure)> VersionAsync(CancellationToken cancellationToken)
    {
        var (result, failure) = await CallAsync<Dictionary<string, string>>(new { protocol = Protocol, command = "version" }, cancellationToken)
            .ConfigureAwait(false);
        return (result, failure);
    }

    /// <summary>
    /// A <c>build</c> or <c>.gradle</c> directory before any <c>src</c> directory is Gradle output or cache
    /// (<c>app/build/generated/...</c>); a package directory named <c>build</c> under <c>src/</c> is ordinary source.
    /// </summary>
    internal static bool InBuildOutput(string path)
    {
        var segments = path.Split('/');
        foreach (var segment in segments.Take(segments.Length - 1))
        {
            if (segment == "src")
            {
                return false;
            }

            if (segment is "build" or ".gradle")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The worker command for the located installation, or the reason analysis cannot start.</summary>
    internal (WorkerCommand? Command, Reason? Failure) Command()
    {
        if (location.WorkerJar is null || !File.Exists(location.WorkerJar))
        {
            return (null, WorkerCall.Missing($"The Kotlin worker ({KotlinLocation.WorkerRelativePath}) was not found; run scripts/setup."));
        }

        if (!location.Detekt.IsUsable)
        {
            return (null, WorkerCall.Missing(location.Detekt.Problem!));
        }

        var classPath = string.Join(Path.PathSeparator, [location.WorkerJar, .. location.Detekt.ClassPath]);
        string[] arguments = ["-Xss16m", "-XX:+UseSerialGC", "-Djava.awt.headless=true", "-cp", classPath, MainClass];
        var command = new WorkerCommand("Kotlin worker", "Java", location.JavaExecutable, arguments)
        {
            ClassifyFailure = run => Classify(location.JavaExecutable, run),
        };
        return (command, null);
    }

    internal static Reason? Classify(string java, WorkerRun run)
    {
        if (run.Error.Contains("UnsupportedClassVersionError", StringComparison.Ordinal))
        {
            return WorkerCall.Missing($"The Kotlin worker needs {JavaRequirement.Description}; '{java}' is older. Set {KotlinLocation.JavaVariable} or JAVA_HOME.");
        }

        return run.ExitCode == MissingAnalyzerExit ? WorkerCall.Missing(run.Error.Trim()) : null;
    }

    protected override (WorkerCommand? Command, Reason? Failure) ResolveCommand() => Command();
}
