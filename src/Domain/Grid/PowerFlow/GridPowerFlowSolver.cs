using System.Numerics;
using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Profiles;
using Domain.Grid.Snapshots;

namespace Domain.Grid.PowerFlow;

/// <summary>Polar Newton-Raphson on a 100 MVA base, with one ideal slack behind each source impedance.</summary>
public sealed class GridPowerFlowSolver
{
    public const double BaseMva = 100;
    public const double Tolerance = 1e-6;
    public const int MaxIterations = 30;

    public GridPowerFlowResult Solve(GridWorld world)
    {
        var d = world.Definition;
        var indices = d.Buses.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var branches = new List<GridBranchModel>();
        var faults = new List<(string Id, int Node, bool Earth)>();
        var next = indices.Count;
        foreach (var l in d.Lines.Where(l => world.Connected(l.Id)))
        {
            var zb = Math.Pow(d.Buses[indices[l.FromBusId]].NominalVoltageKv, 2) / BaseMva;
            var z = new Complex(l.ResistanceOhmPerKm, l.ReactanceOhmPerKm) * l.LengthKm / zb;
            var shunt = new Complex(0, l.SusceptanceMicroSiemensPerKm * 1e-6 * l.LengthKm * zb / 2);
            if (world.Faults.GetValueOrDefault(l.Id) is GridEquipmentFault.ThreePhaseShortCircuit or GridEquipmentFault.EarthFault)
            {
                var node = next++;
                faults.Add((l.Id, node, world.Faults[l.Id] == GridEquipmentFault.EarthFault));
                branches.Add(new(l.Id, indices[l.FromBusId], node, z / 2, 1, shunt / 2, shunt / 2, GridBranchKind.Line, l.RatedCurrentA));
                branches.Add(new(l.Id, node, indices[l.ToBusId], z / 2, 1, shunt / 2, shunt / 2, GridBranchKind.Line, l.RatedCurrentA));
            }
            else branches.Add(new(l.Id, indices[l.FromBusId], indices[l.ToBusId], z, 1, shunt, shunt, GridBranchKind.Line, l.RatedCurrentA));
        }
        foreach (var t in d.Transformers.Where(t => world.Connected(t.Id)))
        {
            var r = t.LoadLossKw / (1000 * t.RatedPowerMva);
            var z = t.ShortCircuitVoltagePercent / 100;
            var factor = BaseMva / t.RatedPowerMva * Math.Pow(t.LvRatedKv / d.Buses[indices[t.LvBusId]].NominalVoltageKv, 2);
            var impedance = new Complex(r, Math.Sqrt(z * z - r * r)) * factor;
            var nominal = (t.HvRatedKv / t.LvRatedKv) /
                (d.Buses[indices[t.HvBusId]].NominalVoltageKv / d.Buses[indices[t.LvBusId]].NominalVoltageKv);
            var tap = nominal / (1 + (world.Taps[t.Id] - t.TapNeutral) * t.TapStepPercent / 100);
            branches.Add(new(t.Id, indices[t.HvBusId], indices[t.LvBusId], impedance, tap,
                new(t.NoLoadLossKw / (1000 * BaseMva), 0), Complex.Zero, GridBranchKind.Transformer, t.RatedPowerMva));
        }
        var slack = new Dictionary<int, Complex>();
        var sources = new List<GridBranchModel>();
        foreach (var s in d.Sources)
        {
            var node = next++;
            slack[node] = new(s.VoltageSetpointPu, 0);
            // X/R = 10 at the stiff external grid.
            var magnitude = BaseMva / s.ShortCircuitPowerMva;
            var z = new Complex(1, 10) * magnitude / Math.Sqrt(101);
            var branch = new GridBranchModel(s.Id, node, indices[s.BusId], z, 1, Complex.Zero, Complex.Zero, GridBranchKind.Line, 0);
            branches.Add(branch); sources.Add(branch);
        }
        foreach (var b in d.Buses)
            if (world.Faults.GetValueOrDefault(b.Id) is GridEquipmentFault.ThreePhaseShortCircuit or GridEquipmentFault.EarthFault)
                faults.Add((b.Id, indices[b.Id], world.Faults[b.Id] == GridEquipmentFault.EarthFault));
        var adjacency = Enumerable.Range(0, next).Select(_ => new List<int>()).ToArray();
        foreach (var b in branches) { adjacency[b.From].Add(b.To); adjacency[b.To].Add(b.From); }
        var energized = new HashSet<int>(slack.Keys);
        var queue = new Queue<int>(slack.Keys);
        while (queue.TryDequeue(out var node))
            foreach (var neighbour in adjacency[node]) if (energized.Add(neighbour)) queue.Enqueue(neighbour);
        var unknown = energized.Where(i => !slack.ContainsKey(i)).Order().ToArray();
        var y = new Complex[next, next];
        foreach (var b in branches)
        {
            y[b.From, b.From] += b.Yff; y[b.From, b.To] += b.Yft;
            y[b.To, b.From] += b.Yft; y[b.To, b.To] += b.Ytt;
        }
        foreach (var c in d.Capacitors.Where(c => world.Connected(c.Id)))
            y[indices[c.BusId], indices[c.BusId]] += new Complex(0, c.RatedPowerMvar * world.CapacitorSteps[c.Id] / c.Steps / BaseMva);
        var voltage = Enumerable.Range(0, next).Select(i => energized.Contains(i) ? Complex.One : Complex.Zero).ToArray();
        foreach (var (id, value) in slack) voltage[id] = value;
        var powers = new Complex[next];
        foreach (var l in d.Loads.Where(l => world.Connected(l.Id) && !world.ShedLoads.Contains(l.Id)))
        {
            var p = world.Demand[l.Id] / BaseMva;
            powers[indices[l.BusId]] -= new Complex(p, p * Math.Tan(Math.Acos(l.PowerFactor)));
        }
        var generation = new double[next];
        foreach (var g in d.Generators.Where(g => world.Connected(g.Id))) generation[indices[g.BusId]] += world.Generation[g.Id] / BaseMva;
        var m = unknown.Length;
        var iterations = 0;
        var converged = false;
        for (; iterations <= MaxIterations; iterations++)
        {
            var mismatch = Residual(voltage);
            if (mismatch.All(x => Math.Abs(x) < Tolerance)) { converged = true; break; }
            if (iterations == MaxIterations) break;
            var jacobian = new double[m * 2, m * 2];
            const double epsilon = 1e-5;
            for (var column = 0; column < m * 2; column++)
            {
                var index = unknown[column % m];
                var saved = voltage[index];
                voltage[index] = column < m ? Complex.FromPolarCoordinates(saved.Magnitude, saved.Phase + epsilon)
                    : Complex.FromPolarCoordinates(saved.Magnitude + epsilon, saved.Phase);
                var perturbed = Residual(voltage);
                voltage[index] = saved;
                for (var row = 0; row < m * 2; row++) jacobian[row, column] = (perturbed[row] - mismatch[row]) / epsilon;
            }
            var delta = GridLinearAlgebra.Solve(jacobian, mismatch.Select(x => -x).ToArray());
            if (delta is null || delta.Any(x => !double.IsFinite(x))) break;
            var max = delta.Skip(m).Select(Math.Abs).DefaultIfEmpty(0).Max();
            var damping = max > 0.2 ? 0.2 / max : 1;
            for (var i = 0; i < m; i++)
            {
                var v = voltage[unknown[i]];
                voltage[unknown[i]] = Complex.FromPolarCoordinates(Math.Clamp(v.Magnitude + damping * delta[i + m], 0.05, 1.6),
                    v.Phase + damping * delta[i]);
            }
        }
        var earthCurrents = new Dictionary<string, double>();
        if (converged && faults.Any(f => energized.Contains(f.Node)))
        {
            var active = faults.Where(f => energized.Contains(f.Node)).ToArray();
            var reduced = new Complex[m, m];
            for (var i = 0; i < m; i++) for (var j = 0; j < m; j++) reduced[i, j] = y[unknown[i], unknown[j]];
            var columns = new List<Complex[]>();
            foreach (var fault in active)
            {
                var rhs = new Complex[m]; rhs[Array.IndexOf(unknown, fault.Node)] = 1;
                columns.Add(GridLinearAlgebra.Solve(reduced, rhs) ?? new Complex[m]);
            }
            var zFault = new Complex[active.Length, active.Length];
            for (var i = 0; i < active.Length; i++)
                for (var j = 0; j < active.Length; j++)
                {
                    zFault[i, j] = columns[j][Array.IndexOf(unknown, active[i].Node)];
                    // Without sequence data, approximate an earth fault with Z0=Z1=Z2 (residual equivalent).
                    if (i == j && active[i].Earth) zFault[i, j] *= 3;
                }
            var currents = GridLinearAlgebra.Solve(zFault, active.Select(f => voltage[f.Node]).ToArray());
            if (currents is not null)
            {
                var earthDrop = new Complex[next];
                for (var i = 0; i < m; i++)
                    for (var f = 0; f < active.Length; f++)
                    {
                        var drop = columns[f][i] * currents[f];
                        voltage[unknown[i]] -= drop;
                        if (active[f].Earth) earthDrop[unknown[i]] += drop;
                    }
                foreach (var b in branches.Where(b => !sources.Contains(b)))
                {
                    var bus = d.Lines.FirstOrDefault(l => l.Id == b.Id)?.FromBusId ?? d.Transformers.First(t => t.Id == b.Id).HvBusId;
                    var kv = d.Buses[indices[bus]].NominalVoltageKv;
                    var current = b.FromCurrent(earthDrop).Magnitude * BaseMva * 1000 / (Math.Sqrt(3) * kv);
                    earthCurrents[b.Id] = Math.Max(earthCurrents.GetValueOrDefault(b.Id), current);
                }
            }
        }
        var import = sources.Sum(b => (voltage[b.From] * Complex.Conjugate(b.FromCurrent(voltage))).Real) * BaseMva;
        var sourceLoss = sources.Sum(b => (voltage[b.From] * Complex.Conjugate(b.FromCurrent(voltage))
            + voltage[b.To] * Complex.Conjugate(b.ToCurrent(voltage))).Real) * BaseMva;
        return new(converged, iterations, indices.ToDictionary(x => x.Key, x => voltage[x.Value]),
            branches.Except(sources).ToArray(), voltage, indices, import, sourceLoss, earthCurrents);

        double[] Residual(Complex[] v)
        {
            var result = new double[m * 2];
            for (var i = 0; i < m; i++)
            {
                var n = unknown[i];
                var current = Complex.Zero;
                for (var j = 0; j < next; j++) current += y[n, j] * v[j];
                var calculated = v[n] * Complex.Conjugate(current);
                var specified = powers[n] * GridProfiles.VoltageFactor(v[n].Magnitude) + new Complex(generation[n], 0);
                result[i] = specified.Real - calculated.Real;
                result[i + m] = specified.Imaginary - calculated.Imaginary;
            }
            return result;
        }
    }
}
