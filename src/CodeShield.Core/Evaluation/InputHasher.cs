using CodeShield.Abstractions;

namespace CodeShield.Core.Evaluation;

/// <summary>Hashes repository inputs, turning expected filesystem errors into explicit unreadable-source reasons.</summary>
public static class InputHasher
{
    public sealed record Result(IReadOnlyList<InputRecord> Inputs, IReadOnlyList<Reason> Unreadable);

    public static Result HashAll(string root, IEnumerable<string> paths)
    {
        var inputs = new List<InputRecord>();
        var unreadable = new List<Reason>();
        foreach (var path in paths)
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                continue;
            }

            try
            {
                inputs.Add(new InputRecord(InputScopes.Repository, path, Hashing.Sha256Hex(File.ReadAllBytes(full))));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(new Reason(ReasonCodes.UnreadableSource, $"Cannot read '{path}': {exception.Message}", null, path));
            }
        }

        return new Result(inputs, unreadable);
    }
}
