namespace Hmi.Services;

/// <summary>
/// One label box to place: the preferred centre plus the box size, both in diagram units. Candidates with a higher
/// <see cref="Priority"/> are placed first, so they win the slots next to their element when space runs out.
/// </summary>
public sealed record LabelBoxCandidate(string Id, double CenterX, double CenterY, double Width, double Height, int Priority = 0);
