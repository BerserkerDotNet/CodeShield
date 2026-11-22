using CodeShield.Abstractions;
using CodeShield.Core.Evaluation;
using CodeShield.Core.Git;
using CodeShield.Core.Graph;
using CodeShield.Core.Scope;
using CodeShield.Git;

namespace CodeShield.Core.Tests;

public sealed class PureLogicTests
{
    [Fact]
    public void DiffParserReadsHunksWithImpliedCounts()
    {
        var diff = DiffParser.Parse("@@ -3 +3 @@\n-a\n+b\n@@ -10,2 +11,0 @@\n-x\n-y\n");
        Assert.Equal([new Hunk(3, 1, 3, 1), new Hunk(10, 2, 11, 0)], diff.Hunks);
        Assert.True(diff.Hunks[1].IsPureDeletion);
        Assert.False(diff.IsBinary);
    }

    [Fact]
    public void ChangeMapMapsLinesAcrossAnInsertion()
    {
        var change = new FileChange(ChangeKind.Modified, "a.ts", null, [new Hunk(2, 0, 3, 2)]);
        var map = new ChangeMap([change], ["a.ts"]);
        Assert.Equal(2, map.MapLineToBaseline("a.ts", 2));
        Assert.Null(map.MapLineToBaseline("a.ts", 4));
        Assert.Equal(3, map.MapLineToBaseline("a.ts", 5));
        Assert.True(map.IsLineChanged("a.ts", 3));
        Assert.False(map.IsLineChanged("a.ts", 6));
    }

    [Fact]
    public void ChangeMapFollowsRenames()
    {
        var map = new ChangeMap([new FileChange(ChangeKind.Renamed, "new.ts", "old.ts", [])], ["old.ts"]);
        Assert.Equal("old.ts", map.BaselinePathOf("new.ts"));
        Assert.Equal("new.ts", map.CurrentPathOf("old.ts"));
    }

    [Fact]
    public void NameStatusParserHandlesRenamesCopiesAndOddPaths()
    {
        var entries = NameStatusParser.Parse("M\0a b.ts\0R100\0old.ts\0new.ts\0C80\0src.ts\0copy.ts\0D\0gone.ts\0");
        Assert.Equal(
            [
                new NameStatusEntry(ChangeKind.Modified, "a b.ts", null),
                new NameStatusEntry(ChangeKind.Renamed, "new.ts", "old.ts"),
                new NameStatusEntry(ChangeKind.Added, "copy.ts", null),
                new NameStatusEntry(ChangeKind.Deleted, "gone.ts", null),
            ],
            entries);
    }

    [Fact]
    public void StronglyConnectedReportsOnlyRealCycles()
    {
        var cycles = StronglyConnected.Cycles(
            ["a", "b", "c", "d"],
            [("a", "b"), ("b", "c"), ("c", "a"), ("c", "d")]);
        Assert.Single(cycles);
        Assert.Contains(cycles, cycle => cycle.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(["a", "b", "c"]));
    }

    [Fact]
    public void SnapshotIdentityIsOrderIndependentAndContentSensitive()
    {
        InputRecord[] first = [new("repository", "a", "1"), new("repository", "b", "2")];
        InputRecord[] reordered = [first[1], first[0], first[0]];
        InputRecord[] changed = [first[0], new("repository", "b", "3")];
        Assert.Equal(SnapshotIdentity.Compute(first), SnapshotIdentity.Compute(reordered));
        Assert.NotEqual(SnapshotIdentity.Compute(first), SnapshotIdentity.Compute(changed));
    }

    [Fact]
    public void SnapshotIdentityDistinguishesFieldBoundaries()
    {
        Assert.NotEqual(
            SnapshotIdentity.Compute([new InputRecord("repository", "ab", "c")]),
            SnapshotIdentity.Compute([new InputRecord("repository", "a", "bc")]));
    }

    private static InputRecord Entry(string scope, string path, params byte[] content) =>
        new(scope, path, Hashing.Sha256Hex(content));

    [Fact]
    public void SnapshotIdentityMatchesGoldenSingleEntry()
    {
        var id = SnapshotIdentity.Compute([Entry("repository", "a.ts", 0x78, 0x0a)]);
        Assert.Equal("e7552a4abc1eb60c9fc4ec1bb2d3a030b4cd2b0fd820a1f33dbe4711a0f040eb", id);
    }

    [Fact]
    public void SnapshotIdentitySortsByUtf8BytesNotUtf16()
    {
        var id = SnapshotIdentity.Compute([
            Entry("repository", "\uD800\uDC00.cs", 0x61),
            Entry("repository", "a.ts", 0x78, 0x0a),
            Entry("external", @"C:\refs\lib.dll", 0x72, 0x65, 0x66),
            Entry("repository", "\uE000.cs"),
        ]);
        Assert.Equal("0211caab58f93dc57b8be32ffe6952515d80e4e5e60e18236a38c50e56e531eb", id);
    }
}
