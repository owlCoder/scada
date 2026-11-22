using Domain.Grid.Definition;
using Hmi.Services.Grid.Sld;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridSldViewportTests
{
    private static readonly SldBounds Network = new(-10, -90, 620, 620);

    private static SldViewport Viewport(double containerWidth = 1400, double containerHeight = 800)
    {
        var viewport = new SldViewport();
        viewport.SetContainer(containerWidth, containerHeight);
        return viewport;
    }

    [Fact]
    public Task FitContainsTheWholeNetworkAndKeepsAspect()
    {
        var v = Viewport();
        v.Fit(Network);
        Assert.True(v.X <= Network.X && v.Y <= Network.Y);
        Assert.True(v.X + v.Width >= Network.Right && v.Y + v.Height >= Network.Bottom);
        Assert.Equal(1400.0 / 800.0, v.Width / v.Height, 3);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZoomAtKeepsTheCursorPointFixed()
    {
        var v = Viewport();
        v.Fit(Network);
        var cursor = new DiagramPosition(300, 200);
        var fxBefore = (cursor.X - v.X) / v.Width;
        var fyBefore = (cursor.Y - v.Y) / v.Height;
        v.ZoomAt(cursor, 2.0);
        Assert.Equal(fxBefore, (cursor.X - v.X) / v.Width, 6);
        Assert.Equal(fyBefore, (cursor.Y - v.Y) / v.Height, 6);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZoomIsClamped()
    {
        var v = Viewport();
        v.Fit(Network);
        for (var i = 0; i < 200; i++)
            v.ZoomCenter(2.0);
        Assert.Equal(SldViewport.MinWidth, v.Width);
        for (var i = 0; i < 200; i++)
            v.ZoomCenter(0.5);
        Assert.Equal(SldViewport.MaxWidth, v.Width);
        return Task.CompletedTask;
    }

    [Fact]
    public Task PanMovesTheViewOppositeToTheDrag()
    {
        var v = Viewport();
        v.Fit(Network);
        var (x, y) = (v.X, v.Y);
        v.PanBy(40, 30);
        Assert.Equal(x - 40, v.X, 6);
        Assert.Equal(y - 30, v.Y, 6);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MinimapFractionNavigatesToTheBoundsPoint()
    {
        var v = Viewport();
        v.Fit(Network);
        v.NavigateFraction(0.5, 0.5, Network);
        Assert.Equal(Network.CenterX, v.X + v.Width / 2, 6);
        Assert.Equal(Network.CenterY, v.Y + v.Height / 2, 6);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZoomToElementCentresAndZoomsIn()
    {
        var v = Viewport();
        v.Fit(Network);
        v.ZoomTo(new DiagramPosition(400, 440), Network);
        Assert.Equal(400, v.X + v.Width / 2, 6);
        Assert.Equal(440, v.Y + v.Height / 2, 6);
        Assert.True(v.Width < Network.Width * 1.2);
        return Task.CompletedTask;
    }

    [Fact]
    public Task LodFollowsPixelsPerUnit()
    {
        var v = Viewport();
        v.Fit(Network);
        Assert.Equal(SldLod.Low, v.Lod); // ~1.17 px/unit after fit
        v.ZoomCenter(2.0);
        Assert.Equal(SldLod.Medium, v.Lod); // ~2.35 px/unit
        v.ZoomCenter(4.0);
        Assert.Equal(SldLod.High, v.Lod); // ~9.4 px/unit
        return Task.CompletedTask;
    }

    [Fact]
    public Task LodThresholdsAreNamesAt15AndValueBoxesAt25PixelsPerUnit()
    {
        var v = new SldViewport();
        v.SetContainer(1500, 900);
        Assert.Equal(1.25, v.PixelsPerUnit, 6); // default width 1200
        Assert.Equal(SldLod.Low, v.Lod);         // fit on a 1520 px canvas is about 1.0 px/unit: ids + key values
        v.ZoomCenter(1.5 / 1.25);
        Assert.Equal(1.5, v.PixelsPerUnit, 6);
        Assert.Equal(SldLod.Medium, v.Lod);      // ~1.5x: names appear
        v.ZoomCenter(2.5 / 1.5);
        Assert.Equal(2.5, v.PixelsPerUnit, 6);
        Assert.Equal(SldLod.High, v.Lod);        // ~2.5x: full value boxes
        v.ZoomCenter(1.4 / 2.5);
        Assert.Equal(1.4, v.PixelsPerUnit, 6);
        Assert.Equal(SldLod.Low, v.Lod);
        return Task.CompletedTask;
    }

    [Fact]
    public Task CounterScalesKeepTextLegibleAndSymbolsCapped()
    {
        var v = new SldViewport();
        v.SetContainer(1500, 900); // ppu 1.25
        Assert.Equal(1.25, v.PixelsPerUnit, 6);
        Assert.Equal(1, v.SymbolScale, 6);            // below the cap the symbol scales with the zoom
        Assert.Equal(1 / 1.25, v.FixedScale, 6);     // value boxes keep a fixed screen size
        // 12 px design text stays within the 11-16 px on-screen window at any zoom.
        Assert.Equal(Math.Clamp(12 * 1.25, SldViewport.MinTextPx, SldViewport.MaxTextPx), 12 * v.PixelsPerUnit * v.TextScale(12), 6);
        v.ZoomCenter(0.25); // ppu 0.3125 -> 12 px design text would be 3.75 px on screen
        Assert.Equal(SldViewport.MinTextPx, 12 * v.PixelsPerUnit * v.TextScale(12), 6); // clamped to 11 px
        v.ZoomCenter(16.0); // ppu 5 -> text would be 60 px on screen
        Assert.Equal(SldViewport.MaxTextPx, 12 * v.PixelsPerUnit * v.TextScale(12), 6); // clamped to 16 px
        Assert.Equal(2.5 / 5.0, v.SymbolScale, 6);   // symbol screen size capped at the 2.5x zoom level
        return Task.CompletedTask;
    }
}
