using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Every selected TypeScript correctness rule, with its pinned explicit options, through the real host.</summary>
public sealed class TypeScriptCorrectnessRuleTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    public static TheoryData<string, string> Triggers => new()
    {
        { "no-floating-promises", "export async function work(): Promise<void> {}\nexport function run(): void {\n  void work();\n}\n" },
        { "no-misused-promises", "export function f(items: number[]): void {\n  items.forEach(async (x) => {\n    await Promise.resolve(x);\n  });\n}\n" },
        { "await-thenable", "export async function f(): Promise<number> {\n  return await 5;\n}\n" },
        { "array-callback-return", "export function f(items: number[]): number[] {\n  return items.map((x) => {\n    if (x > 1) {\n      return x;\n    }\n  });\n}\n" },
        { "for-direction", "export function f(): number {\n  let n = 0;\n  for (let i = 0; i < 10; i--) {\n    n += i;\n  }\n  return n;\n}\n" },
        { "no-async-promise-executor", "export const p = new Promise<number>(async (resolve) => {\n  resolve(1);\n});\n" },
        { "no-constant-binary-expression", "export function f(a: number): boolean {\n  return a === {};\n}\n" },
        { "no-dupe-else-if", "export function f(a: boolean): number {\n  if (a) {\n    return 1;\n  } else if (a) {\n    return 2;\n  }\n  return 3;\n}\n" },
        { "no-self-assign", "export function f(a: number): number {\n  a = a;\n  return a;\n}\n" },
        { "no-unsafe-finally", "export function f(): number {\n  try {\n    return 1;\n  } finally {\n    return 2;\n  }\n}\n" },
        { "no-unsafe-optional-chaining", "export function f(o?: { y: { z: number } }): number {\n  const { z } = o?.y;\n  return z;\n}\n" },
        { "use-isnan", "export function f(a: number): boolean {\n  return a === NaN;\n}\n" },
    };

    private const string Clean =
        "export async function work(): Promise<void> {}\n"
        + "export async function run(): Promise<void> {\n  await work();\n  await Promise.resolve(1);\n}\n"
        + "export function map(items: number[]): number[] {\n  return items.map((x) => x * 2);\n}\n"
        + "export function down(): number {\n  let n = 0;\n  for (let i = 10; i > 0; i--) {\n    n += i;\n  }\n  return n;\n}\n"
        + "export function branch(a: boolean, b: boolean): number {\n  if (a) {\n    return 1;\n  } else if (b) {\n    return 2;\n  }\n  return 3;\n}\n"
        + "export function nan(a: number): boolean {\n  return Number.isNaN(a);\n}\n"
        + "export function chain(o?: { y: { z: number } }): number | undefined {\n  return o?.y.z;\n}\n"
        + "export function guarded(): number {\n  try {\n    return 1;\n  } finally {\n    console.log('done');\n  }\n}\n"
        + "export const p = new Promise<number>((resolve) => {\n  resolve(1);\n});\n";

    private static async Task<JsonElement> EvaluateAsync(string content)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["a.ts"] = "export const a = 1;\n" });
        repo.Write("a.ts", content);
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static List<JsonElement> RuleFindings(JsonElement response, string rule) =>
        [.. response.GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("ruleId").GetString() == "correctness.typescript." + rule)];

    [Theory]
    [MemberData(nameof(Triggers))]
    public async Task RuleTriggersWithItsPinnedOptions(string rule, string source)
    {
        var response = await EvaluateAsync(source);

        var finding = Assert.Single(RuleFindings(response, rule));
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
        Assert.Equal("complete", response.GetProperty("status").GetString());
    }

    [Fact]
    public async Task VoidAloneIsStillAFloatingPromiseAndScoresEightyEight()
    {
        var response = await EvaluateAsync((string)Triggers.First(row => (string)row[0] == "no-floating-promises")[1]);

        Assert.Equal(88, response.GetProperty("score").GetInt32());
        Assert.False(response.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task CorrectedCodeProducesNoCorrectnessFindings()
    {
        var response = await EvaluateAsync(Clean);

        Assert.DoesNotContain(
            response.GetProperty("findings").EnumerateArray(),
            f => f.GetProperty("ruleId").GetString()!.StartsWith("correctness.", StringComparison.Ordinal));
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }
}
