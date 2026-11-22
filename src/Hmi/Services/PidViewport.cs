using System.Globalization;

namespace Hmi.Services;

/// <summary>
/// Pan/zoom viewport of the water P&amp;ID canvas in SVG units. The view keeps the CONTAINER aspect ratio, so the
/// viewBox always matches the canvas and the diagram fills the whole canvas (no letterboxing) at fit. All state is
/// plain C#; the JS module only reports pointer positions and the container size.
/// </summary>
public sealed class PidViewport
{
    /// <summary>Container size assumed before the JS module reports the real one (tests/prerender).</summary>
    public const double DefaultContainerWidth = 1400;
    public const double DefaultContainerHeight = 800;

    /// <summary>Fit padding around the content (SVG units).</summary>
    public const double FitPadding = 12;

    /// <summary>Fit scale factor: the content fills ~96 % of the limiting dimension.</summary>
    public const double FitFill = 1.04;

    public const double MinViewFactor = 1.0 / 12;
    public const double MaxViewFactor = 3.0;

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; } = 800;
    public double Height { get; private set; } = 600;
    public double ContainerWidth { get; private set; } = DefaultContainerWidth;
    public double ContainerHeight { get; private set; } = DefaultContainerHeight;

    /// <summary>View width at the last fit — the reference for <see cref="ZoomPercent"/> (fit = 100 %).</summary>
    public double FitWidth { get; private set; } = 800;

    /// <summary>View aspect (w/h) — always kept equal to the container aspect.</summary>
    public double Aspect => ContainerWidth <= 0 || ContainerHeight <= 0 ? 1 : ContainerWidth / ContainerHeight;

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

    /// <summary>
    /// Fits the whole content into the container respecting its aspect ratio: the limiting dimension is filled to
    /// ~96 %, so the diagram uses the full canvas width/height at fit.
    /// </summary>
    public bool Fit(double contentWidth, double contentHeight)
    {
        var contentW = contentWidth + 2 * FitPadding;
        var contentH = contentHeight + 2 * FitPadding;
        Width = Math.Max(contentW, contentH * Aspect) * FitFill;
        Height = Width / Aspect;
        FitWidth = Width;
        X = contentWidth / 2 - Width / 2;
        Y = contentHeight / 2 - Height / 2;
        return true;
    }

    /// <summary>Zooms around an optional fixed SVG point (the cursor); the point stays under the cursor.</summary>
    public bool ZoomAt(double factor, double? px, double? py, double contentWidth, double contentHeight)
    {
        var newWidth = Math.Clamp(Width * factor, contentWidth * MinViewFactor, contentWidth * MaxViewFactor);
        var ratio = newWidth / Width;
        var anchorX = px ?? X + Width / 2;
        var anchorY = py ?? Y + Height / 2;
        X = anchorX - (anchorX - X) * ratio;
        Y = anchorY - (anchorY - Y) * ratio;
        Width = newWidth;
        Height = Width / Aspect;
        Clamp(contentWidth, contentHeight);
        return true;
    }

    /// <summary>Pans by a drag delta in SVG units (content follows the pointer).</summary>
    public bool PanBy(double dx, double dy, double contentWidth, double contentHeight)
    {
        X -= dx;
        Y -= dy;
        Clamp(contentWidth, contentHeight);
        return true;
    }

    public bool CenterOn(double centerX, double centerY, double contentWidth, double contentHeight)
    {
        X = centerX - Width / 2;
        Y = centerY - Height / 2;
        Clamp(contentWidth, contentHeight);
        return true;
    }

    public bool Clamp(double contentWidth, double contentHeight)
    {
        X = Math.Clamp(X, -Width / 2, contentWidth - Width / 2);
        Y = Math.Clamp(Y, -Height / 2, contentHeight - Height / 2);
        return true;
    }

    /// <summary>Zoom label: 100 % = fitted view.</summary>
    public double ZoomPercent => FitWidth / Math.Max(Width, 1) * 100;

    private static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
