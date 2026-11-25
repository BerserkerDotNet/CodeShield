using CodeShield.Core.Contracts;
using CodeShield.Core.Git;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Configuration;

/// <summary>Builds the response's <c>configuration</c> block; null when no configuration file exists or changed.</summary>
public static class ConfigDisclosureBuilder
{
    public static ConfigurationDisclosure? Build(
        LoadedConfiguration loaded, ChangeMap map, IReadOnlyDictionary<string, int>? excludedFindings)
    {
        var files = Files(loaded, map);
        if (files.Count == 0)
        {
            return null;
        }

        return new ConfigurationDisclosure(
            files,
            files.Any(file => file.Change != ConfigChange.Unchanged),
            [.. loaded.Files.Where(config => config.Exclude == true).Select(Exclusion)],
            [.. loaded.Files.SelectMany(Overrides)],
            [.. (excludedFindings ?? new Dictionary<string, int>()).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ExcludedFindingsDto(pair.Key, pair.Value))]);
    }

    private static List<ConfigFileDto> Files(LoadedConfiguration loaded, ChangeMap map)
    {
        var present = loaded.Files.Select(config => new ConfigFileDto(
            ResponseNative(config.Path), config.Sha256, ChangeOf(map, config.Path)));
        var deleted = RemovedPaths(map).Select(path => new ConfigFileDto(ResponseNative(path), null, nameof(ChangeKind.Deleted).ToLowerInvariant()));
        return [.. present.Concat(deleted).OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    // A config disappears from its path by deletion or by being renamed (to another config or to a non-config name).
    private static IEnumerable<string> RemovedPaths(ChangeMap map) =>
        map.Changes.Select(change => change.Kind == ChangeKind.Deleted ? change.Path : change.OldPath)
            .OfType<string>().Where(ConfigLoader.IsConfigPath).Distinct(StringComparer.Ordinal);

    private static string ChangeOf(ChangeMap map, string path)
    {
        var change = map.Changes.FirstOrDefault(candidate => candidate.Path == path);
        return change is null ? ConfigChange.Unchanged : change.Kind.ToString().ToLowerInvariant();
    }

    private static ConfigExclusionDto Exclusion(FolderConfig config) =>
        new(Folder(config), config.Reason!, ResponseNative(config.Path));

    private static IEnumerable<ConfigOverrideDto> Overrides(FolderConfig config) =>
        config.Rules.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ConfigOverrideDto(
            Folder(config), pair.Key, pair.Value.Enabled, pair.Value.Limit, pair.Value.Reason, ResponseNative(config.Path)));

    private static string Folder(FolderConfig config) =>
        config.Directory.Length == 0 ? "." : ResponseNative(config.Directory);

    private static string ResponseNative(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
