namespace CodeShield.Abstractions;

/// <summary>One selected static-correctness rule. The evaluator ID is derived from the analyzer ID.</summary>
public sealed record CorrectnessRule(string Language, string AnalyzerId, string Detects, string OptionsJson = "[]")
{
    public string EvaluatorId => Language == Languages.CSharp
        ? $"correctness.csharp.{AnalyzerId.ToLowerInvariant()}"
        : $"correctness.typescript.{AnalyzerId[(AnalyzerId.LastIndexOf('/') + 1)..]}";
}

/// <summary>Single source of truth for the pinned correctness rule selection.</summary>
public static class CorrectnessCatalog
{
    public static IReadOnlyList<CorrectnessRule> All { get; } =
    [
        new(Languages.CSharp, "CA2000", "Undisposed resources"),
        new(Languages.CSharp, "CA2011", "Recursive property setters"),
        new(Languages.CSharp, "CA2012", "Incorrect ValueTask use"),
        new(Languages.CSharp, "CA2013", "Value-type identity comparisons"),
        new(Languages.CSharp, "CA2014", "stackalloc in loops"),
        new(Languages.CSharp, "CA2016", "Dropped cancellation tokens"),
        new(Languages.CSharp, "CA2017", "Logging argument mismatch"),
        new(Languages.CSharp, "CA2018", "Incorrect byte counts in Buffer.BlockCopy"),
        new(Languages.CSharp, "CA2019", "Thread-static initialization"),
        new(Languages.CSharp, "CA2021", "Incompatible enumerable casts"),
        new(Languages.CSharp, "CA2022", "Unchecked partial stream reads"),
        new(Languages.CSharp, "CA2023", "Invalid log templates"),
        new(Languages.TypeScript, "@typescript-eslint/no-floating-promises",         "Unhandled promises",
                    """[{"allowForKnownSafeCalls":[],"allowForKnownSafePromises":[],"checkThenables":false,"ignoreIIFE":false,"ignoreVoid":false}]"""),
                new(Languages.TypeScript, "@typescript-eslint/no-misused-promises", "Promises used in inappropriate contexts",
                    """[{"checksConditionals":true,"checksSpreads":true,"checksVoidReturn":true}]"""),
                new(Languages.TypeScript, "@typescript-eslint/await-thenable", "Awaiting non-thenables",
                    """[{"allowMixedPromiseArrays":false}]"""),
                new(Languages.TypeScript, "array-callback-return", "Missing array callback returns",
                    """[{"allowImplicit":false,"allowVoid":false,"checkForEach":false}]"""),
        new(Languages.TypeScript, "for-direction", "for loop updates moving in the wrong direction"),
        new(Languages.TypeScript, "no-async-promise-executor", "Async promise executors"),
        new(Languages.TypeScript, "no-constant-binary-expression", "Suspicious constant binary expressions"),
        new(Languages.TypeScript, "no-dupe-else-if", "Duplicate conditions in an if/else-if chain"),
        new(Languages.TypeScript, "no-self-assign", "Self-assignment", """[{"props":true}]"""),
        new(Languages.TypeScript, "no-unsafe-finally", "Control flow in finally that can mask errors"),
        new(Languages.TypeScript, "no-unsafe-optional-chaining",         "Unsafe use of optional chaining",
                    """[{"disallowArithmeticOperators":false}]"""),
                new(Languages.TypeScript, "use-isnan", "Incorrect comparisons with NaN",
                    """[{"enforceForIndexOf":false,"enforceForSwitchCase":true}]"""),
    ];

    public static ICorrectnessSelection Pinned { get; } = new CorrectnessSelection(All);
}

