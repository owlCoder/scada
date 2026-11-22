using System.Globalization;
using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Pan/zoom viewport of the single-line diagram in SVG units. The view keeps the container aspect ratio, so zooming
/// only changes <see cref="Width"/>/<see cref="Height"/> around a fixed point. All state is plain C#; the JS module
/// only reports pointer positions and the container size.
/// </summary>
public sealed class SldViewport
{
    public const double MinWidth = 60;
    public const double MaxWidth = 40000;

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; } = 1200;
    public double Height { get; private set; } = 800;
    public double ContainerWidth { get; private set; } = 1400;
    public double ContainerHeight { get; private set; } = 800;

    /// <summary>Screen pixels per SVG unit — drives the level of detail.</summary>
    public double PixelsPerUnit => ContainerWidth <= 0 ? 1 : ContainerWidth / Width;

    public SldLod Lod => PixelsPerUnit < 1.5 ? SldLod.Low : PixelsPerUnit < 6 ? SldLod.Medium : SldLod.High;

    public string ViewBox => string.Create(CultureInfo.InvariantCulture, $"{N(X)} {N(Y)} {N(Width)} {N(Height)}");

    public bool SetContainer(double width, double height)
    {
        if (width > 0 && height > 0)
        {
            ContainerWidth = width;
            ContainerHeight = height;
        }
        return true;
    }

    public bool Fit(SldBounds bounds)
    {
        // Keep the container aspect ratio and make sure both bounds dimensions fit.
        var aspect = ContainerHeight / ContainerWidth;
        var width = Math.Max(bounds.Width, bounds.Height / aspect) * 1.1;
        Width = Math.Clamp(width, MinWidth, MaxWidth);
        Height = Width * aspect;
        X = bounds.CenterX - Width / 2;
        Y = bounds.CenterY - Height / 2;
        return true;
    }

    /// <summary>Zooms around a fixed SVG point (the cursor): the point stays under the cursor.</summary>
    public bool ZoomAt(DiagramPosition svgPoint, double factor)
    {
        var newWidth = Math.Clamp(Width / factor, MinWidth, MaxWidth);
        var scale = newWidth / Width;
        X = svgPoint.X - (svgPoint.X - X) * scale;
        Y = svgPoint.Y - (svgPoint.Y - Y) * scale;
        Width = newWidth;
        Height = Width * ContainerHeight / ContainerWidth;
        return true;
    }

    public bool ZoomCenter(double factor) => ZoomAt(new DiagramPosition(X + Width / 2, Y + Height / 2), factor);

    /// <summary>Pans by a drag delta in SVG units (content follows the pointer).</summary>
    public bool PanBy(double dxSvg, double dySvg)
    {
        X -= dxSvg;
        Y -= dySvg;
        return true;
    }

    public bool SetCenter(double centerX, double centerY)
    {
        X = centerX - Width / 2;
        Y = centerY - Height / 2;
        return true;
    }

    /// <summary>Minimap navigation: fractions of the network bounds become the new viewport centre.</summary>
    public bool NavigateFraction(double fx, double fy, SldBounds bounds)
    {
        var width = Math.Min(Width, bounds.Width * 1.2);
        if (Math.Abs(width - Width) > 1e-9)
        {
            Width = Math.Clamp(width, MinWidth, MaxWidth);
            Height = Width * ContainerHeight / ContainerWidth;
        }
        SetCenter(bounds.X + Math.Clamp(fx, 0, 1) * bounds.Width, bounds.Y + Math.Clamp(fy, 0, 1) * bounds.Height);
        return true;
    }

    /// <summary>Search/quick-list navigation: centre on an element and zoom to a readable level.</summary>
    public bool ZoomTo(DiagramPosition anchor, SldBounds bounds)
    {
        var width = Math.Clamp(bounds.Width / 3.5, 180, bounds.Width * 1.2);
        Width = Math.Clamp(Math.Min(Width, width), MinWidth, MaxWidth);
        if (Width > bounds.Width * 1.2)
            Width = Math.Clamp(bounds.Width * 1.2, MinWidth, MaxWidth);
        Height = Width * ContainerHeight / ContainerWidth;
        SetCenter(anchor.X, anchor.Y);
        return true;
    }

    public bool ZoomToBounds(SldBounds bounds)
    {
        Fit(bounds);
        return true;
    }

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
