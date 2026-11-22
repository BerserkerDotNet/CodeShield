using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Duplication;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Exact duplicated token sequences where at least one occurrence is new or changed.</summary>
public sealed class DuplicationRule : IRule
{
    private readonly record struct Occurrence(int File, int Start, int End);

    public IReadOnlyList<string> Ids => [RuleIds.Duplication];

    public RuleOutput Evaluate(RuleInput input)
    {
        var output = new RuleOutput();
        var tokenFailures = input.GraphFailures.Where(failure => failure.RuleId == RuleIds.Duplication).ToList();
        if (tokenFailures.Count > 0)
        {
            output.Coverage.Add(new CoverageEntry(RuleIds.Duplication, GraphRuleBase.RepositoryContext, CoverageStatus.Failed,
                [.. tokenFailures.Select(failure => failure with { ContextId = GraphRuleBase.RepositoryContext })]));
            return output;
        }

        var files = TokenFiles(input);
        var matches = new DuplicateFinder(files, input.Map.IsLineChanged).Find();
        var finder = matches.Count;
        output.Findings.AddRange(Group(input, files, matches).Select(group => ToFinding(input, files, group)));
        output.Coverage.Add(new CoverageEntry(RuleIds.Duplication, GraphRuleBase.RepositoryContext,
            files.Count == 0 && finder == 0 ? CoverageStatus.NotApplicable : CoverageStatus.Complete, []));
        return output;
    }

    private static List<TokenFile> TokenFiles(RuleInput input)
    {
        var interned = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(Token token) =>
            interned.TryGetValue(token.Kind + "\0" + token.Text, out var id) ? id : interned[token.Kind + "\0" + token.Text] = interned.Count;
        return [.. input.Tokens.Values.SelectMany(list => list).OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => new TokenFile(file.Path, file.Tokens, Intern))];
    }

    private static List<List<Occurrence>> Group(RuleInput input, List<TokenFile> files, IReadOnlyList<TokenMatch> matches)
    {
        var occurrences = matches.SelectMany(match => new[]
        {
            new Occurrence(match.FileA, match.StartA, match.StartA + match.Length),
            new Occurrence(match.FileB, match.StartB, match.StartB + match.Length),
        }).Distinct().ToList();
        var parent = Enumerable.Range(0, occurrences.Count).ToArray();
        var position = occurrences.Select((item, index) => (item, index)).ToDictionary(pair => pair.item, pair => pair.index);
        foreach (var match in matches)
        {
            Union(parent, position[new Occurrence(match.FileA, match.StartA, match.StartA + match.Length)],
                position[new Occurrence(match.FileB, match.StartB, match.StartB + match.Length)]);
        }

        UnionOverlaps(parent, occurrences);
        return [.. occurrences.Select((item, index) => (item, root: Find(parent, index)))
            .GroupBy(pair => pair.root).Select(group => group.Select(pair => pair.item).ToList())];
    }

    private static void UnionOverlaps(int[] parent, List<Occurrence> occurrences)
    {
        foreach (var byFile in occurrences.Select((item, index) => (item, index)).GroupBy(pair => pair.item.File))
        {
            var ordered = byFile.OrderBy(pair => pair.item.Start).ToList();
            foreach (var i in Enumerable.Range(1, Math.Max(0, ordered.Count - 1)).Where(i => ordered[i].item.Start < ordered[i - 1].item.End))
            {
                Union(parent, ordered[i].index, ordered[i - 1].index);
            }
        }
    }

    private static int Find(int[] parent, int node)
    {
        while (parent[node] != node)
        {
            parent[node] = parent[parent[node]];
            node = parent[node];
        }

        return node;
    }

    private static void Union(int[] parent, int a, int b) => parent[Find(parent, a)] = Find(parent, b);

    private static RuleFinding ToFinding(RuleInput input, List<TokenFile> files, List<Occurrence> group)
    {
        var rule = input.Ruleset.Get(RuleIds.Duplication);
        var ordered = group.OrderBy(item => files[item.File].Path, StringComparer.Ordinal).ThenBy(item => item.Start).ToList();
        var locations = ordered.Select(item => ToLocation(files, item)).ToList();
        var longest = ordered.MaxBy(item => item.End - item.Start);
        return FindingFactory.Create(rule, new FindingSpec(
            GraphRuleBase.RepositoryContext, Attribution.Introduced, true, locations,
            longest.End - longest.Start, $"{ordered.Count} occurrences; longest match {longest.End - longest.Start} tokens",
            [Ev.Item("duplicateGroup", ("occurrences", locations), ("longestTokens", longest.End - longest.Start))]));
    }

    private static FindingLocation ToLocation(List<TokenFile> files, Occurrence item)
    {
        var tokens = files[item.File].Tokens;
        var first = tokens[item.Start];
        var last = tokens[item.End - 1];
        return new FindingLocation(files[item.File].Path, first.Line, first.Column, last.EndLine, last.EndColumn);
    }
}
