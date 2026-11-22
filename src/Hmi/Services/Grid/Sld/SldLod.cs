namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Level of detail driven by the current zoom (screen pixels per SVG unit): Low shows busbar/feeder ids and key
/// values (bus kV, branch A/%, transformer loading), Medium adds element and substation names (~1.5 px/unit),
/// High shows a collision-free value box for every element including the tap position (~2.5 px/unit).
/// </summary>
public enum SldLod { Low, Medium, High }
