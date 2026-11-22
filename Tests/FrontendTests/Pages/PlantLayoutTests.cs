using Application.Identity;
using Domain.Process.Snapshots;
using FrontendTests.Support;
using Hmi.Services;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PlantLayoutTests
{
    private static PlantSnapshot Plant(HmiTestHost host) => host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());

    [Fact]
    public async Task ParallelPumpRowsAreSpreadFarEnoughApartForLabelAndValue()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var layout = new PlantLayout(Plant(host));
        var p101 = layout.Position("P-101");
        var p103 = layout.Position("P-103");
        Assert.NotEqual(p101.Y, p103.Y); // P-101/P-103 are parallel rows of the T-101 -> T-102 transfer
        // Each row needs its id label above (-36) and its flow value below (+44): the rows must be far enough apart.
        Assert.True(Math.Abs(p103.Y - p101.Y) >= 100,
            $"pump rows {Math.Abs(p103.Y - p101.Y):0.#} apart — labels and values would overlap");
        Assert.Equal(PlantLayout.PumpRowSpacing, Math.Abs(p103.Y - p101.Y), 3);
    }

    [Fact]
    public async Task LabelsNeverIntersectSymbolBoxesOrEachOther()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var plant = Plant(host);
        var layout = new PlantLayout(plant, ["PLC-1", "PLC-2", "RTU-1"]);
        var labels = layout.Labels(PidDetail.Full);
        var placements = layout.PlaceLabels(labels);
        var obstacles = layout.SymbolObstacles();
        Assert.NotEmpty(obstacles);
        Assert.NotEmpty(placements);
        // Every device id and every instrument value is placed (none dropped on the default plant).
        var ids = plant.Tanks.Select(t => t.Id)
            .Concat(plant.Pumps.Select(p => p.Id))
            .Concat(plant.Valves.Select(v => v.Id))
            .Concat(plant.Sensors.Select(s => s.Id + "#v"));
        foreach (var id in ids)
            Assert.True(placements.ContainsKey(id), $"label {id} was dropped");
        var boxes = placements.Join(labels, p => p.Key, l => l.Id, (p, l) => (X: p.Value.X, Y: p.Value.Y, W: l.Width, H: l.Height)).ToArray();
        foreach (var box in boxes)
            foreach (var obstacle in obstacles)
                Assert.False(Intersects(box, obstacle),
                    $"label {box.X:0.#},{box.Y:0.#} {box.W:0.#}x{box.H:0.#} intersects symbol/bell {obstacle.X:0.#},{obstacle.Y:0.#} {obstacle.Width:0.#}x{obstacle.Height:0.#}");
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length; j++)
                Assert.False(Intersects(boxes[i], boxes[j]), $"labels {i} and {j} overlap: {boxes[i]} vs {boxes[j]}");
    }

    [Fact]
    public async Task InstrumentValueTextsSitBesideTheirBubbleNotOnTopOfOtherSymbols()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var layout = new PlantLayout(Plant(host));
        var labels = layout.Labels(PidDetail.Full);
        foreach (var sensorId in new[] { "FT-102", "PT-101", "FT-104", "PT-103", "LT-101", "FT-101" })
        {
            var label = labels.FirstOrDefault(l => l.Id == sensorId + "#v");
            Assert.NotNull(label);
            var (bx, by, _, _) = layout.SensorSlot(sensorId);
            // Beside the bubble (outside its radius), vertically centred on it — not below where pump labels live.
            Assert.True(Math.Abs(label!.CenterX - bx) > PlantLayout.BubbleRadius, $"{sensorId} value not beside the bubble");
            Assert.InRange(label.CenterY - by, -8, 8);
        }
    }

    [Fact]
    public async Task TargetWidthSpreadsTanksPumpsAndValvesAcrossTheCanvas()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var plant = Plant(host);
        var compact = new PlantLayout(plant);
        var wide = new PlantLayout(plant, targetWidth: 1400);
        Assert.True(wide.Width > compact.Width, $"{wide.Width} should exceed {compact.Width}");
        Assert.True(wide.Width >= 1400, $"layout should use the target width, got {wide.Width}");
        var tanks = plant.Tanks.Select(t => wide.Position(t.Id).X).ToArray();
        for (var i = 1; i < tanks.Length; i++)
            Assert.True(tanks[i] - tanks[i - 1] >= PlantLayout.MinTankSpacing, "tanks spread out, not bunched");
        Assert.Equal((1400 - 2 * 70 - PlantLayout.TankWidth) / (plant.Tanks.Count - 1), tanks[1] - tanks[0], 3);
        // Pumps sit between their source and target tanks, so they use the width too.
        var p101 = wide.Position("P-101");
        Assert.InRange(p101.X, wide.Position(plant.Pumps.First(p => p.Id == "P-101").SourceTankId).X + PlantLayout.TankWidth / 2,
            wide.Position(plant.Pumps.First(p => p.Id == "P-101").TargetTankId).X + PlantLayout.TankWidth / 2);
    }

    [Fact]
    public async Task DefaultLayoutIsUnchangedWhenNoTargetWidthIsGiven()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        var plant = Plant(host);
        var layout = new PlantLayout(plant);
        Assert.Equal(PlantLayout.MinTankSpacing, layout.Position(plant.Tanks[1].Id).X - layout.Position(plant.Tanks[0].Id).X, 3);
        Assert.Equal(70, layout.Position(plant.Tanks[0].Id).X, 3);
    }

    private static bool Intersects((double X, double Y, double W, double H) a, LabelObstacle o) =>
        a.X < o.X + o.Width && a.X + a.W > o.X && a.Y < o.Y + o.Height && a.Y + a.H > o.Y;

    private static bool Intersects((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
        a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;
}
