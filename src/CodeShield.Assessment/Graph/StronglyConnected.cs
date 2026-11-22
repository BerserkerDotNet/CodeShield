namespace CodeShield.Core.Graph;

/// <summary>Strongly connected components via an iterative Tarjan algorithm.</summary>
public static class StronglyConnected
{
    private sealed class State(IReadOnlyDictionary<string, List<string>> adjacency)
    {
        public Dictionary<string, int> Index { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Low { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OnStack { get; } = new(StringComparer.Ordinal);
        public Stack<string> Stack { get; } = new();
        public List<List<string>> Components { get; } = [];
        public IReadOnlyDictionary<string, List<string>> Adjacency { get; } = adjacency;
    }

    /// <summary>Returns components with more than one node, each sorted ordinally, ordered by first member.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Cycles(IEnumerable<string> nodes, IEnumerable<(string From, string To)> edges)
    {
        var adjacency = nodes.ToDictionary(node => node, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to) in edges.Where(edge => edge.From != edge.To))
        {
            if (adjacency.TryGetValue(from, out var targets) && adjacency.ContainsKey(to))
            {
                targets.Add(to);
            }
        }

        var state = new State(adjacency);
        foreach (var node in adjacency.Keys.Order(StringComparer.Ordinal).Where(node => !state.Index.ContainsKey(node)))
        {
            Visit(state, node);
        }

        return [.. state.Components.Where(component => component.Count > 1)
            .Select(component => (IReadOnlyList<string>)[.. component.Order(StringComparer.Ordinal)])
            .OrderBy(component => component[0], StringComparer.Ordinal)];
    }

    private static void Visit(State state, string root)
    {
        var work = new Stack<(string Node, int Next)>();
        Enter(state, root);
        work.Push((root, 0));
        while (work.Count > 0)
        {
            var (node, next) = work.Pop();
            var targets = state.Adjacency[node];
            if (next < targets.Count)
            {
                work.Push((node, next + 1));
                Descend(state, work, node, targets[next]);
                continue;
            }

            Leave(state, work, node);
        }
    }

    private static void Enter(State state, string node)
    {
        state.Index[node] = state.Low[node] = state.Index.Count;
        state.Stack.Push(node);
        state.OnStack.Add(node);
    }

    private static void Descend(State state, Stack<(string Node, int Next)> work, string node, string target)
    {
        if (state.Index.TryGetValue(target, out var targetIndex))
        {
            if (state.OnStack.Contains(target))
            {
                state.Low[node] = Math.Min(state.Low[node], targetIndex);
            }

            return;
        }

        Enter(state, target);
        work.Push((target, 0));
    }

    private static void Leave(State state, Stack<(string Node, int Next)> work, string node)
    {
        if (work.Count > 0)
        {
            var parent = work.Peek().Node;
            state.Low[parent] = Math.Min(state.Low[parent], state.Low[node]);
        }

        if (state.Low[node] == state.Index[node])
        {
            state.Components.Add(PopComponent(state, node));
        }
    }

    private static List<string> PopComponent(State state, string root)
    {
        var component = new List<string>();
        string member;
        do
        {
            member = state.Stack.Pop();
            state.OnStack.Remove(member);
            component.Add(member);
        }
        while (member != root);
        return component;
    }
}
