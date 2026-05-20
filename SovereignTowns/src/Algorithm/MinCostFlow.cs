using System;
using System.Collections.Generic;

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

        // Successive shortest path with Johnson-style potentials.
        // Public edges have cost >= 0 and reverse edges start at capacity 0, so the first
        // Dijkstra with h == 0 is exact. After each augmentation we promote h[v] by the
        // (reduced) shortest-path distance; this keeps every residual edge non-negatively
        // weighted in reduced cost, which is the SSP invariant that lets Dijkstra replace
        // SPFA. Unreachable nodes from the very first iteration stay unreachable forever
        // (reverse edges only appear on nodes that already lay on some augmenting path),
        // so their stale h stays out of every reduced-cost computation.
        var potentials = new Dictionary<int, long>(_graph.Count);
        foreach (var id in _graph.Keys)
            potentials[id] = 0L;

        var distance = new Dictionary<int, long>(_graph.Count);
        var parentNode = new Dictionary<int, int>(_graph.Count);
        var parentEdgeIndex = new Dictionary<int, int>(_graph.Count);
        var heap = new MinHeap();

        int totalFlow = 0;
        long totalCost = 0L;

        while (TryFindShortestPath(source, sink, potentials, distance, parentNode, parentEdgeIndex, heap))
        {
            int augment = int.MaxValue;
            for (int v = sink; v != source; v = parentNode[v])
            {
                var edge = _graph[parentNode[v]][parentEdgeIndex[v]];
                if (edge.Capacity < augment) augment = edge.Capacity;
            }

            if (augment <= 0 || augment == int.MaxValue) break;

            for (int v = sink; v != source; v = parentNode[v])
            {
                var edge = _graph[parentNode[v]][parentEdgeIndex[v]];
                edge.Capacity -= augment;
                _graph[edge.To][edge.ReverseIndex].Capacity += augment;
                totalCost += (long)augment * edge.Cost;
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

        int boundedCost = totalCost > int.MaxValue ? int.MaxValue : (int)totalCost;
        return new MinCostFlowResult(totalFlow, boundedCost, flows);
    }

    private bool TryFindShortestPath(
        int source,
        int sink,
        Dictionary<int, long> potentials,
        Dictionary<int, long> distance,
        Dictionary<int, int> parentNode,
        Dictionary<int, int> parentEdgeIndex,
        MinHeap heap)
    {
        const long Inf = long.MaxValue / 4;

        distance.Clear();
        parentNode.Clear();
        parentEdgeIndex.Clear();
        heap.Clear();

        foreach (var id in _graph.Keys)
            distance[id] = Inf;

        distance[source] = 0L;
        heap.Push(0L, source);

        while (heap.Count > 0)
        {
            heap.Pop(out long d, out int u);
            if (d > distance[u]) continue;
            if (u == sink) break;

            long pu = potentials[u];
            var edges = _graph[u];
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge.Capacity <= 0) continue;

                long pv = potentials[edge.To];
                long reduced = edge.Cost + pu - pv;
                if (reduced < 0) reduced = 0;

                long next = d + reduced;
                if (next >= distance[edge.To]) continue;

                distance[edge.To] = next;
                parentNode[edge.To] = u;
                parentEdgeIndex[edge.To] = i;
                heap.Push(next, edge.To);
            }
        }

        if (!parentNode.ContainsKey(sink)) return false;

        foreach (var kv in distance)
        {
            if (kv.Value < Inf)
                potentials[kv.Key] += kv.Value;
        }

        return true;
    }

    private sealed class MinHeap
    {
        private struct Entry
        {
            public long Priority;
            public int Node;
        }

        private Entry[] _data = new Entry[64];
        public int Count { get; private set; }

        public void Clear() => Count = 0;

        public void Push(long priority, int node)
        {
            if (Count == _data.Length)
                Array.Resize(ref _data, _data.Length * 2);

            int i = Count++;
            _data[i].Priority = priority;
            _data[i].Node = node;

            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_data[parent].Priority <= _data[i].Priority) break;
                var tmp = _data[parent];
                _data[parent] = _data[i];
                _data[i] = tmp;
                i = parent;
            }
        }

        public void Pop(out long priority, out int node)
        {
            var top = _data[0];
            priority = top.Priority;
            node = top.Node;

            Count--;
            if (Count == 0) return;

            _data[0] = _data[Count];
            int i = 0;
            int n = Count;
            while (true)
            {
                int l = 2 * i + 1;
                int r = 2 * i + 2;
                int smallest = i;
                if (l < n && _data[l].Priority < _data[smallest].Priority) smallest = l;
                if (r < n && _data[r].Priority < _data[smallest].Priority) smallest = r;
                if (smallest == i) break;
                var tmp = _data[smallest];
                _data[smallest] = _data[i];
                _data[i] = tmp;
                i = smallest;
            }
        }
    }

    public static bool SelfTest(out string message)
    {
        // Case 1 — original transportation problem; the network exercises a residual-edge
        // undo at the second iteration: greedy first picks 0->1->3->5 with cap 2 cost 1
        // per unit, then second iteration must route through 0->2->4->5.
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

        // Case 2 — classic textbook residual-undo example. Without residual reasoning a
        // greedy SSP would settle at cost 6+1+1=8 routing both units the "wrong" way.
        // Correct MCMF: flow=2 cost=6.
        //   0->1 cap 1 cost 1, 0->2 cap 1 cost 5, 1->2 cap 1 cost 1, 1->3 cap 1 cost 1, 2->3 cap 1 cost 1
        var g2 = new MinCostFlow();
        for (int i = 0; i < 4; i++) g2.AddNode(i);
        g2.AddEdge(0, 1, 1, 1);
        g2.AddEdge(0, 2, 1, 5);
        g2.AddEdge(1, 2, 1, 1);
        g2.AddEdge(1, 3, 1, 1);
        g2.AddEdge(2, 3, 1, 1);

        var r2 = g2.Solve(0, 3);
        // Two augmenting paths under SSP:
        //   path A: 0->1->3 cost 2 (or 0->1->2->3 cost 3 — SSP picks the cheaper, so 0->1->3)
        //   path B: 0->2->3 cost 6
        // Total cost = 2 + 6 = 8. Note: SSP always picks shortest path each iteration; there
        // is no cheaper overall configuration because 0->2 cost 5 forces that 5 to be paid.
        // We verify flow=2 cost=8 — anything else means the algorithm produced a non-optimal
        // multi-path assignment.
        if (r2.TotalFlow != 2 || r2.TotalCost != 8)
        {
            message = $"two-path SSP expected flow=2 cost=8, got flow={r2.TotalFlow} cost={r2.TotalCost}";
            return false;
        }

        // Case 3 — zero-flow case (sink unreachable). Must return flow=0 without throwing
        // and without entering the augmentation loop.
        var g3 = new MinCostFlow();
        for (int i = 0; i < 3; i++) g3.AddNode(i);
        g3.AddEdge(0, 1, 1, 1);
        // no edge into node 2
        var r3 = g3.Solve(0, 2);
        if (r3.TotalFlow != 0 || r3.TotalCost != 0)
        {
            message = $"unreachable-sink expected flow=0 cost=0, got flow={r3.TotalFlow} cost={r3.TotalCost}";
            return false;
        }

        message = "MinCostFlow self-test passed";
        return true;
    }
}
