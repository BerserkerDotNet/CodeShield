using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace CodeShield.IntegrationTests;

/// <summary>Loads the published v1 schemas from docs\schemas and validates real or mutated responses against them.</summary>
internal static class PublishedSchemas
{
    private static readonly Lazy<(JsonSchema Evaluate, JsonSchema Details)> Loaded = new(Load);

    public static bool EvaluateIsValid(JsonNode response) => IsValid(Loaded.Value.Evaluate, response);

    public static bool DetailsIsValid(JsonNode response) => IsValid(Loaded.Value.Details, response);

    private static bool IsValid(JsonSchema schema, JsonNode response) =>
        schema.Evaluate(JsonSerializer.SerializeToElement(response), new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical }).IsValid;

    private static (JsonSchema, JsonSchema) Load()
    {
        var directory = SchemaDirectory();
        var registry = new SchemaRegistry();
        var build = new BuildOptions { SchemaRegistry = registry };
        JsonSchema Read(string name) => JsonSchema.FromText(File.ReadAllText(Path.Combine(directory, name)), build);
        Read("common.v1.schema.json");
        Read("evidence.v1.schema.json");
        return (Read("evaluate_quality.v1.schema.json"), Read("get_rule_details.v1.schema.json"));
    }

    private static string SchemaDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "schemas");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("docs\\schemas not found above the test binaries.");
    }
}
