namespace Protocols.Iec104;

/// <summary>All points of one RTU (one common address), in deterministic IOA order.</summary>
public sealed record Iec104RtuMap(int CommonAddress, string RtuId, string RtuName, IReadOnlyList<Iec104Point> Points)
{
    public Iec104Point? Point(int informationObjectAddress) =>
        Points.FirstOrDefault(p => p.InformationObjectAddress == informationObjectAddress);
}
