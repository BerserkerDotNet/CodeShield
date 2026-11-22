using System.Runtime.InteropServices;
using CodeShield.CSharp.Evaluation;

namespace CodeShield.CSharp.Building;

/// <summary>Finds reference assemblies in the locally installed targeting packs; never downloads anything.</summary>
internal static class TargetingPackLocator
{
    public static ReferenceSet Locate(EvaluatedProject project)
    {
        var folder = RefFolderName(project);
        if (folder is null)
        {
            return ReferenceSet.Empty;
        }

        var root = PacksRoot(project);
        if (root is null)
        {
            return ReferenceSet.OfMissing("The dotnet 'packs' directory could not be located.");
        }

        return Requirements(project, folder)
            .Select(requirement => LocatePack(root, requirement, folder))
            .Aggregate(ReferenceSet.Empty, (all, next) => all.Merge(next));
    }

    private static string? RefFolderName(EvaluatedProject project)
    {
        var version = project.Property("TargetFrameworkVersion").TrimStart('v');
        return project.Property("TargetFrameworkIdentifier") switch
        {
            ".NETCoreApp" => (Version.TryParse(version, out var parsed) && parsed.Major < 5 ? "netcoreapp" : "net") + version,
            ".NETStandard" when version == "2.1" => "netstandard2.1",
            _ => null,
        };
    }

    private static List<PackRequirement> Requirements(EvaluatedProject project, string folder)
    {
        if (folder.StartsWith("netstandard", StringComparison.Ordinal))
        {
            return [new PackRequirement("NETStandard.Library.Ref", null)];
        }

        return project.Items.TargetingPacks.Count > 0
            ? [.. project.Items.TargetingPacks]
            : [new PackRequirement("Microsoft.NETCore.App.Ref", null)];
    }

    private static ReferenceSet LocatePack(string root, PackRequirement requirement, string folder)
    {
        var packDirectory = Path.Combine(root, requirement.PackName);
        var version = Directory.Exists(packDirectory) ? PickVersion(packDirectory, requirement.Version, folder) : null;
        var refDirectory = version is null ? null : Path.Combine(packDirectory, version, "ref", folder);
        if (refDirectory is null || !Directory.Exists(refDirectory))
        {
            return ReferenceSet.OfMissing($"Targeting pack {requirement.PackName} for {folder} is not installed.");
        }

        var files = Directory.GetFiles(refDirectory, "*.dll").OrderBy(path => path, StringComparer.Ordinal);
        return new ReferenceSet([.. files], []);
    }

    private static string? PickVersion(string packDirectory, string? preferred, string folder)
    {
        if (preferred is not null && Directory.Exists(Path.Combine(packDirectory, preferred, "ref", folder)))
        {
            return preferred;
        }

        var major = new string([.. folder.SkipWhile(character => !char.IsDigit(character)).TakeWhile(char.IsDigit)]);
        return Directory.GetDirectories(packDirectory)
            .Select(directory => Path.GetFileName(directory))
            .Where(name => name.StartsWith(major + ".", StringComparison.Ordinal))
            .OrderByDescending(name => !name.Contains('-'))
            .ThenByDescending(name => Version.TryParse(name.Split('-')[0], out var parsed) ? parsed : new Version(0, 0))
            .FirstOrDefault();
    }

    private static string? PacksRoot(EvaluatedProject project)
    {
        var candidates = new[]
        {
            project.Property("NetCoreTargetingPackRoot"),
            Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "", "packs"),
            Path.Combine(Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")), "packs"),
        };
        return candidates.FirstOrDefault(candidate => candidate.Length > 0 && Directory.Exists(candidate));
    }
}
