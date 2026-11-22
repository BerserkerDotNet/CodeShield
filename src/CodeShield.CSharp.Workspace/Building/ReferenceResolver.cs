using CodeShield.CSharp.Evaluation;

namespace CodeShield.CSharp.Building;

/// <summary>Resolves the reference assembly files a project compiles against: targeting packs and NuGet compile assets.</summary>
internal sealed class ReferenceResolver(AnalysisEnvironment environment)
{
    public ReferenceSet Resolve(EvaluatedProject project) =>
        TargetingPackLocator.Locate(project).Merge(PackageAssets(project));

    private ReferenceSet PackageAssets(EvaluatedProject project)
    {
        if (project.Items.PackageReferences.Count == 0)
        {
            return ReferenceSet.Empty;
        }

        var file = AssetsFile(project);
        return File.Exists(file)
            ? AssetsReader.Read(environment.ReadInput(file), project.Framework)
            : ReferenceSet.OfMissing($"NuGet assets file not found for {Path.GetFileName(project.FullPath)}; restore has not been run.");
    }

    /// <summary>The assets file under the snapshot root is re-rooted onto the asset root (the workspace for a baseline).</summary>
    private string AssetsFile(EvaluatedProject project)
    {
        var declared = project.Property("ProjectAssetsFile");
        var file = declared.Length > 0
            ? declared
            : Path.Combine(Path.GetDirectoryName(project.FullPath)!, "obj", "project.assets.json");
        var relative = Path.GetRelativePath(environment.Paths.Root, file);
        var outside = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative);
        return outside ? file : Path.Combine(environment.AssetsRoot, relative);
    }
}
