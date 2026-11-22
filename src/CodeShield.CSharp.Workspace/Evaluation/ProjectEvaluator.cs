using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace CodeShield.CSharp.Evaluation;

/// <summary>Evaluates projects (Debug configuration, one context per target framework) without restore or targets.</summary>
internal sealed class ProjectEvaluator : IDisposable
{
    private readonly ProjectCollection _collection = new();
    private readonly Dictionary<string, ProjectEvaluation> _evaluated = new(StringComparer.OrdinalIgnoreCase);

    public ProjectEvaluation Evaluate(string fullPath)
    {
        if (!_evaluated.TryGetValue(fullPath, out var evaluation))
        {
            evaluation = EvaluateUncached(fullPath);
            _evaluated[fullPath] = evaluation;
        }

        return evaluation;
    }

    private ProjectEvaluation EvaluateUncached(string fullPath)
    {
        try
        {
            var frameworks = DeclaredFrameworks(fullPath);
            return new ProjectEvaluation(fullPath, [.. frameworks.Select(framework => EvaluateFramework(fullPath, framework))]);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return new ProjectEvaluation(fullPath, [new ContextEvaluation(null, null, ex.Message)]);
        }
    }

    public void Dispose()
    {
        _collection.UnloadAllProjects();
        _collection.Dispose();
    }

    private ContextEvaluation EvaluateFramework(string fullPath, string? framework)
    {
        try
        {
            var project = Load(fullPath, framework);
            return new ContextEvaluation(framework, EvaluatedProjectReader.Read(project, framework), null);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            return new ContextEvaluation(framework, null, ex.Message);
        }
    }

    private List<string?> DeclaredFrameworks(string fullPath)
    {
        var outer = Load(fullPath, null);
        var single = outer.GetPropertyValue("TargetFramework");
        if (single.Length > 0)
        {
            return [single];
        }

        var multiple = outer.GetPropertyValue("TargetFrameworks")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return multiple.Length > 0 ? [.. multiple.Distinct()] : [null];
    }

    private Project Load(string fullPath, string? framework)
    {
        var globals = new Dictionary<string, string> { ["Configuration"] = "Debug" };
        if (framework is not null)
        {
            globals["TargetFramework"] = framework;
        }

        return Project.FromFile(fullPath, new ProjectOptions { ProjectCollection = _collection, GlobalProperties = globals });
    }

    private static bool IsLoadFailure(Exception ex) =>
        ex is InvalidProjectFileException or IOException or InvalidOperationException or UnauthorizedAccessException
            or ArgumentException or System.Xml.XmlException;
}
