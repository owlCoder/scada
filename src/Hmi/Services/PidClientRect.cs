namespace Hmi.Services;

/// <summary>Client rectangle reported by the pid.js size module.</summary>
public sealed record PidClientRect(double Left, double Top, double Width, double Height);
