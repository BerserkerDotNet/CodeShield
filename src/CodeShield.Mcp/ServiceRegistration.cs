using CodeShield.Abstractions;
using CodeShield.Core.Evaluation;
using CodeShield.Core.Git;
using CodeShield.Core.Rules;
using CodeShield.Core.Scope;
using CodeShield.Git;
using CodeShield.TypeScript;
using Microsoft.Extensions.DependencyInjection;

namespace CodeShield.Mcp;

public static class ServiceRegistration
{
    public static IServiceCollection AddCodeShield(this IServiceCollection services, IEnumerable<ILanguageAnalyzer> analyzers)
    {
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IGitClient>(provider => new GitCliClient(provider.GetRequiredService<IProcessRunner>()));
        services.AddSingleton(new SupportedFiles(analyzers));
        services.AddSingleton<ScopeDiscovery>();
        services.AddSingleton<JobPlanner>();
        services.AddSingleton<ISnapshotMaterializer, SnapshotMaterializer>();
        services.AddSingleton<SnapshotCollector>();
        services.AddSingleton<IRule, MethodMetricRule>();
        services.AddSingleton<IRule, CohesionRule>();
        services.AddSingleton<IRule, FileLengthRule>();
        services.AddSingleton<IRule, DuplicationRule>();
        services.AddSingleton<IRule, ModuleCycleRule>();
        services.AddSingleton<IRule, FileCycleRule>();
        services.AddSingleton<IRule, BoundaryRule>();
        services.AddSingleton<IRule, MainSequenceRule>();
        services.AddSingleton(CorrectnessCatalog.Pinned);
        services.AddSingleton<IRule, CorrectnessDiagnosticsRule>();
        services.AddSingleton<RuleRunner>();
        services.AddSingleton<EvaluationStore>();
        services.AddSingleton<EvaluationService>();
        services.AddSingleton<RuleDetailsService>();
        return services;
    }

    public static IEnumerable<ILanguageAnalyzer> DefaultAnalyzers() =>
        [new TypeScriptAnalyzer(), new CodeShield.CSharp.CSharpLanguageAnalyzer()];
}

