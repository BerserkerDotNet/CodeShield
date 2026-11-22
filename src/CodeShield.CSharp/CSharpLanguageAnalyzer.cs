using CodeShield.Abstractions;
using CodeShield.CSharp.Evaluation;
using CodeShield.CSharp.Planning;

namespace CodeShield.CSharp;

/// <summary>Roslyn-based C# adapter: project evaluation without restore, semantic metrics, LCOM4 and the pinned CA rules.</summary>
/// <param name="assetsRoot">Folder whose obj\project.assets.json files supply NuGet references; defaults to the snapshot root.</param>
public sealed class CSharpLanguageAnalyzer(string? assetsRoot = null) : ILanguageAnalyzer
{
    public string Language => Languages.CSharp;

    public bool IsSource(string repoRelativePath) => FileKinds.IsSource(repoRelativePath);

    public bool IsConfiguration(string repoRelativePath) => FileKinds.IsConfiguration(repoRelativePath);

    public Task<LanguageAnalysis> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => RunAsync(request, cancellationToken), cancellationToken);

    public Task<TokenizeResult> TokenizeAsync(TokenizeRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Tokenizer.Run(request), cancellationToken);

    private Task<LanguageAnalysis> RunAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        try
        {
            MsBuildBootstrap.EnsureRegistered();
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(RegistrationFailure(ex.Message));
        }

        return Execute(request, assetsRoot ?? request.SnapshotRoot, cancellationToken);
    }

    private static Task<LanguageAnalysis> Execute(AnalysisRequest request, string assetsRoot, CancellationToken cancellationToken) =>
        new AnalysisRun(request, assetsRoot).ExecuteAsync(cancellationToken);

    private static LanguageAnalysis RegistrationFailure(string message) =>
        new(Languages.CSharp, new Dictionary<string, string>(), [], ModuleGraphFacts.Empty, [], [],
            [new Reason(ReasonCodes.ProjectLoadFailed, $"MSBuild could not be located: {message}")]);
}
