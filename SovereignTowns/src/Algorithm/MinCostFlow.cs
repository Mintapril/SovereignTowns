using System;
using System.Collections.Generic;
using System.Linq;

namespace SovereignTowns.Algorithm;

public sealed class MinCostFlowResult
{
    public MinCostFlowResult(int totalFlow, int totalCost, Dictionary<(int From, int To), int> edgeFlows)
    {
        TotalFlow = totalFlow;
        TotalCost = totalCost;
        EdgeFlows = edgeFlows ?? new Dictionary<(int From, int To), int>();
    }

    public int TotalFlow { get; }
    public int TotalCost { get; }
    public IReadOnlyDictionary<(int From, int To), int> EdgeFlows { get; }
}

public sealed class MinCostFlow
{
    private sealed class Edge
    {
        public Edge(int from, int to, int reverseIndex, int capacity, int cost, bool original)
        {
            From = from;
            To = to;
            ReverseIndex = reverseIndex;
            Capacity = capacity;
            Cost = cost;
            OriginalCapacity = capacity;
            Original = original;
        }

        public int From { get; }
        public int To { get; }
        public int ReverseIndex { get; }
        public int Capacity { get; set; }
        public int Cost { get; }
        public int OriginalCapacity { get; }
        public bool Original { get; }
    }

    private readonly Dictionary<int, List<Edge>> _graph = new Dictionary<int, List<Edge>>();
    private readonly List<Edge> _originalEdges = new List<Edge>();

    public void AddNode(int id)
    {
        if (!_graph.ContainsKey(id))
            _graph[id] = new List<Edge>();
    }

    public void AddEdge(int from, int to, int capacity, int cost)
    {
        if (capacity <= 0) return;
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost), "Public edge cost must be non-negative.");

        AddNode(from);
        AddNode(to);

        var forward = new Edge(from, to, _graph[to].Count, capacity, cost, original: true);
        var reverse = new Edge(to, from, _graph[from].Count, 0, -cost, original: false);
        _graph[from].Add(forward);
        _graph[to].Add(reverse);
        _originalEdges.Add(forward);
    }

    public MinCostFlowResult Solve(int source, int sink)
    {
        if (!_graph.ContainsKey(source) || !_graph.ContainsKey(sink) || source == sink)
            return new MinCostFlowResult(0, 0, new Dictionary<(int From, int To), int>());

        int totalFlow = 0;
        int totalCost = 0;

        while (TryFindShortestPath(source, sink, out var parentNode, out var parentEdgeIndex))
        {
            int augment = int.MaxValue;
            for (int v = sink; v != source; v = parentNode[v])
            {
                var edge = _graph[parentNode[v]][parentEdgeIndex[v]];
                augment = Math.Min(augment, edge.Capacity);
            }

            if (augment <= 0 || augment == int.MaxValue) break;

            for (int v = sink; v != source; v = parentNode[v])
            {
                var edge = _graph[parentNode[v]][parentEdgeIndex[v]];
                edge.Capacity -= augment;
                _graph[edge.To][edge.ReverseIndex].Capacity += augment;
                totalCost += augment * edge.Cost;
            }

            totalFlow += augment;
        }

        var flows = new Dictionary<(int From, int To), int>();
        foreach (var edge in _originalEdges)
        {
            int flow = edge.OriginalCapacity - edge.Capacity;
            if (flow <= 0) continue;
            var key = (edge.From, edge.To);
            flows[key] = flows.TryGetValue(key, out var existing) ? existing + flow : flow;
        }

        return new MinCostFlowResult(totalFlow, totalCost, flows);
    }

    private bool TryFindShortestPath(
        int source,
        int sink,
        out Dictionary<int, int> parentNode,
        out Dictionary<int, int> parentEdgeIndex)
    {
        const int Inf = int.MaxValue / 4;
        parentNode = new Dictionary<int, int>();
        parentEdgeIndex = new Dictionary<int, int>();

        var distance = _graph.Keys.ToDictionary(id => id, _ => Inf);
        var inQueue = _graph.Keys.ToDictionary(id => id, _ => false);
        var queue = new Queue<int>();

        distance[source] = 0;
        queue.Enqueue(source);
        inQueue[source] = true;

        while (queue.Count > 0)
        {
            int u = queue.Dequeue();
            inQueue[u] = false;

            var edges = _graph[u];
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge.Capacity <= 0) continue;
                int nextDistance = distance[u] + edge.Cost;
                if (nextDistance >= distance[edge.To]) continue;

                distance[edge.To] = nextDistance;
                parentNode[edge.To] = u;
                parentEdgeIndex[edge.To] = i;

                if (!inQueue[edge.To])
                {
                    queue.Enqueue(edge.To);
                    inQueue[edge.To] = true;
                }
            }
        }

        return parentNode.ContainsKey(sink);
    }

    public static bool SelfTest(out string message)
    {
        var graph = new MinCostFlow();
        graph.AddNode(0);
        graph.AddNode(1);
        graph.AddNode(2);
        graph.AddNode(3);
        graph.AddNode(4);
        graph.AddNode(5);
        graph.AddEdge(0, 1, 2, 0);
        graph.AddEdge(0, 2, 1, 0);
        graph.AddEdge(1, 3, 2, 1);
        graph.AddEdge(1, 4, 2, 5);
        graph.AddEdge(2, 3, 1, 2);
        graph.AddEdge(2, 4, 1, 1);
        graph.AddEdge(3, 5, 2, 0);
        graph.AddEdge(4, 5, 1, 0);

        var result = graph.Solve(0, 5);
        if (result.TotalFlow != 3 || result.TotalCost != 3
            || !result.EdgeFlows.TryGetValue((1, 3), out var ax) || ax != 2
            || !result.EdgeFlows.TryGetValue((2, 4), out var by) || by != 1)
        {
            message = $"transportation expected flow=3 cost=3, got flow={result.TotalFlow} cost={result.TotalCost}";
            return false;
        }

        message = "MinCostFlow self-test passed";
        return true;
    }
}
