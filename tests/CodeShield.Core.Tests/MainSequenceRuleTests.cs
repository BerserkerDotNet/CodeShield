using CodeShield.Abstractions;
using CodeShield.Core.Git;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Tests;

public sealed class MainSequenceRuleTests
{
    private const string Subject = "Subject.csproj";

    private static ContextFacts Module(int abstractTypes, int concreteTypes)
    {
        var types = Enumerable.Range(0, abstractTypes + concreteTypes)
            .Select(i => new TypeFact($"T{i}", "Subject/a.cs", i < abstractTypes)).ToList();
        return new ContextFacts(Subject, Languages.CSharp, Subject, null, Subject, ["Subject/a.cs"], [], [], types, [], []);
    }

    private static SnapshotFacts Snapshot(ContextFacts context, int afferent, int efferent)
    {
        var edges = Enumerable.Range(0, afferent).Select(i => new ModuleEdge($"In{i}.csproj", Subject, false))
            .Concat(Enumerable.Range(0, efferent).Select(i => new ModuleEdge(Subject, $"Out{i}.csproj", false))).ToList();
        return new SnapshotFacts([context], new ModuleGraphFacts([], edges, [], []));
    }

    private static RuleOutput Evaluate(SnapshotFacts baseline, SnapshotFacts current, string changedPath)
    {
        var map = new ChangeMap([new FileChange(ChangeKind.Modified, changedPath, null, [])], [Subject, "Subject/a.cs"]);
        var scope = new ChangeScope("root", "commit", map, [Subject], [Subject], "");
        return new MainSequenceRule().Evaluate(new RuleInput(RulesetCatalog.Default, scope, current, baseline, new Dictionary<string, IReadOnlyList<FileTokens>>(), null, []));
    }

    [Fact]
    public void ExactTenthRegressionAboveHalfIsDeductedTwelve()
    {
        var output = Evaluate(Snapshot(Module(5, 5), 1, 0), Snapshot(Module(4, 6), 1, 0), "Subject/a.cs");
        var finding = Assert.Single(output.Findings);
        Assert.Equal(12, finding.Deduction);
    }

    [Fact]
    public void DistanceAtHalfIsNotAViolation()
    {
        var output = Evaluate(Snapshot(Module(1, 1), 1, 0), Snapshot(Module(1, 1), 1, 0), "Subject/a.cs");
        Assert.Empty(output.Findings);
    }

    [Fact]
    public void RegressionBelowATenthIsNotDeducted()
    {
        var output = Evaluate(Snapshot(Module(5, 5), 1, 0), Snapshot(Module(5, 5), 1, 0), "Subject/a.cs");
        Assert.Empty(output.Findings);
        Assert.Empty(Evaluate(Snapshot(Module(11, 11), 1, 0), Snapshot(Module(10, 12), 1, 0), "Subject/a.cs").Findings);
    }

    [Fact]
    public void DependencyOnlyChangeInManifestIsAssessed()
    {
        var output = Evaluate(Snapshot(Module(0, 5), 1, 1), Snapshot(Module(0, 5), 3, 0), Subject);
        Assert.Single(output.Findings);
    }

    [Fact]
    public void UnchangedModuleIsNotAssessed()
    {
        var output = Evaluate(Snapshot(Module(0, 5), 1, 0), Snapshot(Module(0, 5), 1, 0), "Elsewhere/b.cs");
        Assert.Empty(output.Findings);
    }
}
