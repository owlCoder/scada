using Application.Events;
using Application.Grid;
using Domain.Grid.Definition;

namespace Protocols.Iec104;

/// <summary>
/// Holds the active <see cref="Iec104AddressMap"/>. The map is built once from <see cref="IGridView.Definition"/> at startup
/// and rebuilt whenever the definition instance changes — which is what <c>IGridRuntime.ActivateVersionAsync</c> does when
/// it loads a new grid definition version and then raises <see cref="GridStepEventKind.DefinitionActivated"/>. Consumers
/// (ASDU handler, grid monitor, map endpoint) always read <see cref="Current"/> or call <see cref="Sync"/>, so commands and
/// spontaneous data never run against a stale map that could address the wrong equipment.
/// </summary>
public sealed class Iec104AddressMapProvider
{
    private readonly Lock _sync = new();
    private readonly IEventPublisher _events;
    private readonly TimeProvider _time;
    private GridDefinition _definition;
    private Iec104AddressMap _current;

    public Iec104AddressMapProvider(IGridView view, IEventPublisher events, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(view);
        _events = events;
        _time = time;
        _definition = view.Definition;
        _current = Iec104AddressMap.Build(_definition);
    }

    /// <summary>The map that is currently in force. Never null; rebuilt instances replace it atomically.</summary>
    public Iec104AddressMap Current
    {
        get { lock (_sync) return _current; }
    }

    /// <summary>
    /// Returns the current map, rebuilding it first when <paramref name="definition"/> is a different instance than the one
    /// the current map was built from (definition identity change after a version activation). The rebuild is audited.
    /// </summary>
    public Iec104AddressMap Sync(GridDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string? audit = null;
        lock (_sync)
        {
            if (ReferenceEquals(definition, _definition))
                return _current;
            var previousName = _definition.Name;
            var previousPoints = _current.Rtus.Sum(r => r.Points.Count);
            _definition = definition;
            _current = Iec104AddressMap.Build(definition);
            var points = _current.Rtus.Sum(r => r.Points.Count);
            audit = $"IEC 104 address map rebuilt for grid definition '{definition.Name}' (previous '{previousName}'): " +
                $"{_current.Rtus.Count} RTUs, {points} points (was {previousPoints}). Existing connections are re-synchronised with M_EI_NA_1 COT 4.";
        }

        _events.Publish(SecurityEvent.Create(_time.GetUtcNow(), EventCategories.Protocol, EventSeverity.High, "iec104",
            audit, "iec104-server"));
        return Current;
    }
}
