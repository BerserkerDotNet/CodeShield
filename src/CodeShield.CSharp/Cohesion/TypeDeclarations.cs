using CodeShield.Abstractions;
using CodeShield.CSharp.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Cohesion;

/// <summary>A declaration of a type in an owned repository file.</summary>
internal sealed record TypeDeclaration(TypeDeclarationSyntax Syntax, string File);

/// <summary>Caches one semantic model per syntax tree of a compilation.</summary>
internal sealed class SemanticModels(Compilation compilation)
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    public SemanticModel For(SyntaxTree tree)
    {
        if (!_models.TryGetValue(tree, out var model))
        {
            model = compilation.GetSemanticModel(tree);
            _models[tree] = model;
        }

        return model;
    }
}

/// <summary>Resolves the declarations of a (possibly partial) type symbol to repository files.</summary>
internal static class TypeDeclarations
{
    public static IReadOnlyList<TypeDeclaration> Of(INamedTypeSymbol type, IReadOnlyDictionary<SyntaxTree, string> repoPaths) =>
        [.. type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Where(syntax => repoPaths.ContainsKey(syntax.SyntaxTree))
            .Select(syntax => new TypeDeclaration(syntax, repoPaths[syntax.SyntaxTree]))
            .OrderBy(declaration => declaration.File, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.Syntax.SpanStart)];

    public static string UnitId(TypeDeclaration primary) => UnitIds.For(primary.Syntax, primary.File);

    public static IEnumerable<INamedTypeSymbol> Symbols(SyntaxTree tree, SemanticModels models) =>
        tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Select(syntax => models.For(tree).GetDeclaredSymbol(syntax))
            .OfType<INamedTypeSymbol>();
}
