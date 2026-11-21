using Application.Diagnostics;
using System.Diagnostics;
using Application.Detection;
using Application.Events;
using Application.Incidents;
using Application.Realtime;
using Application.Simulation;
using Domain.Incidents;
using Domain.Process.Snapshots;
using Microsoft.Extensions.Logging;

namespace Security.Detection;

/// <summary>
/// Evaluates every enabled rule after each simulation step, deduplicates findings, stores detections,
/// publishes detection events and raises or enriches correlated incidents. Independent of HTTP.
/// </summary>
public sealed class DetectionEngine : ISimulationObserver
{
    private readonly IDetectionRule[] _rules;
    private readonly Dictionary<string, DetectionRuleSettings> _settings;
    private readonly Dictionary<string, MetricsAccumulator> _metrics;
    private readonly DetectionDeduplicator _deduplicator = new();
    private readonly RecentEventBuffer _events;
    private readonly IDetectionStore _store;
    private readonly IncidentService _incidents;
    private readonly IEventPublisher _publisher;
    private readonly IRealtimePublisher _realtime;
    private readonly IActiveScenarioProvider _scenarios;
    private readonly SimulationEngine _engine;
    private readonly ILogger<DetectionEngine> _logger;
    private readonly Lock _sync = new();
    private PlantSnapshot? _previous;
    private Guid? _scope;
    private DateTimeOffset _scopeStart = DateTimeOffset.MinValue;

    public DetectionEngine(
        IEnumerable<IDetectionRule> rules,
        RecentEventBuffer events,
        IDetectionStore store,
        IncidentService incidents,
        IEventPublisher publisher,
        IRealtimePublisher realtime,
        IActiveScenarioProvider scenarios,
        SimulationEngine engine,
        ILogger<DetectionEngine> logger)
    {
        _rules = rules.ToArray();
        if (_rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != _rules.Length)
            throw new ArgumentException("Detection rule identifiers must be unique.", nameof(rules));
        _settings = _rules.ToDictionary(r => r.Id, r => r.DefaultSettings, StringComparer.Ordinal);
        _metrics = _rules.ToDictionary(r => r.Id, _ => new MetricsAccumulator(), StringComparer.Ordinal);
        _events = events;
        _store = store;
        _incidents = incidents;
        _publisher = publisher;
        _realtime = realtime;
        _scenarios = scenarios;
        _engine = engine;
        _logger = logger;
    }

    public IReadOnlyList<RuleDescriptor> Rules
    {
        get
        {
            lock (_sync)
                return _rules.Select(r => new RuleDescriptor(r.Id, r.Name, r.Description, r.Category, _settings[r.Id])).ToArray();
        }
    }

    public IReadOnlyList<RuleExecutionMetrics> Metrics
    {
        get
        {
            lock (_sync)
                return _rules.Select(r =>
                {
                    var m = _metrics[r.Id];
                    return new RuleExecutionMetrics(r.Id, r.Name, _settings[r.Id].Enabled, m.Evaluations, m.Findings,
                        m.Suppressed, Math.Round(m.TotalMilliseconds, 3), m.LastFindingAt);
                }).ToArray();
        }
    }

