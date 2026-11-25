using CodeShield.CSharp.Correctness;
using CodeShield.TypeScript;

namespace CodeShield.Mcp;

internal sealed record Check(string Name, bool Passed, string Detail);

/// <summary>Verifies the installed payload and prerequisites using the same locations the evaluator resolves at runtime.</summary>
internal static class InstallationChecks
{
    private static readonly string[] WorkerDependencies = ["eslint", "typescript", "typescript-eslint"];
    private static readonly string[] PackagedAssets = ["release-manifest.json", "schemas", "agent-instructions"];

    public static async Task<IReadOnlyList<Check>> RunAsync(CancellationToken cancellationToken)
    {
        var location = WorkerLocation.Discover();
        return
        [
            Analyzers(),
            Assets(),
            Worker(location),
            await GitAsync(cancellationToken).ConfigureAwait(false),
            await NodeAsync(location, cancellationToken).ConfigureAwait(false),
        ];
    }

    private static Check Analyzers()
    {
        var count = Directory.Exists(AnalyzerLocation.Directory) ? Directory.GetFiles(AnalyzerLocation.Directory, "*.dll").Length : 0;
        return count > 0
            ? new Check("C# analyzers", true, $"{count} analyzer assemblies in {AnalyzerLocation.Directory}")
            : new Check("C# analyzers", false, $"No analyzer assemblies in {AnalyzerLocation.Directory}. Reinstall the CodeShield tool.");
    }

    private static Check Assets()
    {
        var missing = PackagedAssets.Where(name => !File.Exists(Path.Combine(AppContext.BaseDirectory, name)) && !Directory.Exists(Path.Combine(AppContext.BaseDirectory, name))).ToList();
        return missing.Count == 0
            ? new Check("Packaged assets", true, "Schemas, release manifest and agent guidance are present.")
            : new Check("Packaged assets", false, $"Missing {string.Join(", ", missing)}. Reinstall the CodeShield tool.");
    }

    private static Check Worker(WorkerLocation location)
    {
        if (location.WorkerScript is null || !File.Exists(location.WorkerScript))
        {
            return new Check("TypeScript worker", false, $"Worker script not found ({location.WorkerScript ?? "no location"}). Reinstall the CodeShield tool.");
        }

        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(location.WorkerScript)!, ".."));
        var missing = WorkerDependencies.Where(name => !File.Exists(Path.Combine(root, "node_modules", name, "package.json"))).ToList();
        return missing.Count == 0
            ? new Check("TypeScript worker", true, location.WorkerScript)
            : new Check("TypeScript worker", false, $"Worker dependencies missing from {root}: {string.Join(", ", missing)}. Reinstall the CodeShield tool.");
    }

    private static async Task<Check> GitAsync(CancellationToken cancellationToken)
    {
        var version = await ToolProbe.RunAsync("git", "--version", cancellationToken).ConfigureAwait(false);
        return version is null
            ? new Check("Git", false, "git was not found on PATH. Install Git.")
            : new Check("Git", true, version);
    }

    private static async Task<Check> NodeAsync(WorkerLocation location, CancellationToken cancellationToken)
    {
        var output = await ToolProbe.RunAsync(location.NodeExecutable, "--version", cancellationToken).ConfigureAwait(false);
        var version = output is null ? null : NodeRequirement.Parse(output);
        if (version is null)
        {
            return new Check("Node.js", false, $"'{location.NodeExecutable}' could not be run. Install {NodeRequirement.Description} to analyze TypeScript; C# analysis does not need it.");
        }

        return NodeRequirement.IsSupported(version)
            ? new Check("Node.js", true, output!)
            : new Check("Node.js", false, $"{output} is not supported. Install {NodeRequirement.Description}.");
    }
}
