using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Cohesion;

/// <summary>Builds ClassFacts and TypeFacts from a compilation's owned sources.</summary>
internal static class TypeFacts
{
    private static readonly TypeKind[] ClassKinds = [TypeKind.Class];
    private static readonly TypeKind[] AllKinds = [TypeKind.Class, TypeKind.Struct, TypeKind.Interface];

    public static IReadOnlyList<ClassFact> Classes(IEnumerable<SyntaxTree> focusTrees, IReadOnlyDictionary<SyntaxTree, string> repoPaths, SemanticModels models) =>
        [.. Symbols(focusTrees, models, ClassKinds)
            .Where(type => !type.IsStatic)
            .Select(type => CohesionAnalyzer.Analyze(type, repoPaths, models))
            .OfType<ClassFact>()
            .OrderBy(fact => fact.UnitId, StringComparer.Ordinal)];

    public static IReadOnlyList<TypeFact> Types(IEnumerable<SyntaxTree> ownedTrees, IReadOnlyDictionary<SyntaxTree, string> repoPaths, SemanticModels models) =>
        [.. Symbols(ownedTrees, models, AllKinds)
            .Select(type => (Type: type, Declarations: TypeDeclarations.Of(type, repoPaths)))
            .Where(item => item.Declarations.Count > 0)
            .Select(item => new TypeFact(TypeDeclarations.UnitId(item.Declarations[0]), item.Declarations[0].File, IsAbstract(item.Type)))
            .OrderBy(fact => fact.UnitId, StringComparer.Ordinal)];

    private static bool IsAbstract(INamedTypeSymbol type) => type.TypeKind == TypeKind.Interface || (type.IsAbstract && !type.IsStatic);

    private static IEnumerable<INamedTypeSymbol> Symbols(IEnumerable<SyntaxTree> trees, SemanticModels models, TypeKind[] kinds) =>
        trees.SelectMany(tree => TypeDeclarations.Symbols(tree, models))
            .Where(type => kinds.Contains(type.TypeKind))
            .Distinct(SymbolEqualityComparer.Default)
            .OfType<INamedTypeSymbol>();
}
