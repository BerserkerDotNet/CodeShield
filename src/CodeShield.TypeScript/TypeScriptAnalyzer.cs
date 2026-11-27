using CodeShield.Abstractions;
using CodeShield.Workers;

namespace CodeShield.TypeScript;

/// <summary>TypeScript adapter: delegates analysis and tokenization to the bundled Node.js worker.</summary>
public sealed class TypeScriptAnalyzer(WorkerLocation location, ICorrectnessSelection correctness) : WorkerLanguageAnalyzer(correctness)
{
    private static readonly string[] SourceExtensions = [".ts", ".tsx", ".mts", ".cts"];

    public TypeScriptAnalyzer()
        : this(WorkerLocation.Discover(), CorrectnessCatalog.Pinned)
    {
    }

    public override string Language => Languages.TypeScript;

    public override bool IsSource(string repoRelativePath) =>
        !InNodeModules(repoRelativePath)
        && SourceExtensions.Any(extension => repoRelativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        && !repoRelativePath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

    public override bool IsConfiguration(string repoRelativePath)
    {
        var name = repoRelativePath[(repoRelativePath.LastIndexOf('/') + 1)..];
        var isConfig = name == "package.json"
            || (name.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        return isConfig && !InNodeModules(repoRelativePath);
    }

    protected override (WorkerCommand? Command, Reason? Failure) ResolveCommand()
    {
        if (location.WorkerScript is null || !File.Exists(location.WorkerScript))
        {
            return (null, WorkerCall.Missing("The TypeScript worker (worker/dist/main.js) was not found; run scripts/setup."));
        }

        return (new WorkerCommand("TypeScript worker", "Node.js", location.NodeExecutable, [location.WorkerScript]), null);
    }

    private static bool InNodeModules(string path) =>
        path.StartsWith("node_modules/", StringComparison.Ordinal) || path.Contains("/node_modules/", StringComparison.Ordinal);
}



