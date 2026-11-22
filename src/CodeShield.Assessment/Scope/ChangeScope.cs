namespace CodeShield.Core.Scope;

public sealed record ChangeScope(
    string Root,
    string BaselineCommit,
    ChangeMap Map,
    IReadOnlyList<string> BaselinePaths,
    IReadOnlyList<string> CurrentPaths,
    string InputPaths)
{
    /// <summary>Deterministic text identifying the discovered change set and relevant input paths, used for staleness detection.</summary>
    public string Fingerprint =>
        string.Join('\n', Map.Changes.Select(change => $"{change.Kind}|{change.Path}|{change.OldPath}")) + "\n--\n" + InputPaths;
}
