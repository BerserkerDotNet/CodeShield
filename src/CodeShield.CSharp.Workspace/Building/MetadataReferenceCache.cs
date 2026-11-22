using System.Collections.Concurrent;
using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Building;

/// <summary>Process-wide cache of metadata references, validated by file size and timestamp.</summary>
internal static class MetadataReferenceCache
{
    private sealed record Entry(long Length, long LastWriteTicks, string Hash, MetadataReference Reference);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static MetadataReference Load(string fullPath, IInputRecorder inputs)
    {
        var info = new FileInfo(fullPath);
        var key = RepoPaths.Canonical(fullPath);
        if (!Entries.TryGetValue(key, out var entry) || entry.Length != info.Length || entry.LastWriteTicks != info.LastWriteTimeUtc.Ticks)
        {
            entry = Create(fullPath, info);
            Entries[key] = entry;
        }

        inputs.Record(InputScopes.External, key, entry.Hash);
        return entry.Reference;
    }

    private static Entry Create(string fullPath, FileInfo info)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var reference = MetadataReference.CreateFromImage(bytes, filePath: fullPath);
        return new Entry(bytes.Length, info.LastWriteTimeUtc.Ticks, Hashing.Sha256Hex(bytes), reference);
    }
}

