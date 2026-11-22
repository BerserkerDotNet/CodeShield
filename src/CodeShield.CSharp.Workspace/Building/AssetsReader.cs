using System.Text.Json;

namespace CodeShield.CSharp.Building;

/// <summary>Reads compile assets of NuGet packages from an existing obj/project.assets.json.</summary>
internal static class AssetsReader
{
    public static ReferenceSet Read(byte[] assetsJson, string? framework)
    {
        try
        {
            using var document = JsonDocument.Parse(assetsJson);
            return ReadCompileAssets(document.RootElement, framework);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ReferenceSet.OfMissing($"NuGet assets file is unreadable: {ex.Message}");
        }
    }

    private static ReferenceSet ReadCompileAssets(JsonElement root, string? framework)
    {
        var target = FindTarget(root.GetProperty("targets"), framework);
        if (target is null)
        {
            return ReferenceSet.OfMissing($"NuGet assets file has no target for {framework}.");
        }

        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();
        var libraries = root.GetProperty("libraries");
        var resolved = CompileAssets(target.Value, libraries)
            .Select(asset => (asset.Package, Path: folders.Select(folder => Path.Combine(folder, asset.Relative)).FirstOrDefault(File.Exists)))
            .ToList();
        return new ReferenceSet(
            [.. resolved.Where(item => item.Path is not null).Select(item => item.Path!)],
            [.. resolved.Where(item => item.Path is null).Select(item => $"NuGet package asset for {item.Package} was not found in any package folder.")]);
    }

    private static JsonElement? FindTarget(JsonElement targets, string? framework)
    {
        var entries = targets.EnumerateObject().ToList();
        var match = entries.FirstOrDefault(entry => string.Equals(entry.Name, framework, StringComparison.OrdinalIgnoreCase));
        match = match.Value.ValueKind == JsonValueKind.Undefined
            ? entries.FirstOrDefault(entry => entry.Name.StartsWith(framework + "/", StringComparison.OrdinalIgnoreCase))
            : match;
        return match.Value.ValueKind == JsonValueKind.Undefined ? null : match.Value;
    }

    private static IEnumerable<(string Package, string Relative)> CompileAssets(JsonElement target, JsonElement libraries) =>
        target.EnumerateObject()
            .Where(package => package.Value.TryGetProperty("compile", out _) && IsPackage(libraries, package.Name))
            .SelectMany(package => package.Value.GetProperty("compile").EnumerateObject()
                .Where(asset => !asset.Name.EndsWith("_._", StringComparison.Ordinal))
                .Select(asset => (package.Name, Path.Combine(libraries.GetProperty(package.Name).GetProperty("path").GetString()!, asset.Name))));

    private static bool IsPackage(JsonElement libraries, string name) =>
        libraries.TryGetProperty(name, out var library)
        && library.TryGetProperty("type", out var type)
        && type.GetString() == "package";
}
