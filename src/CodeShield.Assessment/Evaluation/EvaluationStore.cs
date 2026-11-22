using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Evaluation;

public sealed record StoredEvaluation(
    string EvaluationId,
    Ruleset Ruleset,
    IReadOnlyList<Rules.RuleFinding> Findings);

/// <summary>In-memory only: evaluation details are lost when the server stops.</summary>
public sealed class EvaluationStore
{
    private const int Capacity = 100;
    private readonly Dictionary<string, StoredEvaluation> _items = [];
    private readonly Queue<string> _order = new();
    private readonly Lock _gate = new();

    public string Add(Ruleset ruleset, IReadOnlyList<Rules.RuleFinding> findings)
    {
        var id = "eval_" + Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            _items[id] = new StoredEvaluation(id, ruleset, findings);
            _order.Enqueue(id);
            while (_order.Count > Capacity)
            {
                _items.Remove(_order.Dequeue());
            }
        }

        return id;
    }

    public StoredEvaluation? Find(string id)
    {
        lock (_gate)
        {
            return _items.GetValueOrDefault(id);
        }
    }
}
