using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Metrics;

/// <summary>
/// Stable code-unit identifiers: <c>file#Namespace.Type.Member(paramTypes)</c>. Local functions and lambdas are
/// appended to their parent with "/"; lambdas are numbered by their order within the parent (<c>&lt;lambda@N&gt;</c>).
/// </summary>
internal static class UnitIds
{
    private static readonly Dictionary<SyntaxKind, Func<SyntaxNode, string>> LocalNames = new()
    {
        [SyntaxKind.MethodDeclaration] = node => MethodName((MethodDeclarationSyntax)node),
        [SyntaxKind.ConstructorDeclaration] = node => ConstructorName((ConstructorDeclarationSyntax)node),
        [SyntaxKind.DestructorDeclaration] = node => "~" + ((DestructorDeclarationSyntax)node).Identifier.Text + "()",
        [SyntaxKind.OperatorDeclaration] = node => OperatorName((OperatorDeclarationSyntax)node),
        [SyntaxKind.ConversionOperatorDeclaration] = node => ConversionName((ConversionOperatorDeclarationSyntax)node),
        [SyntaxKind.PropertyDeclaration] = node => PropertyName((PropertyDeclarationSyntax)node),
        [SyntaxKind.IndexerDeclaration] = node => IndexerName((IndexerDeclarationSyntax)node),
        [SyntaxKind.EventDeclaration] = node => ((EventDeclarationSyntax)node).Identifier.Text,
        [SyntaxKind.EventFieldDeclaration] = node => FirstVariable(((EventFieldDeclarationSyntax)node).Declaration),
        [SyntaxKind.FieldDeclaration] = node => FirstVariable(((FieldDeclarationSyntax)node).Declaration),
        [SyntaxKind.ClassDeclaration] = TypeName,
        [SyntaxKind.StructDeclaration] = TypeName,
        [SyntaxKind.InterfaceDeclaration] = TypeName,
        [SyntaxKind.RecordDeclaration] = TypeName,
        [SyntaxKind.RecordStructDeclaration] = TypeName,
        [SyntaxKind.EnumDeclaration] = node => ((EnumDeclarationSyntax)node).Identifier.Text,
        [SyntaxKind.DelegateDeclaration] = node => ((DelegateDeclarationSyntax)node).Identifier.Text,
        [SyntaxKind.GetAccessorDeclaration] = AccessorName,
        [SyntaxKind.SetAccessorDeclaration] = AccessorName,
        [SyntaxKind.InitAccessorDeclaration] = AccessorName,
        [SyntaxKind.AddAccessorDeclaration] = AccessorName,
        [SyntaxKind.RemoveAccessorDeclaration] = AccessorName,
        [SyntaxKind.UnknownAccessorDeclaration] = AccessorName,
        [SyntaxKind.LocalFunctionStatement] = node => LocalFunctionName((LocalFunctionStatementSyntax)node),
        [SyntaxKind.SimpleLambdaExpression] = LambdaName,
        [SyntaxKind.ParenthesizedLambdaExpression] = LambdaName,
        [SyntaxKind.AnonymousMethodExpression] = LambdaName,
    };

    public static string For(SyntaxNode node, string file) => $"{file}#{Qualified(node)}";

    /// <summary>Id of the member-level declaration enclosing <paramref name="node"/> (lambdas and local functions map to their member).</summary>
    public static string ForEnclosingMember(SyntaxNode node, string file)
    {
        var member = node.AncestorsAndSelf().FirstOrDefault(candidate => IsScope(candidate) && !IsNestedFunction(candidate));
        return member is null ? $"{file}#<global>" : For(member, file);
    }

    public static string Qualified(SyntaxNode node)
    {
        var scope = EnclosingScope(node);
        var prefix = scope is null ? NamespaceOf(node) : Qualified(scope);
        var local = LocalNames[node.Kind()](node);
        if (prefix.Length == 0)
        {
            return local;
        }

        return prefix + (IsNestedFunction(node) ? "/" : ".") + local;
    }

