using CodeShield.CSharp.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Cohesion;

/// <summary>An eligible LCOM4 node: a concrete instance method or property accessor with a body.</summary>
internal sealed record CohesionMember(string Id, string Name, IMethodSymbol Symbol, SyntaxNode Syntax);

/// <summary>Finds the LCOM4 nodes and the instance state of a type.</summary>
internal static class CohesionMembers
{
    public static IReadOnlyList<CohesionMember> Nodes(INamedTypeSymbol type, SemanticModels models) =>
        [.. type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(declaration => declaration.Members.SelectMany(member => NodesOf(member, models)))];

    public static HashSet<ISymbol> State(INamedTypeSymbol type)
    {
        var state = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var member in type.GetMembers().Where(IsInstanceState))
        {
            state.Add(member);
        }

        return state;
    }

    private static IEnumerable<CohesionMember> NodesOf(MemberDeclarationSyntax member, SemanticModels models)
    {
        var model = models.For(member.SyntaxTree);
        if (member.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return [];
        }

        return member switch
        {
            MethodDeclarationSyntax method => MethodNode(method, model),
            BasePropertyDeclarationSyntax property => PropertyNodes(property, model),
            _ => [],
        };
    }

    private static IEnumerable<CohesionMember> MethodNode(MethodDeclarationSyntax method, SemanticModel model) =>
        IsNonEmpty(method.Body, method.ExpressionBody) && model.GetDeclaredSymbol(method) is { } symbol
            ? [new CohesionMember(UnitIds.LocalName(method), method.Identifier.Text, symbol, method)]
            : [];

    private static IEnumerable<CohesionMember> PropertyNodes(BasePropertyDeclarationSyntax property, SemanticModel model)
    {
        var symbol = model.GetDeclaredSymbol(property) as IPropertySymbol;
        var expression = (property as PropertyDeclarationSyntax)?.ExpressionBody ?? (property as IndexerDeclarationSyntax)?.ExpressionBody;
        if (expression is not null)
        {
            return symbol?.GetMethod is { } getter ? [Member(UnitIds.LocalName(property), getter, property)] : [];
        }

        return (property.AccessorList?.Accessors ?? [])
            .Where(accessor => IsNonEmpty(accessor.Body, accessor.ExpressionBody))
            .Select(accessor => (Accessor: accessor, Symbol: model.GetDeclaredSymbol(accessor)))
            .Where(item => item.Symbol is not null)
            .Select(item => Member(AccessorId(property, item.Accessor), item.Symbol!, item.Accessor));
    }

    private static CohesionMember Member(string id, IMethodSymbol symbol, SyntaxNode syntax) =>
        new(id, id.Split('(')[0], symbol, syntax);

    private static string AccessorId(BasePropertyDeclarationSyntax property, AccessorDeclarationSyntax accessor) =>
        UnitIds.LocalName(property) + "." + UnitIds.LocalName(accessor);

    private static bool IsNonEmpty(BlockSyntax? body, ArrowExpressionClauseSyntax? expression) =>
        expression is not null || body is { Statements.Count: > 0 };

    private static bool IsInstanceState(ISymbol member) => member switch
    {
        IFieldSymbol field => IsPlainInstance(field) && !field.IsConst && !field.IsImplicitlyDeclared,
        IPropertySymbol property => IsPlainInstance(property) && IsAutoProperty(property),
        _ => false,
    };

    private static bool IsPlainInstance(ISymbol member) => !member.IsStatic && member is not IPropertySymbol { IsIndexer: true };

    private static bool IsAutoProperty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax()).Any(IsAutoDeclaration);

    private static bool IsAutoDeclaration(SyntaxNode syntax) => syntax switch
    {
        ParameterSyntax => true,
        PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } list } =>
            list.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null),
        _ => false,
    };
}
