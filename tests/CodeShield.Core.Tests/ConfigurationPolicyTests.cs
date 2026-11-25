using CodeShield.Core.Configuration;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Tests;

public sealed class ConfigurationPolicyTests
{
    private static FolderConfig Read(string path, string json) =>
        new ConfigReader(path, RulesetCatalog.Default).Read(System.Text.Encoding.UTF8.GetBytes(json), "sha");

    private static string Rule(string id, string body) => $$"""{ "version": 1, "rules": { "{{id}}": {{body}} } }""";

    private static RulePolicy Policy(params FolderConfig[] configs) => new(configs);

    [Fact]
    public void RootOnlyAppliesEverywhere()
    {
        var policy = Policy(Read(".codeshield.json", Rule(RuleIds.Nesting, """{ "limit": 4, "reason": "legacy" }""")));
        Assert.Equal(4, policy.IntLimit(RuleIds.Nesting, "a/b/c.cs", 2));
        Assert.Equal(4, policy.IntLimit(RuleIds.Nesting, "c.cs", 2));
    }

    [Fact]
    public void NearestFolderWinsAndSettingsMergeIndividually()
    {
        var policy = Policy(
            Read(".codeshield.json", """{ "version": 1, "rules": { "complexity.nesting": { "limit": 4, "reason": "r" }, "complexity.cyclomatic": { "limit": 9, "reason": "r" } } }"""),
            Read("src/.codeshield.json", Rule(RuleIds.Nesting, """{ "limit": 1 }""")));
        Assert.Equal(1, policy.IntLimit(RuleIds.Nesting, "src/a.cs", 2));
        Assert.Equal(9, policy.IntLimit(RuleIds.Cyclomatic, "src/a.cs", 6));
        Assert.Equal(4, policy.IntLimit(RuleIds.Nesting, "other/a.cs", 2));
    }

    [Fact]
    public void SiblingFoldersDoNotAffectEachOther()
    {
        var policy = Policy(Read("a/.codeshield.json", """{ "version": 1, "exclude": true, "reason": "generated" }"""));
        Assert.True(policy.IsFullyExcluded("a/x.cs"));
        Assert.False(policy.IsFullyExcluded("b/x.cs"));
    }

    [Fact]
    public void NestedExcludeFalseReIncludesASubtree()
    {
        var policy = Policy(
            Read(".codeshield.json", """{ "version": 1, "exclude": true, "reason": "legacy" }"""),
            Read("keep/.codeshield.json", """{ "version": 1, "exclude": false }"""));
        Assert.True(policy.IsFullyExcluded("other/a.cs"));
        Assert.False(policy.IsFullyExcluded("keep/a.cs"));
        Assert.False(policy.IsExcluded(RuleIds.Nesting, "keep/deep/a.cs"));
    }

    [Fact]
    public void PrefixMatchingRespectsSegmentBoundaries()
    {
        var policy = Policy(Read("src/Foo/.codeshield.json", """{ "version": 1, "exclude": true, "reason": "r" }"""));
        Assert.True(policy.IsFullyExcluded("src/Foo/a.cs"));
        Assert.False(policy.IsFullyExcluded("src/FooBar/a.cs"));
    }

    [Fact]
    public void DisabledRuleIsExcludedOnlyForThatRule()
    {
        var policy = Policy(Read("k/.codeshield.json", Rule(RuleIds.MainSequence, """{ "enabled": false, "reason": "kernel" }""")));
        Assert.True(policy.IsExcluded(RuleIds.MainSequence, "k/k.csproj"));
        Assert.False(policy.IsExcluded(RuleIds.Nesting, "k/k.csproj"));
        Assert.False(policy.ExcludedEverywhere(RuleIds.MainSequence));
    }

    [Theory]
    [InlineData("""{ "version": 2 }""", "'version' must be the number 1")]
    [InlineData("""{ }""", "'version' must be the number 1")]
    [InlineData("[]", "expected a JSON object")]
    [InlineData("{ nope", "is not valid JSON")]
    [InlineData("""{ "version": 1, "bogus": 1 }""", "unknown property 'bogus'")]
    [InlineData("""{ "version": 1, "exclude": "yes" }""", "'exclude' must be true or false")]
    [InlineData("""{ "version": 1, "exclude": true }""", "'reason' is required when 'exclude' is true")]
    [InlineData("""{ "version": 1, "reason": "" }""", "'reason' must be a non-empty string")]
    [InlineData("""{ "version": 1, "rules": [] }""", "'rules' must be an object")]
    [InlineData("""{ "version": 1, "rules": { "made.up": {} } }""", "unknown rule 'made.up'")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": 3 } }""", "must be an object")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": { "x": 1 } } }""", "unknown property of rule")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": { "enabled": false } } }""", "needs a 'reason' when 'enabled' is false")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": { "limit": 3 } } }""", "needs a 'reason' for a limit looser")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": { "limit": 1.5, "reason": "r" } } }""", "limit of rule 'complexity.nesting' must be an integer")]
    [InlineData("""{ "version": 1, "rules": { "complexity.cyclomatic": { "limit": 0, "reason": "r" } } }""", "must be an integer between 1")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": { "limit": -1 } } }""", "must be an integer between 0")]
    [InlineData("""{ "version": 1, "rules": { "complexity.nesting": { "limit": "3", "reason": "r" } } }""", "limit of rule")]
    [InlineData("""{ "version": 1, "rules": { "coupling.main-sequence-distance": { "limit": 1.5, "reason": "r" } } }""", "a number between 0 and 1")]
    [InlineData("""{ "version": 1, "rules": { "coupling.module-cycle": { "limit": 1, "reason": "r" } } }""", "does not have a configurable limit")]
    public void InvalidConfigurationNamesFileAndProblem(string json, string problem)
    {
        var exception = Assert.Throws<ConfigurationException>(() => Read("pkg/.codeshield.json", json));
        Assert.StartsWith("pkg/.codeshield.json: ", exception.Message);
        Assert.Contains(problem, exception.Message);
    }

    [Fact]
    public void IntegerLimitAboveInt32MaxIsRejectedAndMaxIsAccepted()
    {
        var tooBig = Assert.Throws<ConfigurationException>(() =>
            Read(".codeshield.json", Rule(RuleIds.MethodLength, """{ "limit": 2147483648, "reason": "r" }""")));
        Assert.Contains("between 1 and 2147483647", tooBig.Message);
        var policy = Policy(Read(".codeshield.json", Rule(RuleIds.MethodLength, """{ "limit": 2147483647, "reason": "r" }""")));
        Assert.Equal(int.MaxValue, policy.IntLimit(RuleIds.MethodLength, "a.cs", 40));
    }

    [Fact]
    public void CommentsAndStricterLimitsNeedNoReason()
    {
        var config = Read(".codeshield.json", "// note\n" + Rule(RuleIds.Nesting, """{ "limit": 1 } /* stricter */"""));
        Assert.Equal(1, config.Rules[RuleIds.Nesting].Limit);
    }

    [Fact]
    public void LooseDefaultLcom4LimitOfOneNeedsNoReason()
    {
        Assert.Equal(1, Read(".codeshield.json", Rule(RuleIds.Lcom4, """{ "limit": 1 }""")).Rules[RuleIds.Lcom4].Limit);
    }
}
