using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CodeShield.TypeScript;

/// <summary>Serialization options for the TypeScript worker protocol.</summary>
public static class WorkerJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
