using System.Collections.Immutable;
using CodeShield.CSharp.Evaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeShield.CSharp.Building;

/// <summary>A project's compilation plus the owned sources and the dependencies that could not be resolved.</summary>
internal sealed record ProjectCompilation(
    CSharpCompilation Compilation,
    IReadOnlyList<SourceFile> Sources,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<MetadataReference> Linked,
    MetadataReference Reference);

/// <summary>Builds compilations from evaluated projects, recursing through ProjectReference items.</summary>
internal sealed class CompilationFactory
{
    private readonly AnalysisEnvironment _environment;
    private readonly SourceLoader _loader;
    private readonly ReferenceResolver _resolver;
    private readonly Dictionary<string, ProjectCompilation> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CompilationFactory(AnalysisEnvironment environment)
    {
        _environment = environment;
        _loader = new SourceLoader(environment);
        _resolver = new ReferenceResolver(environment);
    }

    public ProjectCompilation Create(EvaluatedProject project) => Build(project, []);

    private ProjectCompilation Build(EvaluatedProject project, ImmutableHashSet<string> ancestry)
    {
        var key = KeyOf(project);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var built = BuildUncached(project, ancestry.Add(key));
        _cache[key] = built;
        return built;
    }

    private ProjectCompilation BuildUncached(EvaluatedProject project, ImmutableHashSet<string> ancestry)
    {
        var parseOptions = ProjectOptionsFactory.Parse(project);
        var load = _loader.Load(project.Items.Compile, parseOptions);
        var references = _resolver.Resolve(project);
        var linked = ReferencedProjects(project, ancestry);
        var trees = load.Files.Select(file => file.Tree).Concat(ImplicitUsingsSource.Create(project, parseOptions));
        var metadata = references.Assemblies.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReferenceCache.Load(path, _environment.Inputs))
            .Concat(linked.References);
        var name = project.Property("AssemblyName") is { Length: > 0 } assembly ? assembly : Path.GetFileNameWithoutExtension(project.FullPath);
        var compilation = CSharpCompilation.Create(name, trees, metadata, ProjectOptionsFactory.Compilation(project));
        return new ProjectCompilation(
            compilation, load.Files, [.. references.Missing, .. linked.Missing], load.Unreadable, linked.References, compilation.ToMetadataReference());
    }

    private (List<MetadataReference> References, List<string> Missing) ReferencedProjects(
        EvaluatedProject project, ImmutableHashSet<string> ancestry)
    {
        var results = project.Items.ProjectReferences.Select(path => ReferencedProject(path, project, ancestry)).ToList();
        return (
            [.. results.SelectMany(result => result.References).Distinct()],
            [.. results.SelectMany(result => result.Missing)]);
    }

    private (List<MetadataReference> References, List<string> Missing) ReferencedProject(
        string path, EvaluatedProject owner, ImmutableHashSet<string> ancestry)
    {
        var target = PickFramework(_environment.Evaluator.Evaluate(path), owner.Framework);
        if (target is null)
        {
            return ([], [$"ProjectReference {Path.GetFileName(path)} could not be evaluated."]);
        }

        if (ancestry.Contains(KeyOf(target)))
        {
            return ([], []);
        }

        var built = Build(target, ancestry);
        return ([built.Reference, .. built.Linked], [.. built.Missing]);
    }

    private static EvaluatedProject? PickFramework(ProjectEvaluation evaluation, string? framework)
    {
        var usable = evaluation.Contexts.Where(context => context.Project is not null).Select(context => context.Project!).ToList();
        return usable.FirstOrDefault(candidate => candidate.Framework == framework) ?? usable.FirstOrDefault();
    }

    private static string KeyOf(EvaluatedProject project) => $"{project.FullPath}|{project.Framework}";
}

