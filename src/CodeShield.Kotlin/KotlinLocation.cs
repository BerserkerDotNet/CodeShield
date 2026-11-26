namespace CodeShield.Kotlin;

/// <summary>Where the Java runtime, the CodeShield Kotlin worker jar and the user's detekt-cli installation live.</summary>
public sealed record KotlinLocation(string JavaExecutable, string? WorkerJar, DetektInstallation Detekt)
{
    public const string JavaVariable = "CODESHIELD_JAVA";
    public const string WorkerVariable = "CODESHIELD_KOTLIN_WORKER";
    public const string WorkerRelativePath = "kotlin-worker/dist/codeshield-kotlin-worker.jar";

    /// <summary>
    /// Java: <c>CODESHIELD_JAVA</c>, then <c>JAVA_HOME/bin/java</c>, then <c>java</c> on PATH. Worker: <c>CODESHIELD_KOTLIN_WORKER</c>,
    /// then <c>kotlin-worker/dist/codeshield-kotlin-worker.jar</c> next to the binaries or in any ancestor. detekt: see <see cref="DetektInstallation"/>.
    /// </summary>
    public static KotlinLocation Discover()
    {
        var worker = Environment.GetEnvironmentVariable(WorkerVariable);
        return new KotlinLocation(
            JavaExecutableFrom(Environment.GetEnvironmentVariable(JavaVariable), Environment.GetEnvironmentVariable("JAVA_HOME")),
            string.IsNullOrWhiteSpace(worker) ? FindWorker(AppContext.BaseDirectory) : worker,
            DetektInstallation.Discover());
    }

    internal static string JavaExecutableFrom(string? configured, string? javaHome)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "java";
    }

    private static string? FindWorker(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory.FullName, WorkerRelativePath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
