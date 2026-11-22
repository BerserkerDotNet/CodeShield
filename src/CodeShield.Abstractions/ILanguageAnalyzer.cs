namespace CodeShield.Abstractions;

/// <summary>Decides which repository paths a language adapter owns.</summary>
public interface ISourceClassifier
{
    string Language { get; }

    /// <summary>True for supported source files (scored).</summary>
    bool IsSource(string repoRelativePath);

    /// <summary>True for project or tool configuration files that influence analysis.</summary>
    bool IsConfiguration(string repoRelativePath);
}

/// <summary>Produces the exact-token streams used for duplication detection.</summary>
public interface ITokenizer
{
    Task<TokenizeResult> TokenizeAsync(TokenizeRequest request, CancellationToken cancellationToken);
}

/// <summary>Language adapter. Implementations are deterministic and never modify the repository.</summary>
public interface ILanguageAnalyzer : ISourceClassifier, ITokenizer
{
    Task<LanguageAnalysis> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken);
}
