namespace Domain.Grid.Definition;

/// <summary>
/// Station RTU that measures and controls the listed elements. <see cref="CommonAddress"/> is the IEC 60870-5-104 ASDU
/// address; <see cref="LinkId"/> is the virtual network link to the control centre (may be null when not modelled).
/// </summary>
public sealed record GridRtuDefinition(
    string Id,
    string Name,
    int CommonAddress,
    string? LinkId,
    IReadOnlyList<string> ElementIds);
