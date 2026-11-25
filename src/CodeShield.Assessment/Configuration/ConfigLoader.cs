using CodeShield.Abstractions;
using CodeShield.Core.Rulesets;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Configuration;

/// <summary>The validated configuration files of a workspace, their cascading policy and the inputs they contribute to the snapshot.</summary>
public sealed record LoadedConfiguration(RulePolicy Policy, IReadOnlyList<FolderConfig> Files, IReadOnlyList<InputRecord> Inputs)
{
    public static LoadedConfiguration None { get; } = new(RulePolicy.Defaults, [], []);
}

/// <summary>Finds <c>.codeshield.json</c> files in the current inventory, hashes and parses the same bytes, and validates them once.</summary>
public static class ConfigLoader
{
    public static bool IsConfigPath(string path) =>
        SupportedFiles.IsCodeShieldConfig(path);

    public static LoadedConfiguration Load(string root, IEnumerable<string> currentPaths, Ruleset ruleset)
    {
        var files = new List<FolderConfig>();
        var inputs = new List<InputRecord>();
        foreach (var path in currentPaths.Where(IsConfigPath).Order(StringComparer.Ordinal))
        {
            var bytes = ReadBytes(root, path);
            if (bytes is null)
            {
                continue;
            }

            var sha = Hashing.Sha256Hex(bytes);
            files.Add(new ConfigReader(path, ruleset).Read(bytes, sha));
            inputs.Add(new InputRecord(InputScopes.Repository, path, sha));
        }

        return files.Count == 0 ? LoadedConfiguration.None : new LoadedConfiguration(new RulePolicy(files), files, inputs);
    }

    private static byte[]? ReadBytes(string root, string path)
    {
        try
        {
            return File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"{path}: cannot be read ({exception.Message})");
        }
    }
}
