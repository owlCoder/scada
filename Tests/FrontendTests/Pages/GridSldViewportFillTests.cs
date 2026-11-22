using Domain.Grid.Definition;
using Hmi.Services.Grid.Sld;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridSldViewportFillTests
{
    private static readonly SldBounds Network = new(-10, -90, 620, 620);

    [Theory]
    [InlineData(1500, 700)]
    [InlineData(1400, 800)]
    [InlineData(1920, 1080)]
    [InlineData(900, 1200)]
    [InlineData(1200, 1200)]
    public Task FitKeepsTheContainerAspectAndFillsAtLeast90PercentOfTheLimitingDimension(double containerWidth, double containerHeight)
    {
        var viewport = new SldViewport();
        viewport.SetContainer(containerWidth, containerHeight);
        viewport.Fit(Network);
        Assert.Equal(containerWidth / containerHeight, viewport.Width / viewport.Height, 6);
        // The whole network is contained.
        Assert.True(viewport.X <= Network.X && viewport.Y <= Network.Y);
        Assert.True(viewport.X + viewport.Width >= Network.Right && viewport.Y + viewport.Height >= Network.Bottom);
        // The limiting dimension is filled to at least 90 % — the fit uses the full canvas, no letterbox.
        var fill = Math.Max(Network.Width / viewport.Width, Network.Height / viewport.Height);
        Assert.True(fill >= 0.9, $"fill {fill:0.###} on {containerWidth}x{containerHeight}");
        return Task.CompletedTask;
    }

    [Fact]
    public Task FitOnAWideCanvasFillsTheHeightNotJustTheWidth()
    {
        // 1500x700 canvas, network 2164x908 SVG units: the height is the limiting dimension and must fill >= 90 %.
        var model = SldModelBuilder.Build(DefaultGridDefinition.Create());
        var viewport = new SldViewport();
        viewport.SetContainer(1500, 700);
        viewport.Fit(model.Bounds);
        var fill = Math.Max(model.Bounds.Width / viewport.Width, model.Bounds.Height / viewport.Height);
        Assert.True(fill >= 0.9, $"fill {fill:0.###}");
        Assert.Equal(1500.0 / 700.0, viewport.Width / viewport.Height, 6);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZoomKeepsTheContainerAspectSoTheCanvasIsNeverLetterboxed()
    {
        var viewport = new SldViewport();
        viewport.SetContainer(1500, 700);
        viewport.Fit(Network);
        for (var i = 0; i < 30; i++)
        {
            viewport.ZoomCenter(1.3);
            Assert.Equal(1500.0 / 700.0, viewport.Width / viewport.Height, 6);
        }
        for (var i = 0; i < 60; i++)
        {
            viewport.ZoomCenter(0.7);
            Assert.Equal(1500.0 / 700.0, viewport.Width / viewport.Height, 6);
        }
        return Task.CompletedTask;
    }
}
