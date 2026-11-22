using CodeShield.CSharp.Building;
using CodeShield.Abstractions;
using CodeShield.CSharp.Evaluation;

namespace CodeShield.CSharp;

/// <summary>Per-analysis services shared by every context of one snapshot.</summary>
internal sealed record AnalysisEnvironment(
    RepoPaths Paths,
    IInputRecorder Inputs,
    string AssetsRoot,
    ProjectEvaluator Evaluator)
{
    /// <summary>Reads a file, recording it through the input recorder as repository or external input.</summary>
    public byte[] ReadInput(string fullPath)
    {
        var repoPath = Paths.ToRepoPath(fullPath);
        return repoPath is null
            ? Inputs.ReadAndRecord(InputScopes.External, RepoPaths.Canonical(fullPath), fullPath)
            : Inputs.ReadAndRecord(InputScopes.Repository, repoPath, fullPath);
    }
}


