namespace Domain.Control;

/// <summary>
/// Maps a field signal to a controller register. Values are stored as round(value * Scale).
/// <see cref="AutomaticPosition"/> is the position an output without a control loop is held at while the device is in
/// automatic mode, so the controller re-establishes it after a fail-safe or an operator hand-back.
/// </summary>
public sealed record IoPoint(ushort Address, string Tag, string DeviceId, IoSignal Signal, double Scale, double? AutomaticPosition = null);
