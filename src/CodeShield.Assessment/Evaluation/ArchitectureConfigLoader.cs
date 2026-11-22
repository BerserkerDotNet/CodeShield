using System.Text.Json;
using CodeShield.Abstractions;
using CodeShield.Core.Rules;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Evaluation;

/// <summary>Reads the optional <c>.quality-architecture.json</c> and records it as an analysis input.</summary>
public static class ArchitectureConfigLoader
{
    public static (ArchitectureConfig? Config, InputRecord? Input) Load(string root)
    {
        var path = Path.Combine(root, SupportedFiles.ArchitectureConfig);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        var bytes = File.ReadAllBytes(path);
        var input = new InputRecord(InputScopes.Repository, SupportedFiles.ArchitectureConfig, Hashing.Sha256Hex(bytes));
        return (Parse(bytes), input);
    }

    public static ArchitectureConfig Parse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            return new ArchitectureConfig(ReadRules(document.RootElement), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new ArchitectureConfig([], $"{SupportedFiles.ArchitectureConfig} is invalid: {exception.Message}");
        }
    }

    private static List<ArchitectureRule> ReadRules(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("forbiddenDependencies", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("expected an object with a 'forbiddenDependencies' array");
        }

        return [.. list.EnumerateArray().Select(ReadRule)];
    }

    private static ArchitectureRule ReadRule(JsonElement item)
    {
        var from = Read(item, "from");
        var to = Read(item, "to");
        return new ArchitectureRule(BoundaryRule.Normalize(from), BoundaryRule.Normalize(to));
    }

    private static string Read(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"each dependency needs a non-empty '{name}'");
}
