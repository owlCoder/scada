using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Hmi.Services;
using Xunit;

namespace FrontendTests.Pages;

public sealed class DiagramFitTests
{
    private static (double X, double Y, double W, double H) ViewBox(IRenderedComponent<Diagram> page)
    {
        var parts = page.Find(".pid-canvas").GetAttribute("viewBox")!.Split(' ');
        return (double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task DiagramFitKeepsTheCanvasAspectAndFillsTheLimitingDimension()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Diagram>();
        var view = ViewBox(page);
        // The viewBox aspect equals the (default) container aspect, so preserveAspectRatio never letterboxes.
        Assert.Equal(PidViewport.DefaultContainerWidth / PidViewport.DefaultContainerHeight, view.W / view.H, 3);
        var plant = host.Rig.Engine.Read(w => w.Plant.CaptureSnapshot());
        var controllers = host.Rig.Engine.Read(w => w.Controllers.Select(c => c.Snapshot()).ToArray());
        var layout = new PlantLayout(plant, controllers.Select(c => c.Id).ToArray());
        // The whole P&ID is contained and the limiting dimension is filled to >= 90 % of the canvas.
        Assert.True(view.W >= layout.Width && view.H >= layout.Height, $"view {view.W}x{view.H} smaller than layout {layout.Width}x{layout.Height}");
        var fill = Math.Max(layout.Width / view.W, layout.Height / view.H);
        Assert.True(fill >= 0.9, $"fill {fill:0.###} — the P&ID must use the full canvas at fit");
        Assert.Contains("%", page.Find(".pid-zoom").TextContent);
    }
}
