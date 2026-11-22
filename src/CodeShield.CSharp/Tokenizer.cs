using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeShield.CSharp;

/// <summary>Produces Roslyn token streams (without trivia) for duplication detection.</summary>
internal static class Tokenizer
{
    public static TokenizeResult Run(TokenizeRequest request)
    {
        var files = new List<FileTokens>();
        var failures = new List<Reason>();
        foreach (var file in request.Files)
        {
            var tokens = TryTokenize(Path.Combine(request.SnapshotRoot, file));
            if (tokens is null)
            {
                failures.Add(new Reason(ReasonCodes.UnreadableSource, $"Could not read {file}."));
            }
            else
            {
                files.Add(new FileTokens(file, tokens));
            }
        }

        return new TokenizeResult(files, failures);
    }

    private static List<Token>? TryTokenize(string fullPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            var tree = CSharpSyntaxTree.ParseText(Microsoft.CodeAnalysis.Text.SourceText.From(bytes, bytes.Length), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
            return [.. tree.GetRoot().DescendantTokens().Where(token => !token.IsMissing && token.Span.Length > 0).Select(ToToken)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Token ToToken(SyntaxToken token)
    {
        var span = token.GetLocation().GetLineSpan();
        return new Token(
            token.Kind().ToString(),
            token.Text,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            Math.Max(1, span.EndLinePosition.Character),
            token.SpanStart);
    }
}
