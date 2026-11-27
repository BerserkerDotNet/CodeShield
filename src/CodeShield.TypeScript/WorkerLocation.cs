namespace CodeShield.TypeScript;

/// <summary>Where Node.js and the compiled worker live.</summary>
public sealed record WorkerLocation(string NodeExecutable, string? WorkerScript)
{
    public const string NodeVariable = "CODESHIELD_NODE";
    public const string WorkerVariable = "CODESHIELD_TS_WORKER";

    /// <summary>Resolves from environment overrides, then <c>worker/dist/main.js</c> next to the binaries or any ancestor.</summary>
    public static WorkerLocation Discover()
    {
        var node = Environment.GetEnvironmentVariable(NodeVariable);
        var script = Environment.GetEnvironmentVariable(WorkerVariable) ?? FindScript(AppContext.BaseDirectory);
        return new WorkerLocation(string.IsNullOrWhiteSpace(node) ? "node" : node, script);
    }

    private static string? FindScript(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "worker", "dist", "main.js");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
