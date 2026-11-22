namespace Hmi.Services;

/// <summary>
/// One P&amp;ID label to place: the text, its css class and the fixed preferred slot (box centre) next to its
/// symbol. <see cref="Priority"/> decides which label survives when space runs out (value texts outrank id labels).
/// </summary>
public sealed record PidLabel(string Id, string Text, string Css, double CenterX, double CenterY, double Width, double Height, int Priority)
{
    public LabelBoxCandidate ToCandidate() => new(Id, CenterX, CenterY, Width, Height, Priority);
}
