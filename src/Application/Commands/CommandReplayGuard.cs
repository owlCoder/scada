namespace Application.Commands;

/// <summary>Tracks the highest accepted sequence number per actor and rejects anything not strictly newer.</summary>
public sealed class CommandReplayGuard
{
    private const int MaxTrackedActors = 10_000;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, long> _lastSequence = new(StringComparer.Ordinal);

    public bool TryAccept(string actor, long sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(actor) || sequenceNumber <= 0)
            return false;

        lock (_sync)
        {
            if (_lastSequence.TryGetValue(actor, out var last) && sequenceNumber <= last)
                return false;

            if (_lastSequence.Count >= MaxTrackedActors && !_lastSequence.ContainsKey(actor))
                _lastSequence.Remove(_lastSequence.Keys.First());
            _lastSequence[actor] = sequenceNumber;
            return true;
        }
    }

    public long LastSequence(string actor)
    {
        lock (_sync)
            return _lastSequence.GetValueOrDefault(actor);
    }
}
