using CodeShield.TypeScript;

namespace CodeShield.Core.Tests;

public sealed class NodeRequirementTests
{
    [Theory]
    [InlineData("v22.12.0", false)]
    [InlineData("v22.13.0", true)]
    [InlineData("v22.20.0", true)]
    [InlineData("v23.5.0", false)]
    [InlineData("v24.0.0", true)]
    [InlineData("v20.19.0", false)]
    [InlineData("v26.1.0", true)]
    public void SupportsOnlyTheIntersectionOfTheLintEngineRanges(string output, bool supported) =>
        Assert.Equal(supported, NodeRequirement.IsSupported(NodeRequirement.Parse(output)!));

    [Theory]
    [InlineData("")]
    [InlineData("not a version")]
    public void UnparseableOutputYieldsNoVersion(string output) =>
        Assert.Null(NodeRequirement.Parse(output));
}
