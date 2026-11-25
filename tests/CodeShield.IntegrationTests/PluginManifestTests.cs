using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>The Agent Plugins and Claude manifests share one launch definition, one skill and one server.</summary>
public sealed class PluginManifestTests
{
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "integrations", "codeshield", "plugin.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("integrations/codeshield not found.");
    }

    private static JsonElement Read(params string[] segments) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine([Root(), .. segments]))).RootElement.Clone();

    private static JsonElement Servers(string file) =>
        Read("integrations", "codeshield", file).GetProperty("mcpServers");

    [Fact]
    public void BothClientsLaunchTheSameSingleServer()
    {
        var shared = Servers("mcp.json");
        var claude = Servers(".mcp.json");

        var server = Assert.Single(shared.EnumerateObject());
        Assert.Equal("codeshield", server.Name);
        Assert.Equal("codeshield", server.Value.GetProperty("command").GetString());
        Assert.Equal(["mcp"], server.Value.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(server.Value.GetProperty("command").GetString(), claude.GetProperty("codeshield").GetProperty("command").GetString());
        Assert.Equal(server.Value.GetProperty("args").GetRawText(), claude.GetProperty("codeshield").GetProperty("args").GetRawText());
        Assert.Single(claude.EnumerateObject());
    }

    [Fact]
    public void ManifestsAndCatalogsAgreeOnNameAndVersion()
    {
        var names = new[]
        {
            Read("integrations", "codeshield", "plugin.json"),
            Read("integrations", "codeshield", ".claude-plugin", "plugin.json"),
        };

        Assert.All(names, manifest => Assert.Equal("codeshield", manifest.GetProperty("name").GetString()));
        Assert.Equal(names[0].GetProperty("version").GetString(), names[1].GetProperty("version").GetString());
        foreach (var catalog in new[] { Read(".claude-plugin", "marketplace.json"), Read(".github", "plugin", "marketplace.json") })
        {
            var entry = Assert.Single(catalog.GetProperty("plugins").EnumerateArray());
            Assert.Equal("codeshield", entry.GetProperty("name").GetString());
            Assert.Equal("./integrations/codeshield", entry.GetProperty("source").GetString());
        }
    }

    [Fact]
    public void SkillCarriesTheAgentGuidanceVerbatim()
    {
        var guidance = File.ReadAllLines(Path.Combine(Root(), "docs", "agent-instructions", "copilot-instructions.md")).Skip(4);
        var skill = File.ReadAllText(Path.Combine(Root(), "integrations", "codeshield", "skills", "quality-check", "SKILL.md"));

        Assert.StartsWith("---\nname: quality-check\n", skill, StringComparison.Ordinal);
        Assert.Contains(string.Join('\n', guidance).TrimEnd(), skill.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
