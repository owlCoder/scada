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
}
