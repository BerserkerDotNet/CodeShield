using CodeShield.Abstractions;

namespace CodeShield.Core.Duplication;

public sealed class TokenFile(string path, IReadOnlyList<Token> tokens, Func<Token, int> intern)
{
    public string Path { get; } = path;

    public IReadOnlyList<Token> Tokens { get; } = tokens;

    public int[] Ids { get; } = [.. tokens.Select(intern)];
}

/// <summary>An exact token match: two equal ranges of <c>Length</c> tokens.</summary>
public sealed record TokenMatch(int FileA, int StartA, int FileB, int StartB, int Length);

/// <summary>Finds maximal exact token-sequence matches that touch changed code.</summary>
public sealed class DuplicateFinder(IReadOnlyList<TokenFile> files, Func<string, int, bool> lineChanged)
{
    public const int Window = 50;
    public const int MinCodeLines = 10;
    private const int MaxBucket = 64;
    private const ulong Base = 1_000_003UL;

    private readonly List<int[]> _changedPrefix = [.. files.Select(file => ChangedPrefix(file, lineChanged))];
    private readonly Dictionary<(int, int, int), int> _covered = [];
    private readonly List<TokenMatch> _matches = [];

    public IReadOnlyList<TokenMatch> Find()
    {
        var hashes = files.Select(file => WindowHashes(file.Ids)).ToList();
        var index = BuildIndex(hashes);
        for (var fileA = 0; fileA < files.Count; fileA++)
        {
            ScanFile(fileA, hashes[fileA], index);
        }

        return _matches;
    }

    private void ScanFile(int fileA, ulong[] hashes, Dictionary<ulong, List<(int File, int Pos)>> index)
    {
        for (var posA = 0; posA < hashes.Length; posA++)
        {
            var bucket = index[hashes[posA]];
            if (bucket.Count < 2)
            {
                continue;
            }

            foreach (var (fileB, posB) in bucket.Where(item => item.File > fileA || (item.File == fileA && item.Pos > posA)))
            {
                TryMatch(fileA, posA, fileB, posB);
            }
        }
    }

    private void TryMatch(int fileA, int posA, int fileB, int posB)
    {
        var key = (fileA, fileB, posB - posA);
        if (_covered.TryGetValue(key, out var end) && posA < end)
        {
            return;
        }

        var length = ExtendedLength(fileA, posA, fileB, posB);
        _covered[key] = posA + Math.Max(length, 1);
        if (length >= Window && Qualifies(new TokenMatch(fileA, posA, fileB, posB, length)))
        {
            _matches.Add(new TokenMatch(fileA, posA, fileB, posB, length));
        }
    }

    private int ExtendedLength(int fileA, int posA, int fileB, int posB)
    {
        var a = files[fileA].Ids;
        var b = files[fileB].Ids;
        var limit = fileA == fileB ? posB - posA : int.MaxValue;
        var length = 0;
        while (length < limit && posA + length < a.Length && posB + length < b.Length && a[posA + length] == b[posB + length])
        {
            length++;
        }

        return length;
    }

    private bool Qualifies(TokenMatch match) =>
        CodeLines(match.FileA, match.StartA, match.Length) >= MinCodeLines
        && CodeLines(match.FileB, match.StartB, match.Length) >= MinCodeLines
        && (HasChange(match.FileA, match.StartA, match.Length) || HasChange(match.FileB, match.StartB, match.Length));

    private bool HasChange(int file, int start, int length) =>
        _changedPrefix[file][start + length] - _changedPrefix[file][start] > 0;

    public int CodeLines(int file, int start, int length) =>
        files[file].Tokens.Skip(start).Take(length).Select(token => token.Line).Distinct().Count();

    private static int[] ChangedPrefix(TokenFile file, Func<string, int, bool> lineChanged)
    {
        var prefix = new int[file.Tokens.Count + 1];
        for (var i = 0; i < file.Tokens.Count; i++)
        {
            prefix[i + 1] = prefix[i] + (lineChanged(file.Path, file.Tokens[i].Line) ? 1 : 0);
        }

        return prefix;
    }

    private static Dictionary<ulong, List<(int File, int Pos)>> BuildIndex(List<ulong[]> hashes)
    {
        var index = new Dictionary<ulong, List<(int, int)>>();
        var entries = hashes.SelectMany((row, file) => row.Select((hash, pos) => (hash, file, pos)));
        foreach (var (hash, file, pos) in entries)
        {
            if (!index.TryGetValue(hash, out var bucket))
            {
                index[hash] = bucket = [];
            }

            bucket.Add((file, pos));
        }

        return index;
    }

    private static ulong[] WindowHashes(int[] ids)
    {
        if (ids.Length < Window)
        {
            return [];
        }

        var power = 1UL;
        for (var i = 1; i < Window; i++)
        {
            power *= Base;
        }

        var hashes = new ulong[ids.Length - Window + 1];
        var hash = 0UL;
        for (var i = 0; i < ids.Length; i++)
        {
            hash = unchecked(hash * Base + (ulong)ids[i] + 1);
            if (i >= Window)
            {
                hash = unchecked(hash - (((ulong)ids[i - Window] + 1) * power * Base));
            }

            if (i >= Window - 1)
            {
                hashes[i - Window + 1] = hash;
            }
        }

        return hashes;
    }
}
