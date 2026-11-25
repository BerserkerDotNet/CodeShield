using CodeShield.Abstractions;
using CodeShield.Core.Configuration;
using CodeShield.Core.Contracts;
using CodeShield.Core.Evaluation;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Tests;

public sealed class ExclusionTests
{
    private static readonly Ruleset Rules = RulesetCatalog.Default;

    private static RulePolicy Policy(params (string Path, string Json)[] files) =>
        new([.. files.Select(file => new ConfigReader(file.Path, Rules).Read(System.Text.Encoding.UTF8.GetBytes(file.Json), "sha"))]);

    private static RuleFinding Finding(string ruleId, params string[] files) => new(
        Rules.Get(ruleId), "ctx", Attribution.Introduced, 5,
        [.. files.Select(file => new FindingLocation(file, 1, 1, 1, 1))], 1, 1, "c", []);

    private static ContextFacts Context(string manifest, params string[] owned) =>
        new(manifest, Languages.CSharp, manifest, null, manifest, owned, [], [], [], [], []);

    private const string Kernel = """{ "version": 1, "rules": { "coupling.main-sequence-distance": { "enabled": false, "reason": "kernel" } } }""";

    [Fact]
    public void FindingIsDroppedOnlyWhenExcludedAtEveryLocation()
    {
        var policy = Policy(("k/.codeshield.json", """{ "version": 1, "exclude": true, "reason": "r" }"""));
        var run = new RuleRun([Finding(RuleIds.Nesting, "k/a.cs"), Finding(RuleIds.Nesting, "k/a.cs", "m/b.cs")], []);
        var result = ExclusionFilter.Apply(run, SnapshotFacts.Empty, policy);
        Assert.Single(result.Findings);
        Assert.Equal(1, result.ExcludedFindings![RuleIds.Nesting]);
    }

    [Fact]
    public void WithoutConfigurationRunIsReturnedUntouched()
    {
        var run = new RuleRun([Finding(RuleIds.Nesting, "a.cs")], []);
        Assert.Same(run, ExclusionFilter.Apply(run, SnapshotFacts.Empty, RulePolicy.Defaults));
    }

    [Fact]
    public void ExcludedCoverageOverridesContextFailures()
    {
        var policy = Policy(("k/.codeshield.json", Kernel));
        var failed = new CoverageEntry(RuleIds.MainSequence, "k/k.csproj|net10.0", CoverageStatus.Failed,
            [new Reason(ReasonCodes.ProjectLoadFailed, "boom")]);
        var run = new RuleRun([], [failed, failed with { RuleId = RuleIds.Nesting }]);
        var result = ExclusionFilter.Apply(run, new SnapshotFacts([Context("k/k.csproj", "k/a.cs")], ModuleGraphFacts.Empty), policy);
        Assert.Equal(CoverageStatus.Excluded, result.Coverage[0].Status);
        Assert.Empty(result.Coverage[0].Reasons);
        Assert.Equal(CoverageStatus.Failed, result.Coverage[1].Status);
    }

    [Fact]
    public void SharedInputFailuresAreNotMaskedUnlessExcludedEverywhere()
    {
        var shared = new CoverageEntry(RuleIds.MainSequence, "repository", CoverageStatus.Failed,
            [new Reason(ReasonCodes.UnreadableSource, "locked")]);
        var partial = Policy(("k/.codeshield.json", Kernel));
        Assert.Equal(CoverageStatus.Failed, ExclusionFilter.Apply(new RuleRun([], [shared]), SnapshotFacts.Empty, partial).Coverage[0].Status);
        var everywhere = Policy((".codeshield.json", Kernel));
        var graph = shared with { Reasons = [new Reason(ReasonCodes.ProjectLoadFailed, "x")] };
        Assert.Equal(CoverageStatus.Excluded, ExclusionFilter.Apply(new RuleRun([], [graph]), SnapshotFacts.Empty, everywhere).Coverage[0].Status);
    }
}
