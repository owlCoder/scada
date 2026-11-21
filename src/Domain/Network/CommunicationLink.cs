namespace Domain.Network;

public sealed class CommunicationLink
{
    private double _totalLatency;

    public CommunicationLink(LinkDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        LatencyMilliseconds = definition.BaseLatencyMilliseconds;
        PacketLossPercent = definition.PacketLossPercent;
    }

    public LinkDefinition Definition { get; }
    public string Id => Definition.Id;
    public bool IsDisconnected { get; private set; }
    public double LatencyMilliseconds { get; private set; }
    public double PacketLossPercent { get; private set; }
    public long Sent { get; private set; }
    public long Delivered { get; private set; }
    public long Lost { get; private set; }
    public long TimedOut { get; private set; }

    public LinkState State => IsDisconnected
        ? LinkState.Down
        : PacketLossPercent >= 10 || LatencyMilliseconds > Definition.TimeoutMilliseconds * 0.5
            ? LinkState.Degraded
            : LinkState.Up;

    public TransmissionResult Transmit(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Sent++;
        if (IsDisconnected)
        {
            Lost++;
            return new(Id, TransmissionOutcome.LinkDown, 0);
        }

        if (random.NextDouble() * 100 < PacketLossPercent)
        {
            Lost++;
            return new(Id, TransmissionOutcome.Lost, 0);
        }

        var jitter = (random.NextDouble() - 0.5) * 0.2 * LatencyMilliseconds;
        var latency = Math.Max(0, LatencyMilliseconds + jitter);
        if (latency > Definition.TimeoutMilliseconds)
        {
            TimedOut++;
            return new(Id, TransmissionOutcome.TimedOut, latency);
        }

        Delivered++;
        _totalLatency += latency;
        return new(Id, TransmissionOutcome.Delivered, latency);
    }

    public bool Disconnect()
    {
        var changed = !IsDisconnected;
        IsDisconnected = true;
        return changed;
    }

    public bool Reconnect()
    {
        var changed = IsDisconnected;
        IsDisconnected = false;
        return changed;
    }

    public bool SetImpairment(double latencyMilliseconds, double packetLossPercent)
    {
        if (!double.IsFinite(latencyMilliseconds) || latencyMilliseconds < 0 ||
            !double.IsFinite(packetLossPercent) || packetLossPercent is < 0 or > 100)
            return false;

        LatencyMilliseconds = latencyMilliseconds;
        PacketLossPercent = packetLossPercent;
        return true;
    }

    /// <summary>Returns true when the link was disconnected or impaired and has been restored.</summary>
    public bool RestoreDefaults()
    {
        var changed = IsDisconnected || LatencyMilliseconds != Definition.BaseLatencyMilliseconds ||
            PacketLossPercent != Definition.PacketLossPercent;
        IsDisconnected = false;
        LatencyMilliseconds = Definition.BaseLatencyMilliseconds;
        PacketLossPercent = Definition.PacketLossPercent;
        return changed;
    }

    public LinkSnapshot Snapshot() => new(Id, Definition.Name, Definition.SourceNode, Definition.TargetNode,
        Definition.SegmentId, State, LatencyMilliseconds, PacketLossPercent, Definition.TimeoutMilliseconds,
        Sent, Delivered, Lost, TimedOut,
        Delivered == 0 ? 0 : Math.Round(_totalLatency / Delivered, 2),
        Sent == 0 ? 100 : Math.Round(100.0 * Delivered / Sent, 2));
}
