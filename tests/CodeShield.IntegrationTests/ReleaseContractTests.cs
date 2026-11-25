using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodeShield.IntegrationTests;

/// <summary>Contracts that keep releases consistent, safe to automate and legally complete.</summary>
public sealed partial class ReleaseContractTests
{
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CHANGELOG.md")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("CHANGELOG.md not found.");
    }

    private static (int ExitCode, string Output) RunCheck(params string[] arguments)
    {
        var script = Path.Combine(Root(), "scripts", "check-release.ps1");
        var shells = OperatingSystem.IsWindows() ? new[] { "powershell", "pwsh" } : new[] { "pwsh" };
        foreach (var shell in shells)
        {
            try
            {
                return Run(shell, script, arguments);
            }
            catch (Win32Exception)
            {
            }
        }

        throw new InvalidOperationException("Neither pwsh nor powershell is available.");
    }

    private static (int ExitCode, string Output) Run(string shell, string script, string[] arguments)
    {
        var start = new ProcessStartInfo(shell) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-File", script }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [Fact]
    public void ProductVersionAgreesAcrossProjectPluginManifestsAndChangelog()
    {
        var (exitCode, output) = RunCheck();

        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void ATagThatDiffersFromTheProductVersionIsRejected()
    {
        var (exitCode, output) = RunCheck("-Tag", "v99.0.0");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("does not match product version", output);
    }

    [Fact]
    public void ReleaseNotesAreExtractedFromTheMatchingChangelogSection()
    {
        var version = RunCheck().Output.Trim();
        var copy = Path.Combine(Path.GetTempPath(), $"cs-release-{Guid.NewGuid():N}");
        try
        {
            foreach (var relative in new[]
                     {
                         Path.Combine("src", "CodeShield.Mcp", "CodeShield.Mcp.csproj"),
                         Path.Combine("integrations", "codeshield", "plugin.json"),
                         Path.Combine("integrations", "codeshield", ".claude-plugin", "plugin.json"),
                     })
            {
                Directory.CreateDirectory(Path.Combine(copy, Path.GetDirectoryName(relative)!));
                File.Copy(Path.Combine(Root(), relative), Path.Combine(copy, relative));
            }

            var changelog = File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"));
            File.WriteAllText(Path.Combine(copy, "CHANGELOG.md"), changelog.Replace("## [Unreleased]", $"## [{version}] - 2026-01-01", StringComparison.Ordinal));
            var notes = Path.Combine(copy, "notes.md");
            var (exitCode, output) = RunCheck("-Tag", $"v{version}", "-NotesPath", notes, "-RepoRoot", copy);

            Assert.True(exitCode == 0, output);
            Assert.Contains("### Added", File.ReadAllText(notes));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public void WorkflowsPinActionsToCommitsAndNeverRunUntrustedCodeWithSecrets()
    {
        foreach (var workflow in Directory.GetFiles(Path.Combine(Root(), ".github", "workflows"), "*.yml"))
        {
            var text = File.ReadAllText(workflow);
            foreach (Match use in UsesPattern().Matches(text))
            {
                Assert.True(use.Groups["ref"].Value.Length == 0 || FullSha().IsMatch(use.Groups["ref"].Value) || use.Groups["target"].Value.StartsWith("./", StringComparison.Ordinal),
                    $"{Path.GetFileName(workflow)}: '{use.Value}' is not pinned to a commit SHA.");
            }

            Assert.DoesNotContain("pull_request_target", text);
            Assert.DoesNotContain("nuget push", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("permissions:", text);
        }
    }

    [Fact]
    public void PackageMetadataDeclaresTheMitLicenseWithoutSuppressingTheWarning()
    {
        var project = File.ReadAllText(Path.Combine(Root(), "src", "CodeShield.Mcp", "CodeShield.Mcp.csproj"));

        Assert.Contains("<PackageLicenseExpression>MIT</PackageLicenseExpression>", project);
        Assert.DoesNotContain("NU5125", project);
        Assert.StartsWith("MIT License", File.ReadAllText(Path.Combine(Root(), "LICENSE")));
    }

    [InstalledFact]
    public void InstalledPackageCarriesTheLicenseAndUpstreamNotices()
    {
        var toolRoot = Path.GetDirectoryName(Environment.GetEnvironmentVariable(InstalledFactAttribute.Variable)!)!;
        var notices = Directory.GetFiles(toolRoot, "THIRD-PARTY-NOTICES.md", SearchOption.AllDirectories).Single();
        var package = Path.GetDirectoryName(notices)!;

        foreach (var expected in new[]
                 {
                     "LICENSE",
                     Path.Combine("licenses", "Apache-2.0.txt"),
                     Path.Combine("licenses", "Microsoft.CodeAnalysis.Common", "ThirdPartyNotices.rtf"),
                     Path.Combine("licenses", "Microsoft.CodeAnalysis.CSharp", "ThirdPartyNotices.rtf"),
                     Path.Combine("licenses", "Microsoft.CodeAnalysis.NetAnalyzers", "ThirdPartyNotices.txt"),
                     Path.Combine("licenses", "Microsoft.Extensions.Hosting", "THIRD-PARTY-NOTICES.TXT"),
                 })
        {
            Assert.True(File.Exists(Path.Combine(package, expected)), $"Installed package is missing {expected}.");
        }
    }

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("1.2.3-rc.1", true)]
    [InlineData("1.0.0-0A.is.legal", true)]
    [InlineData("01.2.3", false)]
    [InlineData("1.02.3", false)]
    [InlineData("0.1.0-..", false)]
    [InlineData("0.1.0-", false)]
    [InlineData("1.0.0-alpha..1", false)]
    [InlineData("1.0.0-01", false)]
    [InlineData("1.0", false)]
    public void ProductVersionMustBeStrictSemanticVersioning(string version, bool valid)
    {
        var fixture = CreateFixture(version, "## [Unreleased]\n");
        try
        {
            var (exitCode, output) = RunCheck("-RepoRoot", fixture);

            if (valid)
            {
                Assert.True(exitCode == 0, output);
                Assert.Equal(version, output.Trim());
            }
            else
            {
                Assert.NotEqual(0, exitCode);
                Assert.Contains("Semantic Versioning", output);
            }
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    [Fact]
    public void ReleaseWorkflowMetadataStepSucceedsForAMatchingTagAndFailsForAWrongOne()
    {
        var fixture = CreateFixture("2.3.4", "## [2.3.4] - 2026-01-01\n\n### Added\n- Something.\n");
        try
        {
            var script = MetadataStepScript();
            var good = RunWorkflowStep(script, fixture, "v2.3.4");
            var bad = RunWorkflowStep(script, fixture, "v9.9.9");

            Assert.True(good.ExitCode == 0, good.Output);
            Assert.Equal(["tag=v2.3.4", "version=2.3.4"], good.GitHubOutput);
            Assert.NotEqual(0, bad.ExitCode);
            Assert.Empty(bad.GitHubOutput);
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static string CreateFixture(string version, string changelog)
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"cs-release-{Guid.NewGuid():N}");
        foreach (var relative in new[]
                 {
                     Path.Combine("scripts", "check-release.ps1"),
                     Path.Combine("src", "CodeShield.Mcp", "CodeShield.Mcp.csproj"),
                     Path.Combine("integrations", "codeshield", "plugin.json"),
                     Path.Combine("integrations", "codeshield", ".claude-plugin", "plugin.json"),
                 })
        {
            Directory.CreateDirectory(Path.Combine(fixture, Path.GetDirectoryName(relative)!));
            var text = File.ReadAllText(Path.Combine(Root(), relative));
            text = relative.EndsWith(".csproj", StringComparison.Ordinal) ? VersionElement().Replace(text, $"<Version>{version}</Version>")
                : relative.EndsWith(".json", StringComparison.Ordinal) ? ManifestVersion().Replace(text, $"\"version\": \"{version}\"", 1)
                : text;
            File.WriteAllText(Path.Combine(fixture, relative), text);
        }

        File.WriteAllText(Path.Combine(fixture, "CHANGELOG.md"), changelog);
        return fixture;
    }

    // The exact script of the release workflow's metadata step, so the test exercises the real wrapper.
    private static string MetadataStepScript()
    {
        var workflow = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "release.yml")).Replace("\r\n", "\n");
        var step = MetaStep().Match(workflow);
        Assert.True(step.Success, "release.yml has no 'meta' step with a run block.");
        return string.Join('\n', step.Groups["body"].Value.Split('\n').Select(line => line.Length >= 10 ? line[10..] : line.Trim()));
    }

    private static (int ExitCode, string Output, string[] GitHubOutput) RunWorkflowStep(string script, string workingDirectory, string tag)
    {
        var outputFile = Path.Combine(workingDirectory, $"github-output-{tag}.txt");
        var shells = OperatingSystem.IsWindows() ? new[] { "pwsh", "powershell" } : new[] { "pwsh" };
        foreach (var shell in shells)
        {
            var start = new ProcessStartInfo(shell) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory };
            start.Environment["RELEASE_TAG"] = tag;
            start.Environment["GITHUB_OUTPUT"] = outputFile;
            start.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                var lines = File.Exists(outputFile) ? File.ReadAllLines(outputFile).Where(line => line.Length > 0).ToArray() : [];
                return (process.ExitCode, output, lines);
            }
            catch (Win32Exception)
            {
            }
        }

        throw new InvalidOperationException("Neither pwsh nor powershell is available.");
    }

    [GeneratedRegex(@"- id: meta\n(?:.*\n)*?\s+run: \|\n(?<body>(?:          .*\n?)+)")]
    private static partial Regex MetaStep();

    [GeneratedRegex("\"version\":\\s*\"[^\"]+\"")]
    private static partial Regex ManifestVersion();

    [GeneratedRegex("<Version>[^<]+</Version>")]
    private static partial Regex VersionElement();

    [GeneratedRegex(@"uses:\s*(?<target>[^@\s]+)(@(?<ref>[^\s#]+))?")]
    private static partial Regex UsesPattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullSha();
}
