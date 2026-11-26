using System.IO.Compression;
using System.Text.RegularExpressions;

namespace CodeShield.Kotlin;

/// <summary>
/// The user's detekt-cli installation: the jars put on the worker class path (they supply the Kotlin parser and the
/// correctness rules) and the version recorded in their manifest. <c>Problem</c> explains why it cannot be used.
/// </summary>
public sealed partial record DetektInstallation(string? Source, IReadOnlyList<string> ClassPath, string? Version, string? Problem)
{
    public const string Variable = "CODESHIELD_DETEKT";
    public const string Description = "detekt-cli 1.23.x";

    private const string InstallHint =
        "Install detekt-cli 1.23.x (for example 'brew install detekt', 'scoop install detekt' or the release zip) and put it on PATH, "
        + $"or set {Variable} to the detekt-cli-<version>-all.jar or the installation directory.";

    private static readonly string[] CommandNames = ["detekt-cli", "detekt"];
    private static readonly string[] WindowsExtensions = [".bat", ".cmd", ".exe", ""];

    public bool IsUsable => Problem is null;

    /// <summary>Only the 1.23 line is accepted: the worker is compiled against its API and the pinned rules exist in every 1.23 release.</summary>
    public static bool IsSupported(string version) => SupportedVersion().IsMatch(version);

    public static DetektInstallation Discover() =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } configured
            ? FromConfigured(configured)
            : FromPath(Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

    /// <summary>Reads an explicit jar file or installation directory.</summary>
    public static DetektInstallation FromConfigured(string configured)
    {
        if (File.Exists(configured))
        {
            return configured.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                ? FromJars(configured, [Path.GetFullPath(configured)])
                : Unusable(configured, $"{Variable} ('{configured}') is not a .jar file or a detekt-cli installation directory.");
        }

        return Directory.Exists(configured)
            ? FromInstallDirectory(configured, configured)
            : Unusable(configured, $"{Variable} ('{configured}') does not exist. {InstallHint}");
    }

    /// <summary>Finds the detekt-cli launcher on PATH and the jars of the installation it belongs to.</summary>
    public static DetektInstallation FromPath(string path)
    {
        foreach (var launcher in Launchers(path))
        {
            var target = ResolveLauncher(launcher);
            var root = Path.GetDirectoryName(Path.GetDirectoryName(target));
            if (root is not null)
            {
                return FromInstallDirectory(launcher, root);
            }
        }

        return Unusable(null, $"detekt-cli was not found. {InstallHint}");
    }

    private static DetektInstallation FromInstallDirectory(string source, string root)
    {
        string[] candidates = [Path.Combine(root, "lib"), Path.Combine(root, "libexec", "lib"), Path.Combine(root, "libexec"), root];
        foreach (var directory in candidates.Where(Directory.Exists))
        {
            var jars = Directory.GetFiles(directory, "*.jar").Order(StringComparer.Ordinal).ToList();
            if (jars.Any(jar => Path.GetFileName(jar).StartsWith("detekt-cli", StringComparison.OrdinalIgnoreCase)))
            {
                return FromJars(source, [.. jars.Select(Path.GetFullPath)]);
            }
        }

        return Unusable(source, $"No detekt-cli jar was found under '{root}' (looked in lib, libexec/lib and libexec). {InstallHint}");
    }

    private static DetektInstallation FromJars(string source, IReadOnlyList<string> jars)
    {
        var version = jars.Select(ReadVersion).FirstOrDefault(found => found is not null);
        if (version is null)
        {
            return new DetektInstallation(source, jars, null, $"'{source}' is not a detekt-cli distribution: no jar declares a DetektVersion. {InstallHint}");
        }

        return IsSupported(version)
            ? new DetektInstallation(source, jars, version, null)
            : new DetektInstallation(source, jars, version, $"detekt-cli {version} ('{source}') is not supported; {Description} is required. {InstallHint}");
    }

    private static DetektInstallation Unusable(string? source, string problem) => new(source, [], null, problem);

    /// <summary>The <c>DetektVersion</c> attribute of a jar manifest, or null when absent or unreadable.</summary>
    internal static string? ReadVersion(string jar)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jar);
            var entry = archive.GetEntry("META-INF/MANIFEST.MF");
            if (entry is null)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            return ManifestAttribute(reader.ReadToEnd(), "DetektVersion");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads one main-section attribute, joining continuation lines (which start with a single space).</summary>
    internal static string? ManifestAttribute(string manifest, string name)
    {
        var lines = manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = lines[index][(name.Length + 1)..];
            while (index + 1 < lines.Length && lines[index + 1].StartsWith(' '))
            {
                value += lines[++index][1..];
            }

            return value.Trim();
        }

        return null;
    }

    private static IEnumerable<string> Launchers(string path)
    {
        var extensions = OperatingSystem.IsWindows() ? WindowsExtensions : [""];
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(directory => CommandNames.SelectMany(name => extensions.Select(extension => Path.Combine(directory, name + extension))))
            .Where(File.Exists);
    }

    /// <summary>Follows symbolic links (Homebrew, Linux packages) and Scoop shims to the real launcher script.</summary>
    private static string ResolveLauncher(string launcher)
    {
        var shim = Path.ChangeExtension(launcher, ".shim");
        if (File.Exists(shim) && ShimTarget(File.ReadAllText(shim)) is { } target)
        {
            return target;
        }

        try
        {
            return File.ResolveLinkTarget(launcher, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(launcher);
        }
        catch (IOException)
        {
            return Path.GetFullPath(launcher);
        }
    }

    internal static string? ShimTarget(string shim)
    {
        var match = ShimPath().Match(shim);
        return match.Success ? match.Groups["path"].Value : null;
    }

    [GeneratedRegex(@"^1\.23\.\d+$")]
    private static partial Regex SupportedVersion();

    [GeneratedRegex("""^\s*path\s*=\s*"?(?<path>[^"\r\n]+?)"?\s*$""", RegexOptions.Multiline)]
    private static partial Regex ShimPath();
}
