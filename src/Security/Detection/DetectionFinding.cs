using Application.Detection;

namespace Security.Detection;

/// <summary>A rule hit. <see cref="Key"/> identifies the subject for deduplication (for example a device or actor).</summary>
public sealed record DetectionFinding(
    string Key,
    string Title,
    string Description,
    string? DeviceId,
    IReadOnlyList<DetectionEvidence> Evidence,
    DetectionSeverity? SeverityOverride = null);
