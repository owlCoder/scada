using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// A protection relay's settings were changed or the relay was disabled (Industroyer's protection-tampering payload):
/// either a <see cref="GridStepEventKind.ProtectionSettingChanged"/> event or the snapshot flag
/// <see cref="ProtectionSnapshot.SettingsChanged"/>.
/// </summary>
public sealed class GridProtectionTamperingRule(IGridView view) : GridDetectionRuleBase
{
    private readonly IGridView _view = view ?? throw new ArgumentNullException(nameof(view));

    public override string Id => "grid-protection-tampering";
    public override string Name => "Protection tampering";
    public override string Description => "Protection settings were changed or a relay was disabled.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.Critical, 0, 300, 600);

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        var subjects = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.ProtectionSettingChanged))
            subjects[stepEvent.SourceId] = stepEvent.Message;

        foreach (var protection in context.Current.Protections.Where(p => p.SettingsChanged))
        {
            subjects.TryAdd(protection.Id,
                // Operators and incidents learn that the relay no longer matches its approved settings, not the tampered values
                // (instructors see those on the protection page).
                protection.Enabled ? "settings differ from the approved definition" : "relay disabled");
        }

        foreach (var (protectionId, detail) in subjects)
        {
            var protection = context.Current.Protections.FirstOrDefault(p => p.Id == protectionId);
            findings.Add(new(protectionId, $"Protection {protectionId} tampered with",
                $"{protection?.Name ?? protectionId}: {detail}.",
                protectionId,
                [Evidence("protection", protectionId, detail)]));
        }

        return findings;
    }
}
