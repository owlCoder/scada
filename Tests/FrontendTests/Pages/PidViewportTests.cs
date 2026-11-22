using Hmi.Services;
using Xunit;

namespace FrontendTests.Pages;

public sealed class PidViewportTests
{
    [Theory]
    [InlineData(1500, 700)]
    [InlineData(1400, 800)]
    [InlineData(1920, 1080)]
    [InlineData(900, 1200)]
    public Task FitIsAspectAwareContainsTheContentAndFillsAtLeast90PercentOfTheLimitingDimension(double containerWidth, double containerHeight)
    {
        var viewport = new PidViewport();
        viewport.SetContainer(containerWidth, containerHeight);
        viewport.Fit(780, 640);
        Assert.Equal(containerWidth / containerHeight, viewport.Width / viewport.Height, 6); // viewBox aspect = canvas aspect
        // Content fully contained (with the fit padding).
        Assert.True(viewport.X <= -PidViewport.FitPadding + 1e-6 && viewport.Y <= -PidViewport.FitPadding + 1e-6);
        Assert.True(viewport.X + viewport.Width >= 780 + PidViewport.FitPadding - 1e-6
            && viewport.Y + viewport.Height >= 640 + PidViewport.FitPadding - 1e-6);
        // The P&ID uses the full canvas: the limiting dimension is filled to >= 90 % (no left-only 60 % letterbox).
        var fill = Math.Max(780 / viewport.Width, 640 / viewport.Height);
        Assert.True(fill >= 0.9, $"fill {fill:0.###} on {containerWidth}x{containerHeight}");
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZoomAtKeepsTheCursorPointFixedAndTheContainerAspect()
    {
        var viewport = new PidViewport();
        viewport.SetContainer(1500, 700);
        viewport.Fit(780, 640);
        var cursor = (X: 300.0, Y: 200.0);
        var fx = (cursor.X - viewport.X) / viewport.Width;
        var fy = (cursor.Y - viewport.Y) / viewport.Height;
        viewport.ZoomAt(0.8, cursor.X, cursor.Y, 780, 640);
        Assert.Equal(fx, (cursor.X - viewport.X) / viewport.Width, 6);
        Assert.Equal(fy, (cursor.Y - viewport.Y) / viewport.Height, 6);
        Assert.Equal(1500.0 / 700.0, viewport.Width / viewport.Height, 6);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ZoomIsClampedToTheContentAndPanStaysInsideIt()
    {
        var viewport = new PidViewport();
        viewport.SetContainer(1500, 700);
        viewport.Fit(780, 640);
        for (var i = 0; i < 100; i++)
            viewport.ZoomAt(0.5, null, null, 780, 640); // factor < 1 zooms in
        Assert.Equal(780 * PidViewport.MinViewFactor, viewport.Width, 3);
        for (var i = 0; i < 100; i++)
            viewport.ZoomAt(2.0, null, null, 780, 640); // factor > 1 zooms out
        Assert.Equal(780 * PidViewport.MaxViewFactor, viewport.Width, 3);
        viewport.Fit(780, 640);
        viewport.PanBy(100000, 100000, 780, 640);
        Assert.True(viewport.X <= 780 - viewport.Width / 2 + 1e-6 && viewport.X >= -viewport.Width / 2 - 1e-6);
        Assert.True(viewport.Y <= 640 - viewport.Height / 2 + 1e-6 && viewport.Y >= -viewport.Height / 2 - 1e-6);
        Assert.Equal(100, viewport.ZoomPercent, 3); // fit = 100 %
        viewport.ZoomAt(0.5, null, null, 780, 640);
        Assert.Equal(200, viewport.ZoomPercent, 3); // zoomed in 2x = 200 %
        return Task.CompletedTask;
    }
}
