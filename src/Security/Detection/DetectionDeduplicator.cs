namespace Security.Detection;

/// <summary>Suppresses repeated findings with the same fingerprint inside the rule cooldown.</summary>
public sealed class DetectionDeduplicator
{
    private const int MaxEntries = 10_000;
    private readonly Dictionary<string, DateTimeOffset> _lastEmitted = new(StringComparer.Ordinal);

    public bool ShouldEmit(string fingerprint, DateTimeOffset now, TimeSpan cooldown)
    {
        if (_lastEmitted.TryGetValue(fingerprint, out var last) && now - last < cooldown)
            return false;

        if (_lastEmitted.Count >= MaxEntries)
        {
            foreach (var stale in _lastEmitted.Where(p => now - p.Value > TimeSpan.FromHours(1)).Select(p => p.Key).ToArray())
                _lastEmitted.Remove(stale);
            if (_lastEmitted.Count >= MaxEntries)
                _lastEmitted.Clear();
        }

        _lastEmitted[fingerprint] = now;
        return true;
    }

    public int Clear()
    {
        var count = _lastEmitted.Count;
        _lastEmitted.Clear();
        return count;
    }
}
