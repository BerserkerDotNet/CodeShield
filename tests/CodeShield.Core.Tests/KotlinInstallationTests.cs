using System.IO.Compression;
using CodeShield.Abstractions;
using CodeShield.Kotlin;

namespace CodeShield.Core.Tests;

/// <summary>
/// Discovery of the Java runtime, detekt-cli and the Kotlin worker command from fixture files in a temporary directory; no Java
/// runtime or detekt installation is needed.
/// </summary>
public sealed class KotlinInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codeshield-kt-" + Guid.NewGuid().ToString("N"));

    public KotlinInstallationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ConfiguredJarIsUsedWithItsManifestVersion()
    {
        var jar = Jar("detekt-cli-1.23.8-all.jar", "1.23.8");

        var detekt = DetektInstallation.FromConfigured(jar);

        Assert.True(detekt.IsUsable);
        Assert.Equal("1.23.8", detekt.Version);
        Assert.Equal([Path.GetFullPath(jar)], detekt.ClassPath);
    }

    [Fact]
    public void ConfiguredJarOfAnotherDetektLineIsRejected()
    {
        var jar = Jar("detekt-cli-1.22.0-all.jar", "1.22.0");

        var detekt = DetektInstallation.FromConfigured(jar);

        Assert.False(detekt.IsUsable);
        Assert.Equal("1.22.0", detekt.Version);
        Assert.StartsWith($"detekt-cli 1.22.0 ('{jar}') is not supported; detekt-cli 1.23.x is required.", detekt.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredPathsThatAreNotDetektAreRejected()
    {
        var plain = Jar("plain.jar", version: null);
        var text = Path.Combine(_root, "detekt.txt");
        File.WriteAllText(text, "not a jar");
        var missing = Path.Combine(_root, "missing");

        Assert.StartsWith($"'{plain}' is not a detekt-cli distribution: no jar declares a DetektVersion.",
            DetektInstallation.FromConfigured(plain).Problem, StringComparison.Ordinal);
        Assert.Equal($"CODESHIELD_DETEKT ('{text}') is not a .jar file or a detekt-cli installation directory.",
            DetektInstallation.FromConfigured(text).Problem);
        Assert.StartsWith($"CODESHIELD_DETEKT ('{missing}') does not exist.", DetektInstallation.FromConfigured(missing).Problem, StringComparison.Ordinal);
        Assert.Null(DetektInstallation.ReadVersion(text));
    }

    [Theory]
    [InlineData("lib")]
    [InlineData("libexec")]
    [InlineData("libexec/lib")]
    public void ConfiguredInstallDirectoryPutsEveryJarOfItsLibraryOnTheClassPath(string library)
    {
        var detektJar = Jar($"detekt/{library}/detekt-cli-1.23.0-all.jar", "1.23.0");
        var extra = Jar($"detekt/{library}/annotations.jar", version: null);

        var detekt = DetektInstallation.FromConfigured(Path.Combine(_root, "detekt"));

        Assert.True(detekt.IsUsable, detekt.Problem);
        Assert.Equal("1.23.0", detekt.Version);
        Assert.Equal([Path.GetFullPath(extra), Path.GetFullPath(detektJar)], detekt.ClassPath);
    }

    [Fact]
    public void InstallDirectoryWithoutDetektJarIsRejected()
    {
        Jar("empty/lib/other.jar", "1.23.8");

        var detekt = DetektInstallation.FromConfigured(Path.Combine(_root, "empty"));

        Assert.StartsWith($"No detekt-cli jar was found under '{Path.Combine(_root, "empty")}'", detekt.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherOnPathLeadsToItsInstallation()
    {
        Write("tools/detekt-cli-1.23.8/bin/detekt-cli", "#!/bin/sh");
        var jar = Jar("tools/detekt-cli-1.23.8/lib/detekt-cli-1.23.8-all.jar", "1.23.8");
        var path = string.Join(Path.PathSeparator, Path.Combine(_root, "nowhere"), Path.Combine(_root, "tools", "detekt-cli-1.23.8", "bin"));

        var detekt = DetektInstallation.FromPath(path);

        Assert.True(detekt.IsUsable, detekt.Problem);
        Assert.Equal(Path.Combine(_root, "tools", "detekt-cli-1.23.8", "bin", "detekt-cli"), detekt.Source);
        Assert.Equal([Path.GetFullPath(jar)], detekt.ClassPath);
    }

    [Fact]
    public void ScoopShimIsFollowedToTheApplication()
    {
        var app = Path.Combine(_root, "scoop", "apps", "detekt-cli", "current");
        Write("scoop/shims/detekt-cli", "shim");
        Write("scoop/shims/detekt-cli.shim", $"path = \"{Path.Combine(app, "bin", "detekt-cli.bat")}\"\nargs = \n");
        var jar = Jar("scoop/apps/detekt-cli/current/lib/detekt-cli-1.23.7-all.jar", "1.23.7");

        var detekt = DetektInstallation.FromPath(Path.Combine(_root, "scoop", "shims"));

        Assert.True(detekt.IsUsable, detekt.Problem);
        Assert.Equal("1.23.7", detekt.Version);
        Assert.Equal([Path.GetFullPath(jar)], detekt.ClassPath);
    }

    [Fact]
    public void MissingLauncherExplainsHowToInstall()
    {
        var detekt = DetektInstallation.FromPath(Path.Combine(_root, "nowhere"));

        Assert.False(detekt.IsUsable);
        Assert.Empty(detekt.ClassPath);
        Assert.StartsWith("detekt-cli was not found. Install detekt-cli 1.23.x", detekt.Problem, StringComparison.Ordinal);
        Assert.EndsWith("set CODESHIELD_DETEKT to the detekt-cli-<version>-all.jar or the installation directory.", detekt.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void JavaIsTakenFromTheOverrideThenJavaHomeThenPath()
    {
        var java = Write($"jdk/bin/{(OperatingSystem.IsWindows() ? "java.exe" : "java")}", "");

        Assert.Equal("/custom/java", KotlinLocation.JavaExecutableFrom("/custom/java", Path.Combine(_root, "jdk")));
        Assert.Equal(java, KotlinLocation.JavaExecutableFrom(null, Path.Combine(_root, "jdk")));
        Assert.Equal("java", KotlinLocation.JavaExecutableFrom(" ", Path.Combine(_root, "no-jdk")));
        Assert.Equal("java", KotlinLocation.JavaExecutableFrom(null, null));
    }

    [Fact]
    public async Task MissingWorkerJarIsAMissingAnalyzerForEveryCommand()
    {
        var location = new KotlinLocation("java", Path.Combine(_root, "absent.jar"), new DetektInstallation(null, [], null, "unused"));
        var analyzer = new KotlinAnalyzer(location, CorrectnessCatalog.Pinned);
        const string message = "The Kotlin worker (kotlin-worker/dist/codeshield-kotlin-worker.jar) was not found; run scripts/setup.";

        var analysis = await analyzer.AnalyzeAsync(new AnalysisRequest(_root, ["A.kt"], ["A.kt"], [], false), CancellationToken.None);
        var tokens = await analyzer.TokenizeAsync(new TokenizeRequest(_root, ["A.kt"]), CancellationToken.None);
        var (versions, failure) = await analyzer.VersionAsync(CancellationToken.None);

        Assert.Equal(Languages.Kotlin, analysis.Language);
        Assert.Equal([new Reason(ReasonCodes.MissingAnalyzer, message)], analysis.Failures);
        Assert.Equal([new Reason(ReasonCodes.MissingAnalyzer, message)], tokens.Failures);
        Assert.Null(versions);
        Assert.Equal(new Reason(ReasonCodes.MissingAnalyzer, message), failure);
    }

    [Fact]
    public void UnusableDetektIsReportedWithItsProblem()
    {
        var detekt = new DetektInstallation(null, [], null, "detekt-cli was not found.");
        var analyzer = new KotlinAnalyzer(new KotlinLocation("java", Write("worker.jar", ""), detekt), CorrectnessCatalog.Pinned);

        var (command, failure) = analyzer.Command();

        Assert.Null(command);
        Assert.Equal(new Reason(ReasonCodes.MissingAnalyzer, "detekt-cli was not found."), failure);
    }

    [Fact]
    public void CommandRunsTheWorkerWithDetektOnTheClassPath()
    {
        var worker = Write("worker.jar", "");
        var detekt = new DetektInstallation("detekt", ["/d/lib/a.jar", "/d/lib/detekt-cli-1.23.8-all.jar"], "1.23.8", null);
        var analyzer = new KotlinAnalyzer(new KotlinLocation("/jdk/bin/java", worker, detekt), CorrectnessCatalog.Pinned);

        var (command, failure) = analyzer.Command();

        Assert.Null(failure);
        Assert.Equal("/jdk/bin/java", command!.Executable);
        var classPath = command.Arguments[command.Arguments.ToList().IndexOf("-cp") + 1];
        Assert.Equal(string.Join(Path.PathSeparator, worker, "/d/lib/a.jar", "/d/lib/detekt-cli-1.23.8-all.jar"), classPath);
        Assert.Equal("codeshield.kotlin.Main", command.Arguments[^1]);
        Assert.Contains("-Xss16m", command.Arguments);
    }

    [Fact]
    public async Task JavaThatCannotStartIsAMissingAnalyzer()
    {
        var java = Path.Combine(_root, "no-such-java");
        var detekt = new DetektInstallation("detekt", [Jar("lib/detekt-cli-1.23.8-all.jar", "1.23.8")], "1.23.8", null);
        var analyzer = new KotlinAnalyzer(new KotlinLocation(java, Write("worker.jar", ""), detekt), CorrectnessCatalog.Pinned);

        var tokens = await analyzer.TokenizeAsync(new TokenizeRequest(_root, ["A.kt"]), CancellationToken.None);

        var failure = Assert.Single(tokens.Failures);
        Assert.Equal(ReasonCodes.MissingAnalyzer, failure.Code);
        Assert.StartsWith($"Java could not be started ('{java}'):", failure.Message, StringComparison.Ordinal);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return Path.GetFullPath(path);
    }

    private string Jar(string relative, string? version)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(version is null ? "readme.txt" : "META-INF/MANIFEST.MF").Open());
        writer.Write(version is null ? "no manifest" : Manifest(version));
        return path;
    }

    private static string Manifest(string version) =>
        $"Manifest-Version: 1.0\r\nMain-Class: io.gitlab.arturbosch.detekt.cli.Main\r\nDetektVersion: {version}\r\n\r\n";
}
