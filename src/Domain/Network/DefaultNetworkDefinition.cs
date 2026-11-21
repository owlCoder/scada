namespace Domain.Network;

public static class DefaultNetworkDefinition
{
    public static NetworkDefinition Create() => new(
        [
            new("ENT", "Enterprise LAN", NetworkZone.Enterprise),
            new("DMZ", "Industrial DMZ", NetworkZone.Demilitarized),
            new("SUP", "Supervisory LAN", NetworkZone.Supervisory),
            new("CTL", "Control LAN", NetworkZone.Control),
            new("WAN", "Telemetry Radio Link", NetworkZone.Control),
            new("FLD-1", "Transfer Station Field Bus", NetworkZone.Field),
            new("FLD-2", "Treatment Field Bus", NetworkZone.Field),
            new("FLD-3", "Reservoir Field Bus", NetworkZone.Field)
        ],
        [
            new("L-SCADA-PLC1", "SCADA server to PLC-1", "SCADA", "PLC-1", "CTL", 8, 0, 250),
            new("L-SCADA-PLC2", "SCADA server to PLC-2", "SCADA", "PLC-2", "CTL", 8, 0, 250),
            new("L-SCADA-RTU1", "SCADA server to RTU-1", "SCADA", "RTU-1", "WAN", 60, 0, 500),
            new("L-PLC1-IO", "PLC-1 to remote I/O", "PLC-1", "IO-1", "FLD-1", 2, 0, 100),
            new("L-PLC2-IO", "PLC-2 to remote I/O", "PLC-2", "IO-2", "FLD-2", 2, 0, 100),
            new("L-RTU1-IO", "RTU-1 to field instruments", "RTU-1", "IO-3", "FLD-3", 4, 0, 150)
        ],
        [
            new(NetworkZone.Enterprise, NetworkZone.Demilitarized),
            new(NetworkZone.Supervisory, NetworkZone.Demilitarized),
            new(NetworkZone.Supervisory, NetworkZone.Control),
            new(NetworkZone.Control, NetworkZone.Field)
        ]);
}
