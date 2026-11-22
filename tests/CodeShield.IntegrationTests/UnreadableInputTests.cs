using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>A held-open (FileShare.None) input must yield a structured incomplete result, never a tool error or a false pass.</summary>
public sealed class UnreadableInputTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    [Fact]
    public async Task LockedUntrackedFileIsIncompleteWithItsPathAndUnlockedControlIsComplete()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["api.ts"] = "export const api = 1;\n",
        });
        repo.Write("new.ts", "export const added = 1;\n");
        await using var session = await McpSession.StartAsync();

        JsonElement locked;
        using (new FileStream(Path.Combine(repo.Root, "new.ts"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            locked = await session.EvaluateAsync(repo.Root, baseline);
        }

        Assert.Equal("incomplete", locked.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, locked.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, locked.GetProperty("passed").ValueKind);
        Assert.Contains(locked.GetProperty("reasons").EnumerateArray(),
            reason => reason.GetProperty("code").GetString() == "UNREADABLE_SOURCE"
                && reason.GetProperty("message").GetString()!.Contains("new.ts", StringComparison.Ordinal));
        Assert.Contains(locked.GetProperty("coverage").EnumerateArray(), entry => entry.GetProperty("status").GetString() == "failed");

        var unlocked = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal("complete", unlocked.GetProperty("status").GetString());
        Assert.Equal(100, unlocked.GetProperty("score").GetInt32());
        Assert.True(unlocked.GetProperty("passed").GetBoolean());
    }
}
