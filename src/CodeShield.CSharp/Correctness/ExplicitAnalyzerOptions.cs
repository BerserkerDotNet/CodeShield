using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeShield.CSharp.Correctness;

/// <summary>Fixed analyzer options; repository .editorconfig, .globalconfig and rulesets are never consulted.</summary>
internal sealed class ExplicitAnalyzerOptions : AnalyzerConfigOptionsProvider
{
    private static readonly ImmutableDictionary<string, string> Values = ImmutableDictionary<string, string>.Empty
        .WithComparers(StringComparer.OrdinalIgnoreCase)
        .Add("dotnet_code_quality.dispose_analysis_kind", "NonExceptionPaths")
        .Add("dotnet_code_quality.dispose_ownership_transfer_at_constructor", "false")
        .Add("dotnet_code_quality.dispose_ownership_transfer_at_method_call", "false")
        .Add("dotnet_code_quality.interprocedural_analysis_kind", "ContextSensitive")
        .Add("dotnet_code_quality.max_interprocedural_method_call_chain", "3")
        .Add("dotnet_code_quality.max_interprocedural_lambda_or_local_function_call_chain", "3");

    private static readonly FixedOptions Shared = new(Values);

    public override AnalyzerConfigOptions GlobalOptions => Shared;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Shared;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Shared;

    private sealed class FixedOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);

        public override IEnumerable<string> Keys => values.Keys;
    }
}
