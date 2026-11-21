namespace Domain.Control;

/// <summary>
/// Register image of a controller. Forced values override the live image until released, mirroring the
/// force tables of real PLCs.
/// </summary>
public sealed class ControllerRegisterBank
{
    private readonly ushort[] _input = new ushort[ControllerRegisterMap.Size];
    private readonly ushort[] _holding = new ushort[ControllerRegisterMap.Size];
    private readonly Dictionary<(RegisterArea Area, ushort Address), ushort> _forced = [];

    public IReadOnlyDictionary<(RegisterArea Area, ushort Address), ushort> Forced => _forced;

    public static bool IsValidAddress(int address) => address is >= 0 and < ControllerRegisterMap.Size;

    public ushort Read(RegisterArea area, ushort address)
    {
        if (!IsValidAddress(address))
            throw new ArgumentOutOfRangeException(nameof(address));

        return _forced.TryGetValue((area, address), out var forced) ? forced : Raw(area)[address];
    }

    public bool Write(RegisterArea area, ushort address, ushort value)
    {
        if (!IsValidAddress(address))
            return false;

        Raw(area)[address] = value;
        return true;
    }

    public bool Force(RegisterArea area, ushort address, ushort value)
    {
        if (!IsValidAddress(address))
            return false;

        _forced[(area, address)] = value;
        return true;
    }

    public bool Release(RegisterArea area, ushort address) => _forced.Remove((area, address));

    public int ReleaseAll()
    {
        var count = _forced.Count;
        _forced.Clear();
        return count;
    }

    public bool IsForced(RegisterArea area, ushort address) => _forced.ContainsKey((area, address));

    private ushort[] Raw(RegisterArea area) => area == RegisterArea.Input ? _input : _holding;
}
