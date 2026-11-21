namespace Domain.Control;

/// <summary>Maps a field signal to a controller register. Values are stored as round(value * Scale).</summary>
public sealed record IoPoint(ushort Address, string Tag, string DeviceId, IoSignal Signal, double Scale);
