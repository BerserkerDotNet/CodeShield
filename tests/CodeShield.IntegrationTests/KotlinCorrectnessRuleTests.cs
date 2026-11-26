namespace CodeShield.IntegrationTests;

/// <summary>Every selected Kotlin correctness rule (detekt, pinned options) through the real host, the worker and the user's detekt-cli.</summary>
public sealed class KotlinCorrectnessRuleTests
{
    public static TheoryData<string, string> Triggers => new()
    {
        { "equals-always-returns-true-or-false", "class AlwaysEqual {\n    override fun equals(other: Any?): Boolean {\n        return true\n    }\n    override fun hashCode(): Int = 1\n}\n" },
        { "equals-with-hash-code-exist", "class OnlyEquals {\n    override fun equals(other: Any?): Boolean = other is OnlyEquals\n}\n" },
        { "exception-raised-in-unexpected-location", "class Text {\n    override fun toString(): String {\n        throw IllegalStateException(\"no text\")\n    }\n}\n" },
        { "explicit-garbage-collection-call", "fun collectGarbage() {\n    System.gc()\n}\n" },
        { "invalid-range", "fun countDown() {\n    for (i in 10..1) println(i)\n}\n" },
        {
            "iterator-has-next-calls-next-method",
            "class Peeking(private var left: Int) : Iterator<Int> {\n    override fun hasNext(): Boolean {\n        next()\n        return left > 0\n    }\n"
            + "    override fun next(): Int {\n        if (left <= 0) throw NoSuchElementException()\n        left -= 1\n        return left\n    }\n}\n"
        },
        { "iterator-not-throwing-no-such-element-exception", "class Endless : Iterator<Int> {\n    override fun hasNext(): Boolean = true\n    override fun next(): Int = 1\n}\n" },
        { "throwing-exception-from-finally", "fun risky(): Int {\n    try {\n        return 1\n    } finally {\n        throw IllegalStateException(\"boom\")\n    }\n}\n" },
        { "unconditional-jump-statement-in-loop", "fun first(values: List<Int>): Int {\n    for (v in values) {\n        return v\n    }\n    return 0\n}\n" },
        { "useless-postfix-expression", "class LostIncrement {\n    private var counter = 0\n    fun bump() {\n        counter = counter++\n    }\n}\n" },
        { "wrong-equals-type-parameter", "class Name(private val value: String) {\n    fun equals(other: String): Boolean = value == other\n}\n" },
    };

    private const string Clean =
        "package app\n\n"
        + "class SometimesEqual {\n    override fun equals(other: Any?): Boolean {\n        return other is SometimesEqual\n    }\n    override fun hashCode(): Int = 1\n}\n\n"
        + "class BothOverridden {\n    override fun equals(other: Any?): Boolean = other is BothOverridden\n    override fun hashCode(): Int = 7\n}\n\n"
        + "class Describer {\n    fun describe(): String {\n        throw IllegalStateException(\"not yet\")\n    }\n}\n\n"
        + "fun clearCache(cache: MutableList<Int>) {\n    cache.clear()\n}\n\n"
        + "fun countUp() {\n    for (i in 1..10) println(i)\n    for (i in 10 downTo 1) println(i)\n}\n\n"
        + "class Bounded(private var left: Int) : Iterator<Int> {\n    override fun hasNext(): Boolean = left > 0\n"
        + "    override fun next(): Int {\n        if (left <= 0) throw NoSuchElementException()\n        left -= 1\n        return left\n    }\n}\n\n"
        + "fun careful(): Int {\n    try {\n        return 1\n    } finally {\n        println(\"done\")\n    }\n}\n\n"
        + "fun firstPositive(values: List<Int>): Int {\n    for (v in values) {\n        if (v > 0) return v\n    }\n    return 0\n}\n\n"
        + "class KeptIncrement {\n    private var counter = 0\n    fun bump() {\n        counter++\n    }\n}\n\n"
        + "class Label(private val value: String) {\n    fun matches(other: String): Boolean = value == other\n}\n";

    [Theory]
    [MemberData(nameof(Triggers))]
    public async Task RuleTriggersWithItsPinnedOptions(string rule, string source)
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/app/Seed.kt", "package app\n\n" + source);

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var finding = Assert.Single(KotlinFixture.Findings(response, "correctness."));
        Assert.Equal("correctness.kotlin." + rule, finding.GetProperty("ruleId").GetString());
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
        Assert.Equal("introduced", finding.GetProperty("attribution").GetString());
        Assert.Equal("src/main/kotlin/app/Seed.kt", KotlinFixture.FileOf(finding));
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(88, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task CorrectedCodeProducesNoCorrectnessFindings()
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/app/Seed.kt", Clean);

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        Assert.Empty(KotlinFixture.Findings(response, "correctness."));
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task AnUntouchedDiagnosticInAChangedFileIsPreExistingAndNotCharged()
    {
        const string Old = "package app\n\nfun collectGarbage() {\n    System.gc()\n}\n";
        var (repo, baseline) = KotlinFixture.SingleProject(new Dictionary<string, string> { ["src/main/kotlin/app/Old.kt"] = Old });
        using var _ = repo;
        repo.Write("src/main/kotlin/app/Old.kt", "package app\n\nfun added(): Int = 2\n\n" + Old[13..]);

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var finding = Assert.Single(KotlinFixture.Findings(response, "correctness.kotlin."));
        Assert.Equal("correctness.kotlin.explicit-garbage-collection-call", finding.GetProperty("ruleId").GetString());
        Assert.Equal("pre_existing", finding.GetProperty("attribution").GetString());
        Assert.Equal(0, finding.GetProperty("deduction").GetInt32());
        Assert.Equal(6, finding.GetProperty("locations")[0].GetProperty("startLine").GetInt32());
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }
}
