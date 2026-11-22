namespace Hmi.Services.Grid.Sld;

/// <summary>Voltage-level CSS class for an element (colour carries the nominal voltage level only).</summary>
public static class SldVoltage
{
    public static string Class(double kv) => kv >= 100 ? "v-110" : kv >= 30 ? "v-35" : kv >= 15 ? "v-20" : kv >= 5 ? "v-10" : "v-04";
}
