using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeShield.CSharp.Correctness;

/// <summary>The pinned analyzers that could be loaded, or the reason they could not.</summary>
internal sealed record AnalyzerSet(ImmutableArray<DiagnosticAnalyzer> Analyzers, string? Error)
{
    public static AnalyzerSet Failed(string error) => new([], error);
}

/// <summary>Loads analyzer assemblies from bytes that were hashed through the input ledger.</summary>
internal sealed class LedgerAssemblyLoader(IReadOnlyDictionary<string, byte[]> contents) : IAnalyzerAssemblyLoader
{
    private static readonly ConcurrentDictionary<string, Assembly> Loaded = new(StringComparer.OrdinalIgnoreCase);

    public Assembly LoadFromPath(string fullPath) =>
        Loaded.GetOrAdd(fullPath, path => AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(contents[path])));

    public void AddDependencyLocation(string fullPath)
    {
        // Dependencies are preloaded by AnalyzerSetFactory.
    }
}

/// <summary>Where the pinned correctness analyzers are loaded from; shared by evaluation and installation diagnostics.</summary>
public static class AnalyzerLocation
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "analyzers");
}

internal static class AnalyzerSetFactory
{
    public static string AnalyzerDirectory => AnalyzerLocation.Directory;

    public static AnalyzerSet Load(AnalysisEnvironment environment)
    {
        try
        {
            var files = Directory.Exists(AnalyzerDirectory)
                ? Directory.GetFiles(AnalyzerDirectory, "*.dll").OrderBy(path => path.Length).ThenBy(path => path, StringComparer.Ordinal).ToList()
                : [];
            return files.Count == 0 ? AnalyzerSet.Failed("Analyzer folder is missing or empty.") : LoadFiles(files, environment);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or FileLoadException)
        {
            return AnalyzerSet.Failed(ex.Message);
        }
    }

    private static AnalyzerSet LoadFiles(List<string> files, AnalysisEnvironment environment)
    {
        var contents = files.ToDictionary(path => path, environment.ReadInput, StringComparer.OrdinalIgnoreCase);
        var loader = new LedgerAssemblyLoader(contents);
        var failures = new List<string>();
        var analyzers = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        foreach (var file in files)
        {
            var reference = new AnalyzerFileReference(file, loader);
            reference.AnalyzerLoadFailed += (_, args) => failures.Add(args.Message);
            analyzers.AddRange(reference.GetAnalyzers(LanguageNames.CSharp));
        }

        var selected = analyzers.Where(analyzer => ExposesSelectedRule(analyzer)).ToImmutableArray();
        return failures.Count > 0 || selected.IsEmpty
            ? AnalyzerSet.Failed(failures.FirstOrDefault() ?? "No analyzer exposes the selected rules.")
            : new AnalyzerSet(selected, null);
    }

    public static IReadOnlySet<string> SelectedIds { get; } =
        CorrectnessCatalog.Pinned.ForLanguage(Languages.CSharp).Select(rule => rule.AnalyzerId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool ExposesSelectedRule(DiagnosticAnalyzer analyzer) =>
        analyzer.SupportedDiagnostics.Any(descriptor => SelectedIds.Contains(descriptor.Id));
}


