using CodeShield.Abstractions;
using CodeShield.Kotlin;
using CodeShield.Workers;

namespace CodeShield.Core.Tests;

/// <summary>File classification, version parsing and failure mapping of the Kotlin adapter, without any files, Java runtime or detekt.</summary>
public sealed class KotlinAdapterTests
{
    private static readonly KotlinAnalyzer Analyzer =
        new(new KotlinLocation("java", null, new DetektInstallation(null, [], null, "unused")), CorrectnessCatalog.Pinned);

    [Theory]
    [InlineData("openjdk version \"21.0.11\" 2026-04-21 LTS", 21, 0, 11, true)]
    [InlineData("openjdk version \"17\" 2021-09-14", 17, 0, 0, true)]
    [InlineData("java version \"1.8.0_352\"", 8, 0, 0, false)]
    [InlineData("openjdk version \"11.0.2\" 2019-01-15", 11, 0, 2, false)]
    public void JavaVersionIsParsedFromJavaVersionOutput(string output, int major, int minor, int build, bool supported)
    {
        var version = JavaRequirement.Parse(output);
        Assert.Equal(new Version(major, minor, build), version);
        Assert.Equal(supported, JavaRequirement.IsSupported(version!));
    }

    [Fact]
    public void UnparseableJavaOutputYieldsNoVersion() => Assert.Null(JavaRequirement.Parse("Error: could not find java.dll"));

    [Theory]
    [InlineData("1.23.0", true)]
    [InlineData("1.23.8", true)]
    [InlineData("1.22.0", false)]
    [InlineData("1.23.8-RC1", false)]
    [InlineData("2.0.0-alpha.1", false)]
    public void OnlyTheDetekt123LineIsSupported(string version, bool supported) =>
        Assert.Equal(supported, DetektInstallation.IsSupported(version));

    [Fact]
    public void ManifestAttributeJoinsContinuationLines()
    {
        const string manifest = "Manifest-Version: 1.0\r\nClass-Path: a.jar\r\n b.jar\r\nDetektVersion: 1.2\r\n 3.8\r\n";
        Assert.Equal("1.23.8", DetektInstallation.ManifestAttribute(manifest, "DetektVersion"));
        Assert.Null(DetektInstallation.ManifestAttribute(manifest, "Missing"));
    }

    [Theory]
    [InlineData("path = \"C:\\scoop\\apps\\detekt\\current\\bin\\detekt.bat\"\nargs =", "C:\\scoop\\apps\\detekt\\current\\bin\\detekt.bat")]
    [InlineData("args = -x\r\npath = /opt/detekt/bin/detekt\r\n", "/opt/detekt/bin/detekt")]
    [InlineData("args = -x\n", null)]
    public void ShimTargetIsReadFromThePathLine(string shim, string? target) =>
        Assert.Equal(target, DetektInstallation.ShimTarget(shim));

    [Theory]
    [InlineData("app/src/main/kotlin/App.kt", true)]
    [InlineData("App.kt", true)]
    [InlineData("app/src/main/kotlin/build/Output.kt", true)]
    [InlineData("app/src/main/kotlin/App.KT", true)]
    [InlineData("app/build/generated/ksp/Gen.kt", false)]
    [InlineData("build/tmp/Gen.kt", false)]
    [InlineData(".gradle/kotlin/Cache.kt", false)]
    [InlineData("app/build.gradle.kts", false)]
    [InlineData("scripts/run.kts", false)]
    [InlineData("app/src/main/java/App.java", false)]
    public void SourcesAreKotlinFilesOutsideGradleOutput(string path, bool source) =>
        Assert.Equal(source, Analyzer.IsSource(path));

    [Theory]
    [InlineData("settings.gradle.kts", true)]
    [InlineData("app/build.gradle", true)]
    [InlineData("gradle/publishing.gradle.kts", true)]
    [InlineData("app/build/tmp/applied.gradle.kts", false)]
    [InlineData("gradle/libs.versions.toml", false)]
    [InlineData("gradle.properties", false)]
    public void ConfigurationIsGradleScriptsOutsideGradleOutput(string path, bool configuration) =>
        Assert.Equal(configuration, Analyzer.IsConfiguration(path));

    [Fact]
    public void WorkerExitsAreClassified()
    {
        var oldJava = KotlinAnalyzer.Classify("/jdk8/java", new WorkerRun(1, "",
            "Error: LinkageError occurred while loading main class codeshield.kotlin.Main\n\tjava.lang.UnsupportedClassVersionError: ..."));
        var missingDetekt = KotlinAnalyzer.Classify("java", new WorkerRun(3, "", "detekt-cli 1.23.x is not on the class path.\n"));
        var crash = KotlinAnalyzer.Classify("java", new WorkerRun(1, "", "java.lang.OutOfMemoryError"));

        Assert.Equal(new Reason(ReasonCodes.MissingAnalyzer,
            "The Kotlin worker needs a Java runtime (JDK or JRE) 17 or newer; '/jdk8/java' is older. Set CODESHIELD_JAVA or JAVA_HOME."), oldJava);
        Assert.Equal(new Reason(ReasonCodes.MissingAnalyzer, "detekt-cli 1.23.x is not on the class path."), missingDetekt);
        Assert.Null(crash);
    }

    [Theory]
    [InlineData("InvalidRange", "correctness.kotlin.invalid-range")]
    [InlineData("IteratorNotThrowingNoSuchElementException", "correctness.kotlin.iterator-not-throwing-no-such-element-exception")]
    public void KotlinEvaluatorIdsAreKebabCaseDetektRuleIds(string analyzerId, string evaluatorId) =>
        Assert.Equal(evaluatorId, new CorrectnessRule(Languages.Kotlin, analyzerId, "x").EvaluatorId);
}
