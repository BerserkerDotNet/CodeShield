using CodeShield.Abstractions;

namespace CodeShield.Core.Evaluation;

/// <summary>The deduplicated, UTF-8-ordered analysis inputs plus any path that was observed with more than one hash.</summary>
public sealed record SnapshotInventory(IReadOnlyList<InputRecord> Entries, IReadOnlyList<InputRecord> Conflicts)
{
    public static SnapshotInventory Build(IEnumerable<InputRecord> inputs)
    {
        var groups = inputs.GroupBy(input => (input.Scope, input.Path)).ToList();
        var conflicts = groups.Where(group => group.Select(input => input.Sha256).Distinct().Count() > 1)
            .SelectMany(group => group.DistinctBy(input => input.Sha256)).ToList();
        var entries = groups.Select(group => group.OrderBy(input => input.Sha256, StringComparer.Ordinal).First())
            .OrderBy(input => input.Scope, Utf8Order.Instance).ThenBy(input => input.Path, Utf8Order.Instance).ToList();
        return new SnapshotInventory(entries, conflicts);
    }

    /// <summary>True when two discovery passes saw different inventories (added, removed or changed inputs, or a path with two hashes).</summary>
    public static bool Differs(IEnumerable<InputRecord> first, IEnumerable<InputRecord> second)
    {
        var left = Build(first);
        var right = Build(second);
        return left.Conflicts.Count > 0 || right.Conflicts.Count > 0 || !left.Entries.SequenceEqual(right.Entries);
    }
}
