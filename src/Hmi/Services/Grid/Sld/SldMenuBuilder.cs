namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Context-menu commands per element kind and role. Viewers only get read-only items; switching and tagging require
/// <c>CanOperate</c>. Tags are only offered on switch/line/transformer/load (the taggable equipment).
/// </summary>
public static class SldMenuBuilder
{
    public static SldContextMenu For(SldElement element, SldElementState? state, bool canOperate, double clientX, double clientY)
    {
        var items = new List<SldMenuItem>();
        switch (element.Kind)
        {
            case SldElementKind.Switch:
                if (canOperate)
                {
                    items.Add(new SldMenuItem("Open…", GridSldActionKind.SelectOpen, state?.Closed != false));
                    items.Add(new SldMenuItem("Close…", GridSldActionKind.SelectClose, state?.Closed != true));
                }
                if (canOperate)
                    items.Add(new SldMenuItem("Place tag…", GridSldActionKind.PlaceTag));
                items.Add(new SldMenuItem("Trend", GridSldActionKind.ShowTrend));
                items.Add(new SldMenuItem("Details", GridSldActionKind.ShowDetails));
                break;
            case SldElementKind.Line:
            case SldElementKind.Transformer:
            case SldElementKind.Load:
                items.Add(new SldMenuItem("Control window", GridSldActionKind.OpenControl));
                items.Add(new SldMenuItem("Trend", GridSldActionKind.ShowTrend));
                if (canOperate)
                    items.Add(new SldMenuItem("Place tag…", GridSldActionKind.PlaceTag));
                items.Add(new SldMenuItem("Details", GridSldActionKind.ShowDetails));
                break;
            default:
                items.Add(new SldMenuItem("Details", GridSldActionKind.ShowDetails));
                items.Add(new SldMenuItem("Trend", GridSldActionKind.ShowTrend));
                break;
        }
        return new SldContextMenu(element.Id, clientX, clientY, items);
    }
}
