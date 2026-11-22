namespace Hmi.Services;

/// <summary>A point in P&amp;ID canvas coordinates, returned by the pid.js pointer module.</summary>
public sealed record PidPoint(double X, double Y);
