using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeShield.CSharp.Building;

/// <summary>A parsed compile item. <c>RepoPath</c> is null for files outside the snapshot inventory.</summary>
internal sealed record SourceFile(string? RepoPath, string FullPath, SyntaxTree Tree, bool Generated);

internal sealed record SourceLoad(IReadOnlyList<SourceFile> Files, IReadOnlyList<string> Unreadable);

/// <summary>Reads compile items through the input ledger and parses the bytes that were hashed.</summary>
internal sealed class SourceLoader(AnalysisEnvironment environment)
{
    public SourceLoad Load(IEnumerable<string> fullPaths, CSharpParseOptions options)
    {
        var results = fullPaths.Select(path => (Path: path, File: TryLoad(path, options))).ToList();
        return new SourceLoad(
            [.. results.Where(result => result.File is not null).Select(result => result.File!)],
            [.. results.Where(result => result.File is null).Select(result => result.Path)]);
    }

    private SourceFile? TryLoad(string fullPath, CSharpParseOptions options)
    {
        try
        {
            var bytes = environment.ReadInput(fullPath);
            var text = SourceText.From(bytes, bytes.Length);
            var tree = CSharpSyntaxTree.ParseText(text, options, fullPath);
            var repoPath = environment.Paths.ToRepoPath(fullPath);
            return new SourceFile(repoPath, fullPath, tree, GeneratedFiles.IsGenerated(repoPath ?? fullPath, text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
