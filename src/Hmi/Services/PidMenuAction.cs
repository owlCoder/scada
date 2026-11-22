namespace Hmi.Services;

/// <summary>One entry of the P&amp;ID context menu. <see cref="Action"/> is one of start/stop/auto/manual/position/trend/details.</summary>
public sealed record PidMenuAction(string DeviceId, string Action);
