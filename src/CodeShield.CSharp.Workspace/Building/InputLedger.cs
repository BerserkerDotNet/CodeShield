using CodeShield.Abstractions;

namespace CodeShield.CSharp.Building;

/// <summary>Thread-safe record of the files an analysis read, hashed from the bytes actually used.</summary>
public sealed class InputLedger : IInputRecorder
{
    private readonly Dictionary<(string Scope, string Path), SortedSet<string>> _entries = [];
    private readonly Lock _gate = new();

    public byte[] ReadAndRecord(string scope, string recordedPath, string fullPath)
    {
        var bytes = File.ReadAllBytes(fullPath);
        Record(scope, recordedPath, Hashing.Sha256Hex(bytes));
        return bytes;
    }

    public void Record(string scope, string path, string sha256)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue((scope, path), out var hashes))
            {
                _entries[(scope, path)] = hashes = new SortedSet<string>(StringComparer.Ordinal);
            }

            hashes.Add(sha256);
        }
    }

    public IReadOnlyList<InputRecord> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries.SelectMany(entry => entry.Value.Select(hash => new InputRecord(entry.Key.Scope, entry.Key.Path, hash)))];
        }
    }
}


