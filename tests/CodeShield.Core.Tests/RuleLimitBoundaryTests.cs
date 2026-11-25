using CodeShield.Abstractions;
using CodeShield.Core.Configuration;
using CodeShield.Core.Git;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Tests;

/// <summary>The real metric rules must read the effective limit at their anchor folder and report it in the finding.</summary>
public sealed class RuleLimitBoundaryTests
{
    private static readonly Ruleset Rules = RulesetCatalog.Default;
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
    private const string Module = "pkg/p.csproj";
    private const string Owned = "pkg/a.cs";
    private const string Other = "elsewhere/b.cs";

    private static RulePolicy Policy(string ruleId, string limit, string folder = "pkg") =>
        new([new ConfigReader($"{folder}/.codeshield.json", Rules).Read(System.Text.Encoding.UTF8.GetBytes(
            $$"""{ "version": 1, "rules": { "{{ruleId}}": { "limit": {{limit}}, "reason": "boundary test" } } }"""), "sha")]);

    private static RuleOutput Run(IRule rule, ContextFacts context, RulePolicy policy, ContextFacts? baseline = null, ChangeKind kind = ChangeKind.Added)
    {
        var paths = context.OwnedFiles.Append(Module).ToList();
        var map = new ChangeMap(paths.Select(path => new FileChange(kind, path, null, [new Hunk(1, 0, 1, 1000)])), baseline is null ? [] : paths);
        var scope = new ChangeScope("root", "commit", map, [Module], [Module], "");
        var empty = SnapshotFacts.Empty;
        var current = new SnapshotFacts([context], ModuleGraphFacts.Empty);
        var before = baseline is null ? empty : new SnapshotFacts([baseline], ModuleGraphFacts.Empty);
        return rule.Evaluate(new RuleInput(Rules, scope, current, before, new Dictionary<string, IReadOnlyList<FileTokens>>(), null, []) { Policy = policy });
    }

    private static ContextFacts Context(
        IReadOnlyList<FileFact>? files = null, IReadOnlyList<ClassFact>? classes = null, IReadOnlyList<TypeFact>? types = null) =>
        new(Module, Languages.CSharp, Module, null, Module, [Owned, Other], files ?? [], classes ?? [], types ?? [], [], []);

    private static MethodFact Method(int value) =>
        new("m", "M", new SourceSpan(1, 1, 5, 1), value, value, value, value, [], []);

    private static FileFact FileWith(string path, int codeLines = 10, params MethodFact[] methods) => new(path, codeLines, false, methods);

    private static (decimal? Actual, decimal? Limit, int Deduction) Only(RuleOutput output, string ruleId)
    {
        var finding = output.Findings.Where(candidate => candidate.Rule.Id == ruleId).ToList();
        return finding.Count == 0 ? (null, null, 0)
            : (Convert.ToDecimal(finding[0].Actual, Invariant), Convert.ToDecimal(finding[0].Limit, Invariant), finding[0].Deduction);
    }

    [Theory]
    [InlineData(RuleIds.Cyclomatic, 6, 9)]
    [InlineData(RuleIds.Nesting, 2, 4)]
    [InlineData(RuleIds.MethodLength, 40, 50)]
    [InlineData(RuleIds.ParameterCount, 5, 8)]
    public void MethodMetricsFlagOnlyJustOverTheEffectiveLimit(string ruleId, int standard, int relaxed)
    {
        var rule = new MethodMetricRule();
        var policy = Policy(ruleId, relaxed.ToString(Invariant));
        var deduction = Rules.Get(ruleId).Deduction;

        Assert.Equal((null, null, 0), Only(Run(rule, Context([FileWith(Owned, 10, Method(relaxed))]), policy), ruleId));
        Assert.Equal((relaxed + 1, relaxed, deduction), Only(Run(rule, Context([FileWith(Owned, 10, Method(relaxed + 1))]), policy), ruleId));
        Assert.Equal((null, null, 0), Only(Run(rule, Context([FileWith(Other, 10, Method(standard))]), policy), ruleId));
        Assert.Equal((standard + 1, standard, deduction), Only(Run(rule, Context([FileWith(Other, 10, Method(standard + 1))]), policy), ruleId));
    }

    [Theory]
    [InlineData(RuleIds.Nesting, 1)]
    [InlineData(RuleIds.ParameterCount, 2)]
    [InlineData(RuleIds.Cyclomatic, 3)]
    [InlineData(RuleIds.MethodLength, 10)]
    public void StricterMethodLimitsNeedNoReasonAndApplyOnlyInsideTheFolder(string ruleId, int strict)
    {
        var rule = new MethodMetricRule();
        var policy = Policy(ruleId, strict.ToString(Invariant));
        Assert.Equal((strict + 1, strict, Rules.Get(ruleId).Deduction), Only(Run(rule, Context([FileWith(Owned, 10, Method(strict + 1))]), policy), ruleId));
        Assert.Equal((null, null, 0), Only(Run(rule, Context([FileWith(Other, 10, Method(strict + 1))]), policy), ruleId));
    }

