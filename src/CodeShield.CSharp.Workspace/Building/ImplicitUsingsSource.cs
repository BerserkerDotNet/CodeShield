using CodeShield.CSharp.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeShield.CSharp.Building;

/// <summary>Generates the global-usings source the SDK would produce for ImplicitUsings.</summary>
internal static class ImplicitUsingsSource
{
    public const string Path = "<implicit-usings>.g.cs";

    public static SyntaxTree[] Create(EvaluatedProject project, CSharpParseOptions options)
    {
        var lines = project.Items.Usings.Select(Line).ToList();
        return lines.Count == 0 ? [] : [CSharpSyntaxTree.ParseText(string.Join('\n', lines) + "\n", options, Path)];
    }

    private static string Line(UsingDirective directive)
    {
        if (directive.Alias is not null)
        {
            return $"global using {directive.Alias} = {directive.Namespace};";
        }

        return directive.IsStatic ? $"global using static {directive.Namespace};" : $"global using {directive.Namespace};";
    }
}
