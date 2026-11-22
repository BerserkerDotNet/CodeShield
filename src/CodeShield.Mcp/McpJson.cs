using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodeShield.Mcp;

/// <summary>Serialization options for MCP tool results; wire names are part of the published v1 contract.</summary>
public static class McpJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
