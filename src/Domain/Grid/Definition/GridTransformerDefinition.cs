namespace Domain.Grid.Definition;

/// <summary>
/// Two-winding transformer. The tap changer sits on the HV winding; positive taps raise the LV voltage.
/// When <see cref="AvrTargetLvKv"/> is set and the tap changer is in automatic mode, the voltage regulator steps the tap
/// after the LV voltage has stayed outside the deadband for <see cref="AvrDelaySeconds"/>.
/// </summary>
public sealed record GridTransformerDefinition(
    string Id,
    string Name,
    string HvBusId,
    string LvBusId,
    double RatedPowerMva,
    double HvRatedKv,
    double LvRatedKv,
    double ShortCircuitVoltagePercent,
    double LoadLossKw,
    double NoLoadLossKw,
    bool HasOnLoadTapChanger,
    int TapMin,
    int TapMax,
    int TapNeutral,
    int InitialTap,
    double TapStepPercent,
    double? AvrTargetLvKv,
    double AvrDeadbandPercent,
    double AvrDelaySeconds,
    string? CatalogType,
    DiagramPosition Position);