    /// <summary>The name of a member without its container, e.g. <c>Run(int)</c> or <c>Value.get</c>.</summary>
    public static string LocalName(SyntaxNode node) => LocalNames[node.Kind()](node);

    public static bool IsScope(SyntaxNode node) => LocalNames.ContainsKey(node.Kind());

    public static bool IsNestedFunction(SyntaxNode node) =>
        node is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    private static SyntaxNode? EnclosingScope(SyntaxNode node) =>
        node.Parent?.AncestorsAndSelf().FirstOrDefault(IsScope);

    private static string NamespaceOf(SyntaxNode node) =>
        string.Join('.', node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => Compact(ns.Name.ToString())));

    private static string MethodName(MethodDeclarationSyntax method) =>
        (method.ExplicitInterfaceSpecifier is { } explicitInterface ? Compact(explicitInterface.Name.ToString()) + "." : "")
        + method.Identifier.Text + TypeParameters(method.TypeParameterList) + Signature(method.ParameterList);

    private static string ConstructorName(ConstructorDeclarationSyntax constructor) =>
        (constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor") + Signature(constructor.ParameterList);

    private static string OperatorName(OperatorDeclarationSyntax declaration) =>
        "operator" + declaration.OperatorToken.Text + Signature(declaration.ParameterList);

    private static string ConversionName(ConversionOperatorDeclarationSyntax declaration) =>
        $"{declaration.ImplicitOrExplicitKeyword.Text} operator {Compact(declaration.Type.ToString())}{Signature(declaration.ParameterList)}";

    private static string PropertyName(PropertyDeclarationSyntax property) =>
        (property.ExplicitInterfaceSpecifier is { } explicitInterface ? Compact(explicitInterface.Name.ToString()) + "." : "")
        + property.Identifier.Text + (property.ExpressionBody is null ? "" : ".get");

    private static string IndexerName(IndexerDeclarationSyntax indexer) =>
        "this[" + string.Join(',', indexer.ParameterList.Parameters.Select(ParameterText)) + "]" + (indexer.ExpressionBody is null ? "" : ".get");

    private static string LocalFunctionName(LocalFunctionStatementSyntax function) =>
        function.Identifier.Text + TypeParameters(function.TypeParameterList) + Signature(function.ParameterList);

    private static string TypeName(SyntaxNode node)
    {
        var declaration = (TypeDeclarationSyntax)node;
        return declaration.Identifier.Text + TypeParameters(declaration.TypeParameterList);
    }

    private static string AccessorName(SyntaxNode node) => ((AccessorDeclarationSyntax)node).Keyword.Text;

    private static string LambdaName(SyntaxNode node) => $"<lambda@{LambdaOrdinal(node)}>";

    private static int LambdaOrdinal(SyntaxNode lambda)
    {
        var scope = EnclosingScope(lambda);
        var container = scope ?? lambda.SyntaxTree.GetRoot();
        var siblings = container.DescendantNodes(candidate => candidate == container || !IsScope(candidate));
        return siblings.OfType<AnonymousFunctionExpressionSyntax>().TakeWhile(candidate => candidate != lambda).Count() + 1;
    }

    private static string FirstVariable(VariableDeclarationSyntax declaration) =>
        declaration.Variables.Count > 0 ? declaration.Variables[0].Identifier.Text : "<field>";

    private static string TypeParameters(TypeParameterListSyntax? list) =>
        list is null ? "" : "<" + string.Join(',', list.Parameters.Select(parameter => parameter.Identifier.Text)) + ">";

    private static string Signature(ParameterListSyntax list) =>
        "(" + string.Join(',', list.Parameters.Select(ParameterText)) + ")";

    private static string ParameterText(ParameterSyntax parameter)
    {
        var modifiers = parameter.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.ScopedKeyword)).Select(modifier => modifier.Text + " ");
        return string.Concat(modifiers) + (parameter.Type is null ? "_" : Compact(parameter.Type.ToString()));
    }

    private static string Compact(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
}
