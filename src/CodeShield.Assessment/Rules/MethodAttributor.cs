using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Rules;

/// <summary>Labels a changed method's finding by matching its code unit in the baseline file (through Git's rename map).</summary>
public sealed class MethodAttributor(ChangeMap map, BaselineContext baseline)
{
    public string Classify(string file, MethodFact method, Func<MethodFact, int> measure, int limit)
    {
        var baselineFile = baseline.FileOf(file);
        if (baselineFile is null)
        {
            return Attribution.Introduced;
        }

        var baselineId = baseline.UnitIdOf(method.UnitId);
        var matches = baselineFile.Methods.Where(candidate => candidate.UnitId == baselineId).ToList();
        return matches.Count switch
        {
            0 => IsEntirelyNew(file, method.Span) ? Attribution.Introduced : Attribution.Ambiguous,
            1 => measure(matches[0]) > limit ? Attribution.PreExisting : Attribution.Introduced,
            _ => Attribution.Ambiguous,
        };
    }

    private bool IsEntirelyNew(string file, SourceSpan span) =>
        Enumerable.Range(span.StartLine, span.EndLine - span.StartLine + 1).All(line => map.MapLineToBaseline(file, line) is null);
}

