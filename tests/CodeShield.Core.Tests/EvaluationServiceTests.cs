using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Evaluation;
using CodeShield.Core.Git;
using CodeShield.Core.Rules;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Tests;

public sealed class EvaluationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codeshield-ut-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGit _git;

    public EvaluationServiceTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.ts"), "x\n");
        _git = new FakeGit(_root);
        _git.Paths.AddRange(["a.ts", "package.json"]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private EvaluationService Service(ILanguageAnalyzer analyzer, EvaluationStore? store = null)
    {
        var supported = new SupportedFiles([analyzer]);
        var planner = new JobPlanner(supported);
        var collector = new SnapshotCollector(planner, supported, new SnapshotMaterializer(_git));
        IRule[] rules = [new MethodMetricRule(), new CohesionRule(), new FileLengthRule(), new MainSequenceRule()];
        return new EvaluationService(new ScopeDiscovery(_git, supported), planner, collector, new RuleRunner(rules), store ?? new EvaluationStore());
    }

    private void ModifyA(int lines)
    {
        _git.Changes.Add(new NameStatusEntry(ChangeKind.Modified, "a.ts", null));
        _git.Diff = $"diff --git a/a.ts b/a.ts\n@@ -1,0 +1,{lines} @@\n";
    }

    private Task<EvaluationResponse> EvaluateAsync(EvaluationService service, string version = "1") =>
        service.EvaluateAsync(new EvaluateRequest(version, _root, "HEAD", null), CancellationToken.None);

    [Theory]
    [InlineData(2, 90, true)]
    [InlineData(3, 85, false)]
    [InlineData(25, 0, false)]
    public async Task ScoreIsFixedDeductionsFromHundredWithFloorAndPassesAtNinety(int methods, int score, bool passed)
    {
        ModifyA(methods);
        var response = await EvaluateAsync(Service(new FakeAnalyzer(methods, 6)));
        Assert.Equal(EvaluationStatus.Complete, response.Status);
        Assert.Equal(score, response.Score);
        Assert.Equal(passed, response.Passed);
    }

    [Fact]
    public async Task IndexRemovedFileWithIdenticalWorkingBytesIsNoChanges()
    {
        _git.Changes.Add(new NameStatusEntry(ChangeKind.Deleted, "a.ts", null));
        _git.Untracked.Add("a.ts");
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6)));
        Assert.Equal(EvaluationStatus.NoChanges, response.Status);
    }

    [Fact]
    public async Task IndexRemovedFileWithEditedWorkingBytesIsEvaluatedAsAModification()
    {
        _git.Changes.Add(new NameStatusEntry(ChangeKind.Deleted, "a.ts", null));
        _git.Untracked.Add("a.ts");
        _git.WorkingFileDiffs["a.ts"] = "@@ -1,0 +1,1 @@\n";
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6)));
        Assert.Equal(EvaluationStatus.Complete, response.Status);
        Assert.Equal(95, response.Score);
        Assert.Equal([new ScopeEntry("a.ts", "modified")], response.Scope);
    }

    [Fact]
    public async Task UntrackedPathThatIsAlsoARenameSourceIsAnIncompleteResultNotAGitFailure()
    {
        _git.Changes.Add(new NameStatusEntry(ChangeKind.Renamed, "b.ts", "a.ts"));
        _git.Untracked.Add("a.ts");
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6)));
        Assert.Equal(EvaluationStatus.Incomplete, response.Status);
        Assert.Null(response.Score);
        Assert.Equal(ReasonCodes.UnreconciledScope, response.Reasons[0].Code);
    }

    [Fact]
    public async Task FailedAnalysisMakesTheResultIncompleteWithoutAScore()
    {
        ModifyA(1);
        var failure = new Reason(ReasonCodes.MissingAnalyzer, "no node");
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6, failure)));
        Assert.Equal(EvaluationStatus.Incomplete, response.Status);
        Assert.Null(response.Score);
        Assert.Null(response.Passed);
    }

    [Fact]
    public async Task ChangesOutsideSupportedFilesReportNoChanges()
    {
        _git.Changes.Add(new NameStatusEntry(ChangeKind.Modified, "README.md", null));
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6)));
        Assert.Equal(EvaluationStatus.NoChanges, response.Status);
        Assert.Equal("NO_SUPPORTED_CHANGES", response.Reasons[0].Code);
    }

    [Fact]
    public async Task GitFailuresBecomeErrorResponses()
    {
        _git.ResolveFailure = new GitException(ReasonCodes.InvalidBaseline, "bad revision");
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6)));
        Assert.Equal(EvaluationStatus.Error, response.Status);
        Assert.Equal(ReasonCodes.InvalidBaseline, response.Reasons[0].Code);
    }

    [Fact]
    public async Task UnsupportedContractVersionIsRejectedBeforeAnyGitWork()
    {
        var response = await EvaluateAsync(Service(new FakeAnalyzer(1, 6)), version: "2");
        Assert.Equal(ReasonCodes.UnsupportedContractVersion, response.Reasons[0].Code);
    }

    [Fact]
    public async Task DetailsAreServedFromTheStoredEvaluationAndUnknownIdsAreErrors()
    {
        ModifyA(2);
        var store = new EvaluationStore();
        var service = Service(new FakeAnalyzer(2, 6), store);
        var response = await EvaluateAsync(service);
        var details = new RuleDetailsService(store);
        var found = details.Get(new RuleDetailsRequest("1", response.EvaluationId, "readability.parameter-count"));
        Assert.Equal(2, found.Findings.Count);
        var missingRule = details.Get(new RuleDetailsRequest("1", response.EvaluationId, "complexity.nesting"));
        Assert.Equal(ReasonCodes.RuleNotFound, missingRule.Reasons[0].Code);
        var missingEvaluation = details.Get(new RuleDetailsRequest("1", "nope", "complexity.nesting"));
        Assert.Equal(ReasonCodes.EvaluationNotFound, missingEvaluation.Reasons[0].Code);
    }
}
