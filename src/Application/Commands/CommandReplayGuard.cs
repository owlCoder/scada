namespace Application.Commands;

/// <summary>
/// Tracks the highest accepted sequence number per actor and rejects anything not strictly newer. When the tracking
/// limit is reached the least recently active actor is forgotten, never an active one.
/// </summary>
public sealed class CommandReplayGuard(int maxTrackedActors = 10_000)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, (long Sequence, long Touched)> _lastSequence = new(StringComparer.Ordinal);
    private long _clock;

    public int MaxTrackedActors { get; } = maxTrackedActors > 0 ? maxTrackedActors : throw new ArgumentOutOfRangeException(nameof(maxTrackedActors));

    public bool TryAccept(string actor, long sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(actor) || sequenceNumber <= 0)
            return false;

        lock (_sync)
        {
            if (_lastSequence.TryGetValue(actor, out var last) && sequenceNumber <= last.Sequence)
                return false;

            if (_lastSequence.Count >= MaxTrackedActors && !_lastSequence.ContainsKey(actor))
                _lastSequence.Remove(_lastSequence.MinBy(e => e.Value.Touched).Key);
            _lastSequence[actor] = (sequenceNumber, ++_clock);
            return true;
        }
    }

    public long LastSequence(string actor)
    {
        lock (_sync)
            return _lastSequence.TryGetValue(actor, out var last) ? last.Sequence : 0;
    }
}
