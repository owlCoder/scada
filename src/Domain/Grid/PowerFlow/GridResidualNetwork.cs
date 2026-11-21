using System.Numerics;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;

namespace Domain.Grid.PowerFlow;

/// <summary>Zero-sequence equivalent: lines Z0=3Z1; transformer windings block transfer between voltage levels.</summary>
internal sealed class GridResidualNetwork
{
    private readonly GridWorld _world;
    private readonly IReadOnlyList<GridBranchModel> _branches;
    private readonly IReadOnlyDictionary<string, int> _indices;
    private readonly Dictionary<int, Complex> _neutrals = [];
    private readonly Dictionary<string, Complex> _windingNeutrals = [];
    private readonly Dictionary<int, Complex[]> _columns = [];
    private readonly int[] _nodes;
    private readonly Complex[,] _matrix;
    private readonly int _count;

    private GridResidualNetwork(GridWorld world, IReadOnlyList<GridBranchModel> branches,
        IReadOnlyDictionary<string, int> indices, int count)
    {
        _world = world; _branches = branches; _indices = indices; _count = count;
        var y = new Complex[count, count];
        var adjacency = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        foreach (var b in branches.Where(b => b.Kind == GridBranchKind.Line))
        {
            var admittance = 1 / (3 * b.Impedance);
            y[b.From, b.From] += admittance; y[b.To, b.To] += admittance;
            y[b.From, b.To] -= admittance; y[b.To, b.From] -= admittance;
            adjacency[b.From].Add(b.To); adjacency[b.To].Add(b.From);
        }
        foreach (var t in world.Definition.Transformers)
        {
            var b = branches.First(b => b.Id == t.Id);
            var kv = world.Definition.Buses[indices[t.LvBusId]].NominalVoltageKv;
            // 20 kV: 300 A neutral resistor (38.49 ohm). LV: solid neutral with transformer Z0=Z1.
            var rn = kv >= 1 ? kv * 1000 / (Math.Sqrt(3) * 300) : 0;
            var z = b.Impedance + 3 * rn / (kv * kv / GridPowerFlowSolver.BaseMva);
            _windingNeutrals[t.Id] = 1 / z;
            _neutrals[b.To] = _neutrals.GetValueOrDefault(b.To) + 1 / z;
        }
        foreach (var s in world.Definition.Sources)
        {
            var kv = world.Definition.Buses[indices[s.BusId]].NominalVoltageKv;
            var rn = kv * 1000 / (Math.Sqrt(3) * 300);
            var node = indices[s.BusId];
            _neutrals[node] = _neutrals.GetValueOrDefault(node) + 1 / (3 * rn / (kv * kv / GridPowerFlowSolver.BaseMva));
        }
        var reached = new HashSet<int>(_neutrals.Keys);
        var queue = new Queue<int>(reached);
        while (queue.TryDequeue(out var node))
            foreach (var other in adjacency[node]) if (reached.Add(other)) queue.Enqueue(other);
        _nodes = reached.Order().ToArray();
        foreach (var (node, admittance) in _neutrals) y[node, node] += admittance;
        _matrix = new Complex[_nodes.Length, _nodes.Length];
        for (var i = 0; i < _nodes.Length; i++)
            for (var j = 0; j < _nodes.Length; j++) _matrix[i, j] = y[_nodes[i], _nodes[j]];
    }

    public static GridResidualNetwork Create(GridWorld world, IReadOnlyList<GridBranchModel> branches,
        IReadOnlyDictionary<string, int> indices, int count) => new(world, branches, indices, count);

    public Complex Transfer(int to, int from) => Array.IndexOf(_nodes, from) < 0 && to == from
        ? new Complex(1e12, 0) : Column(from)[to];

    public Dictionary<string, double> TerminalCurrents(Complex[] injection)
    {
        var drop = new Complex[_count];
        for (var n = 0; n < injection.Length; n++)
        {
            if (injection[n] == Complex.Zero) continue;
            var column = Column(n);
            for (var i = 0; i < drop.Length; i++) drop[i] += column[i] * injection[n];
        }
        var result = new Dictionary<string, double>();
        foreach (var l in _world.Definition.Lines)
            foreach (var bus in new[] { l.FromBusId, l.ToBusId })
            {
                var index = _indices[bus];
                var current = _branches.Where(b => b.Id == l.Id && (b.From == index || b.To == index))
                    .Select(b => ((drop[b.From] - drop[b.To]) / (3 * b.Impedance)).Magnitude).DefaultIfEmpty(0).Max();
                result[$"{l.Id}:{bus}"] = Amps(current, bus);
            }
        foreach (var t in _world.Definition.Transformers)
        {
            var b = _branches.First(b => b.Id == t.Id);
            result[$"{t.Id}:{t.LvBusId}"] = _world.TerminalConnected(t.Id, t.LvBusId)
                ? Amps((drop[b.To] * _windingNeutrals[t.Id]).Magnitude, t.LvBusId) : 0;
            result[$"{t.Id}:{t.HvBusId}"] = 0;
        }
        return result;
    }

    private double Amps(double pu, string bus) => pu * GridPowerFlowSolver.BaseMva * 1000 /
        (Math.Sqrt(3) * _world.Definition.Buses[_indices[bus]].NominalVoltageKv);

    private Complex[] Column(int node)
    {
        if (_columns.TryGetValue(node, out var cached)) return cached;
        var rhs = new Complex[_nodes.Length];
        var index = Array.IndexOf(_nodes, node);
        // No neutral path: zero conducted residual (capacitive earth current is outside this equivalent).
        if (index < 0) return new Complex[_count];
        rhs[index] = 1;
        var solved = GridLinearAlgebra.Solve(_matrix, rhs) ?? throw new InvalidOperationException("Invalid neutral network.");
        var column = new Complex[_count];
        for (var i = 0; i < _nodes.Length; i++) column[_nodes[i]] = solved[i];
        _columns[node] = column;
        return column;
    }
}