    [Fact]
    public void FileLengthUsesTheFilesEffectiveLimit()
    {
        var rule = new FileLengthRule();
        var policy = Policy(RuleIds.FileLength, "600");
        var deduction = Rules.Get(RuleIds.FileLength).Deduction;

        Assert.Equal((null, null, 0), Only(Run(rule, Context([FileWith(Owned, 600)]), policy), RuleIds.FileLength));
        Assert.Equal((601, 600, deduction), Only(Run(rule, Context([FileWith(Owned, 601)]), policy), RuleIds.FileLength));
        Assert.Equal((null, null, 0), Only(Run(rule, Context([FileWith(Other, 400)]), policy), RuleIds.FileLength));
        Assert.Equal((401, 400, deduction), Only(Run(rule, Context([FileWith(Other, 401)]), policy), RuleIds.FileLength));
    }

    private static ClassFact Class(int components, string file = Owned) => new(
        "C", "C", [new ClassLocation(file, new SourceSpan(1, 1, 9, 1))], true,
        [.. Enumerable.Range(0, components).Select(i => new CohesionNode($"n{i}", $"N{i}"))], [],
        [.. Enumerable.Range(0, components).Select(i => (IReadOnlyList<string>)[$"n{i}"])]);

    [Fact]
    public void CohesionDefaultsToOneComponentAndHonoursAConfiguredLimitAtTheFirstDeclaration()
    {
        var rule = new CohesionRule();
        var deduction = Rules.Get(RuleIds.Lcom4).Deduction;
        var defaults = RulePolicy.Defaults;
        var relaxed = Policy(RuleIds.Lcom4, "2");

        Assert.Equal((null, null, 0), Only(Run(rule, Context(classes: [Class(1)]), defaults), RuleIds.Lcom4));
        Assert.Equal((2, 1, deduction), Only(Run(rule, Context(classes: [Class(2)]), defaults), RuleIds.Lcom4));
        Assert.Equal((null, null, 0), Only(Run(rule, Context(classes: [Class(2)]), relaxed), RuleIds.Lcom4));
        Assert.Equal((3, 2, deduction), Only(Run(rule, Context(classes: [Class(3)]), relaxed), RuleIds.Lcom4));
        Assert.Equal((2, 1, deduction), Only(Run(rule, Context(classes: [Class(2, Other)]), relaxed), RuleIds.Lcom4));
    }

    private static ContextFacts Types(int abstractTypes, int concreteTypes) => Context(types:
        [.. Enumerable.Range(0, abstractTypes + concreteTypes).Select(i => new TypeFact($"T{i}", Owned, i < abstractTypes))]);

    private static (decimal? Actual, decimal? Limit, int Deduction) Distance(ContextFacts current, ContextFacts? baseline, string limit)
    {
        var edges = new[] { new ModuleEdge("in.csproj", Module, false) };
        var graph = new ModuleGraphFacts([], edges, [], []);
        var map = new ChangeMap([new FileChange(baseline is null ? ChangeKind.Added : ChangeKind.Modified, Owned, null, [new Hunk(1, 0, 1, 1)])], baseline is null ? [] : [Owned, Module]);
        var scope = new ChangeScope("root", "commit", map, [Module], [Module], "");
        var before = baseline is null ? SnapshotFacts.Empty : new SnapshotFacts([baseline], graph);
        var input = new RuleInput(Rules, scope, new SnapshotFacts([current], graph), before, new Dictionary<string, IReadOnlyList<FileTokens>>(), null, [])
        { Policy = Policy(RuleIds.MainSequence, limit) };
        return Only(new MainSequenceRule().Evaluate(input), RuleIds.MainSequence);
    }

    [Fact]
    public void MainSequenceDecimalLimitAppliesToANewModule()
    {
        var module = Types(0, 5);
        Assert.Equal((null, null, 0), Distance(module, null, "1"));
        Assert.Equal((1m, 0.9m, Rules.Get(RuleIds.MainSequence).Deduction), Distance(module, null, "0.9"));
    }

    [Fact]
    public void MainSequenceKeepsTheTenthRegressionGateUnderAnyConfiguredLimit()
    {
        var current = Types(0, 5);
        Assert.Equal((null, null, 0), Distance(current, Types(1, 20), "0.1"));
        Assert.Equal((1m, 0.1m, Rules.Get(RuleIds.MainSequence).Deduction), Distance(current, Types(1, 4), "0.1"));
        Assert.Equal((null, null, 0), Distance(current, Types(1, 4), "1"));
    }
}
