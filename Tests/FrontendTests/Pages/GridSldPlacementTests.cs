using Domain.Grid.Definition;
using FrontendTests.Support;
using Hmi.Services.Grid.Sld;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridSldPlacementTests
{
    [Fact]
    public Task SingleCandidateIsPlacedNextToItsAnchor()
    {
        var placements = SldLabelPlacer.Place([new SldLabelCandidate("a", 100, 100, 60, 18)]);
        var p = Assert.Single(placements);
        Assert.Equal("a", p.Id);
        Assert.InRange(Math.Abs(p.X + 30 - 100), 0, 60);
        Assert.InRange(Math.Abs(p.Y + 9 - 100), 0, 60);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ManyCandidatesAtTheSameAnchorNeverOverlap()
    {
        var candidates = Enumerable.Range(0, 60)
            .Select(i => new SldLabelCandidate($"e{i}", 200, 200, 40 + (i % 7) * 10, 18))
            .ToArray();
        AssertNoOverlap(SldLabelPlacer.Place(candidates), candidates);
        return Task.CompletedTask;
    }

    [Fact]
    public Task CandidatesSpreadOverTheNetworkNeverOverlap()
    {
        var candidates = Enumerable.Range(0, 120)
            .Select(i => new SldLabelCandidate($"e{i}", (i * 37) % 800, (i * 53) % 600, 50 + (i % 5) * 8, 18))
            .ToArray();
        AssertNoOverlap(SldLabelPlacer.Place(candidates), candidates);
        return Task.CompletedTask;
    }

    [Fact]
    public Task BoxSizeGrowsWithTextAndFlag()
    {
        var (w1, h1) = SldLabelPlacer.BoxSize("20.20 kV", null);
        var (w2, _) = SldLabelPlacer.BoxSize("20.20 kV", "U");
        var (w3, _) = SldLabelPlacer.BoxSize("80.0 A · 20 % · tap 0", null);
        Assert.Equal(18, h1);
        Assert.True(w2 > w1);
        Assert.True(w3 > w1);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnitScalePlacesBoxesTighterInSvgUnitsButStillWithoutOverlap()
    {
        // At high zoom the caller shrinks box sizes and slot distances by 1/ppu so boxes keep their screen size.
        var candidates = Enumerable.Range(0, 60)
            .Select(i => new SldLabelCandidate($"e{i}", 200, 200, (40 + (i % 7) * 10) / 2.0, 18 / 2.0))
            .ToArray();
        var placements = SldLabelPlacer.Place(candidates, 0.5);
        AssertNoOverlap(placements, candidates);
        var unscaled = SldLabelPlacer.Place(Enumerable.Range(0, 60)
            .Select(i => new SldLabelCandidate($"e{i}", 200, 200, 40 + (i % 7) * 10, 18)));
        var scaledSpread = placements.Max(p => Math.Max(Math.Abs(p.X - 200), Math.Abs(p.Y - 200)));
        var unscaledSpread = unscaled.Max(p => Math.Max(Math.Abs(p.X - 200), Math.Abs(p.Y - 200)));
        Assert.True(scaledSpread < unscaledSpread, $"{scaledSpread} should be tighter than {unscaledSpread}");
        return Task.CompletedTask;
    }

    private static Task AssertNoOverlap(IReadOnlyList<SldLabelPlacement> placements, IReadOnlyList<SldLabelCandidate> candidates)
    {
        Assert.Equal(candidates.Count, placements.Count);
        var boxes = placements.Join(candidates, p => p.Id, c => c.Id, (p, c) => (p.X, p.Y, c.Width, c.Height)).ToArray();
        for (var i = 0; i < boxes.Length; i++)
            for (var j = i + 1; j < boxes.Length; j++)
            {
                var (ax, ay, aw, ah) = boxes[i];
                var (bx, by, bw, bh) = boxes[j];
                var overlap = ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
                Assert.False(overlap, $"Boxes {i} and {j} overlap: ({ax},{ay},{aw},{ah}) vs ({bx},{by},{bw},{bh})");
            }
        return Task.CompletedTask;
    }
}
