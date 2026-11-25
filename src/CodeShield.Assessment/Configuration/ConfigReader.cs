using System.Text.Json;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Configuration;

/// <summary>Strictly parses and validates one <c>.codeshield.json</c>; every problem names the file.</summary>
public sealed class ConfigReader(string path, Ruleset ruleset)
{
    private static readonly string[] TopLevel = ["version", "exclude", "reason", "rules"];
    private static readonly string[] RuleLevel = ["enabled", "limit", "reason"];

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip };

    public FolderConfig Read(byte[] bytes, string sha256)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object, "expected a JSON object");
        RejectUnknown(root, TopLevel, "property");
        RequireVersion(root);
        var exclude = OptionalBool(root, "exclude");
        var reason = OptionalReason(root, "reason");
        Require(exclude != true || reason is not null, "'reason' is required when 'exclude' is true");
        return new FolderConfig(path, DirectoryOf(path), sha256, exclude, reason, ReadRules(root));
    }

    public static string DirectoryOf(string configPath)
    {
        var index = configPath.LastIndexOf('/');
        return index < 0 ? string.Empty : configPath[..index];
    }

    private JsonDocument Parse(byte[] bytes)
    {
        try
        {
            return JsonDocument.Parse(bytes, Options);
        }
        catch (JsonException exception)
        {
            throw Fail($"is not valid JSON ({exception.Message})");
        }
    }

    private void RequireVersion(JsonElement root)
    {
        var valid = root.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1;
        Require(valid, "'version' must be the number 1");
    }

    private Dictionary<string, RuleSetting> ReadRules(JsonElement root)
    {
        var rules = new Dictionary<string, RuleSetting>(StringComparer.Ordinal);
        if (!root.TryGetProperty("rules", out var section))
        {
            return rules;
        }

        Require(section.ValueKind == JsonValueKind.Object, "'rules' must be an object");
        foreach (var entry in section.EnumerateObject())
        {
            Require(ruleset.Find(entry.Name) is not null, $"unknown rule '{entry.Name}'");
            rules[entry.Name] = ReadRule(entry.Name, entry.Value);
        }

        return rules;
    }

    private RuleSetting ReadRule(string ruleId, JsonElement element)
    {
        Require(element.ValueKind == JsonValueKind.Object, $"rule '{ruleId}' must be an object");
        RejectUnknown(element, RuleLevel, $"property of rule '{ruleId}'");
        var enabled = OptionalBool(element, "enabled");
        var limit = ReadLimit(ruleId, element);
        var reason = OptionalReason(element, "reason");
        Require(enabled != false || reason is not null, $"rule '{ruleId}' needs a 'reason' when 'enabled' is false");
        Require(reason is not null || !IsLoose(ruleId, limit), $"rule '{ruleId}' needs a 'reason' for a limit looser than the default");
        return new RuleSetting(enabled, limit, reason);
    }

    private bool IsLoose(string ruleId, decimal? limit) =>
        limit is { } value && LimitDomain.IsLooserThanDefault(value, ruleset.Get(ruleId).Limit);

    private decimal? ReadLimit(string ruleId, JsonElement element)
    {
        if (!element.TryGetProperty("limit", out var raw))
        {
            return null;
        }

        var domain = LimitDomain.For(ruleId);
        Require(domain is not null, $"rule '{ruleId}' does not have a configurable limit");
        var valid = raw.ValueKind == JsonValueKind.Number && raw.TryGetDecimal(out var value) && domain!.Accepts(value);
        Require(valid, $"limit of rule '{ruleId}' must be {domain!.Description}");
        return raw.GetDecimal();
    }

    private bool? OptionalBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"'{name}' must be true or false");
        return value.GetBoolean();
    }

    private string? OptionalReason(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var valid = value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());
        Require(valid, $"'{name}' must be a non-empty string");
        return value.GetString();
    }

    private void RejectUnknown(JsonElement element, string[] allowed, string noun)
    {
        foreach (var property in element.EnumerateObject())
        {
            Require(allowed.Contains(property.Name, StringComparer.Ordinal), $"unknown {noun} '{property.Name}'");
        }
    }

    private void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw Fail(problem);
        }
    }

    private ConfigurationException Fail(string problem) => new($"{path}: {problem}");
}