    public bool UpdateSettings(DetectionRuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Severity) || settings.WindowSeconds is < 1 or > 86_400 ||
            settings.CooldownSeconds is < 0 or > 86_400 || !double.IsFinite(settings.Threshold) || settings.Threshold < 0)
            return false;

        lock (_sync)
        {
            if (!_settings.ContainsKey(settings.RuleId))
                return false;

            _settings[settings.RuleId] = settings;
            return true;
        }
    }

    public int ResetState()
    {
        lock (_sync)
        {
            _previous = null;
            return _deduplicator.Clear();
        }
    }

    public async ValueTask<int> OnStepAsync(SimulationStepReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var pending = new List<(IDetectionRule Rule, DetectionFinding Finding, DetectionRuleSettings Settings)>();
        lock (_sync)
        {
            // Event windows never reach back past the start of the current scope (a scenario run, or live operation
            // after a run), so events from one run cannot be re-detected and attributed to the next.
            var scope = _scenarios.ActiveRunId;
            if (scope != _scope)
            {
                _scope = scope;
                _scopeStart = report.Timestamp;
                foreach (var rule in _rules)
                    rule.ResetState();
            }

            var maxWindow = _settings.Values.Max(s => s.WindowSeconds);
            var windowStart = report.Timestamp.AddSeconds(-maxWindow);
            var recent = _events.Since(windowStart > _scopeStart ? windowStart : _scopeStart);
            var context = new DetectionContext(report, _previous, recent, report.Timestamp, _engine.Options.StepSeconds);
            foreach (var rule in _rules)
            {
                var settings = _settings[rule.Id];
                if (!settings.Enabled)
                    continue;

                var metrics = _metrics[rule.Id];
                var watch = Stopwatch.StartNew();
                IReadOnlyList<DetectionFinding> findings;
                try
                {
                    findings = rule.Evaluate(context, settings);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Detection rule {RuleId} failed", rule.Id);
                    findings = [];
                }

                metrics.Evaluations++;
                metrics.TotalMilliseconds += watch.Elapsed.TotalMilliseconds;
                foreach (var finding in findings)
                {
                    // Scoped to the active scenario run so a new run re-detects conditions an earlier run already reported.
                    var fingerprint = $"{rule.Id}:{finding.Key}:{scope?.ToString("N") ?? "live"}";
                    if (!_deduplicator.ShouldEmit(fingerprint, report.Timestamp, TimeSpan.FromSeconds(settings.CooldownSeconds)))
                    {
                        metrics.Suppressed++;
                        continue;
                    }

                    metrics.Findings++;
                    metrics.LastFindingAt = report.Timestamp;
                    pending.Add((rule, finding, settings));
                }
            }

            _previous = report.Plant.Snapshot;
        }

        foreach (var (rule, finding, settings) in pending)
            await EmitAsync(rule, finding, settings, report.Timestamp, cancellationToken);
        return pending.Count;
    }

    private async Task<bool> EmitAsync(IDetectionRule rule, DetectionFinding finding, DetectionRuleSettings settings,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var severity = finding.SeverityOverride ?? settings.Severity;
        var detection = new DetectionRecord(Guid.NewGuid(), now, rule.Id, rule.Name, severity, rule.Category,
            finding.Title, finding.Description, finding.DeviceId, _scenarios.ActiveRunId, $"{rule.Id}:{finding.Key}",
            finding.Evidence, null);

        var incident = await _incidents.RaiseAsync(new IncidentDraft(
            finding.Title,
            finding.Description,
            rule.Category,
            Map(severity),
            "detection-engine",
            DetectionCorrelator.CorrelationKey(detection),
            finding.DeviceId is null ? [] : [finding.DeviceId],
            detection.ScenarioRunId,
            new IncidentEvidence(now, "detection", detection.Id.ToString("N"), $"[{rule.Id}] {finding.Title}")), cancellationToken);
        detection = detection with { IncidentId = incident.Incident?.Id };

        await _store.AppendAsync(detection, cancellationToken);
        _publisher.Publish(new SecurityEvent(Guid.NewGuid(), now, EventCategories.Detection, MapEvent(severity), rule.Id,
            $"{rule.Name}: {finding.Title}", "detection-engine", finding.DeviceId, detection.ScenarioRunId,
            detection.Id.ToString("N")));
        _realtime.Publish(RealtimeTopics.Detections, detection);
        ScadaInstrumentation.Detections.Add(1, new KeyValuePair<string, object?>("rule", rule.Id),
            new KeyValuePair<string, object?>("severity", severity.ToString()));
        return true;
    }

    private static IncidentSeverity Map(DetectionSeverity severity) => severity switch
    {
        DetectionSeverity.Critical => IncidentSeverity.Critical,
        DetectionSeverity.High => IncidentSeverity.High,
        DetectionSeverity.Medium => IncidentSeverity.Medium,
        _ => IncidentSeverity.Low
    };

    private static EventSeverity MapEvent(DetectionSeverity severity) => severity switch
    {
        DetectionSeverity.Critical => EventSeverity.Critical,
        DetectionSeverity.High => EventSeverity.High,
        DetectionSeverity.Medium => EventSeverity.Medium,
        _ => EventSeverity.Low
    };
}
