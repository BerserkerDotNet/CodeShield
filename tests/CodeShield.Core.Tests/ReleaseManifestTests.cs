using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeShield.Abstractions;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Tests;

/// <summary>Keeps docs/release-manifest.json identical to the ruleset compiled into the host.</summary>
public sealed class ReleaseManifestTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    [Fact]
    public void ManifestMatchesCompiledRuleset()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "release-manifest.json");
        var expected = JsonSerializer.Serialize(Build(), Options).ReplaceLineEndings("\n");
        if (Environment.GetEnvironmentVariable("UPDATE_MANIFEST") == "1")
        {
            File.WriteAllText(path, expected + "\n");
        }

        Assert.Equal(expected, File.ReadAllText(path).Trim().ReplaceLineEndings("\n"));
    }

    private static object Build()
    {
        var ruleset = RulesetCatalog.Default;
        return new
        {
            contractVersion = "1",
            ruleset = ruleset.Qualified,
            threshold = ruleset.Threshold,
            calibration = "unreviewed: limits and deductions are fixed by the specification; no empirical calibration was performed",
            rules = ruleset.Rules.Select(rule => new
            {
                id = rule.Id,
                category = rule.Category,
                limit = rule.Limit,
                deduction = rule.Deduction,
                calculation = rule.Calculation,
            }),
            correctnessSelection = CorrectnessCatalog.All.Select(rule => new { rule.Language, rule.AnalyzerId, ruleId = rule.EvaluatorId, options = JsonDocument.Parse(rule.OptionsJson).RootElement.Clone() }),
            analyzers = new
            {
                roslyn = "Microsoft.CodeAnalysis.CSharp 5.12.0",
                netAnalyzers = "Microsoft.CodeAnalysis.NetAnalyzers 10.0.404",
                csharpAnalyzerOptions = "selected CA rules enabled as warnings, every other diagnostic suppressed; no editorconfig or ruleset files are read",
                typescript = "5.9.3",
                eslint = "10.15.0",
                typescriptEslint = "8.73.0",
                eslintOptions = "flat config built by the worker: typescript-eslint parser with the owning tsconfig as project; only the selected rules enabled at error severity; repository eslint configuration is ignored",
            },
            runtimes = new
            {
                dotnetSdk = "10.0.400",
                nodeMinimum = "22",
                git = "required on PATH",
            },
            summary = string.Create(CultureInfo.InvariantCulture, $"{ruleset.Rules.Count} rules"),
        };
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeShield.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
