namespace Domain.Network;

/// <summary>
/// Entirely simulated industrial network. No sockets are opened; transmissions are modelled with
/// deterministic latency and loss so training scenarios are reproducible.
/// </summary>
public sealed class VirtualNetwork
{
    private readonly Dictionary<string, CommunicationLink> _links;
    private readonly Dictionary<string, NetworkSegment> _segments;
    private readonly IRandomSource _random;

    public VirtualNetwork(NetworkDefinition definition, IRandomSource random)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _segments = definition.Segments.ToDictionary(s => s.Id, StringComparer.Ordinal);
        if (definition.Links.Any(link => !_segments.ContainsKey(link.SegmentId)))
            throw new ArgumentException("Every link must belong to a known segment.", nameof(definition));
        _links = definition.Links.ToDictionary(l => l.Id, l => new CommunicationLink(l), StringComparer.Ordinal);
    }

    public NetworkDefinition Definition { get; }
    public long BlockedAttempts { get; private set; }
    public IReadOnlyCollection<CommunicationLink> Links => _links.Values;

    public CommunicationLink? FindLink(string id) => _links.GetValueOrDefault(id);

    public TransmissionResult Transmit(string linkId) =>
        _links.TryGetValue(linkId, out var link)
            ? link.Transmit(_random)
            : new TransmissionResult(linkId, TransmissionOutcome.LinkDown, 0);

    public bool IsConduitAllowed(NetworkZone from, NetworkZone to) =>
        from == to || Definition.AllowedConduits.Any(rule => rule.From == from && rule.To == to);

    /// <summary>Evaluates a connection attempt between nodes in two segments and counts denied attempts.</summary>
    public BlockedConnectionAttempt? EvaluateConnection(string sourceNode, string sourceSegmentId, string targetNode, string targetSegmentId)
    {
        if (!_segments.TryGetValue(sourceSegmentId, out var source) || !_segments.TryGetValue(targetSegmentId, out var target))
        {
            BlockedAttempts++;
            return new(sourceNode, NetworkZone.Enterprise, targetNode, NetworkZone.Field, "Unknown network segment");
        }

        if (IsConduitAllowed(source.Zone, target.Zone))
            return null;

        BlockedAttempts++;
        return new(sourceNode, source.Zone, targetNode, target.Zone,
            $"No conduit from {source.Zone} zone to {target.Zone} zone");
    }

    public int RestoreDefaults() => _links.Values.Count(link => link.RestoreDefaults());

    public NetworkSnapshot Snapshot() => new(Definition.Segments,
        _links.Values.Select(l => l.Snapshot()).ToArray(), Definition.AllowedConduits, BlockedAttempts);
}
