using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CodeShield.Abstractions;

namespace CodeShield.Core.Evaluation;

/// <summary>Orders strings by their UTF-8 bytes; differs from UTF-16 ordinal order for supplementary characters.</summary>
internal sealed class Utf8Order : IComparer<string>
{
    public static Utf8Order Instance { get; } = new();

    public int Compare(string? x, string? y) =>
        Encoding.UTF8.GetBytes(x ?? "").AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y ?? ""));
}

/// <summary>Computes the snapshot ID and detects whether the analyzed inputs changed since analysis.</summary>
public static class SnapshotIdentity
{
    public static string Compute(IEnumerable<InputRecord> inputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var input in SnapshotInventory.Build(inputs).Entries)
        {
            Append(hash, input.Scope);
            Append(hash, input.Path);
            Append(hash, input.Sha256);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>True when an input was observed with two hashes or no longer matches the bytes on disk, external references included.</summary>
    public static bool IsStale(string root, IEnumerable<InputRecord> inputs)
    {
        var inventory = SnapshotInventory.Build(inputs);
        return inventory.Conflicts.Count > 0 || inventory.Entries.Any(input => !MatchesDisk(FullPath(root, input), input.Sha256));
    }

    private static string FullPath(string root, InputRecord input) => input.Scope == InputScopes.Repository
        ? Path.Combine(root, input.Path.Replace('/', Path.DirectorySeparatorChar))
        : input.Path;

    private static bool MatchesDisk(string fullPath, string expected)
    {
        try
        {
            return string.Equals(Hashing.Sha256Hex(File.ReadAllBytes(fullPath)), expected, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
