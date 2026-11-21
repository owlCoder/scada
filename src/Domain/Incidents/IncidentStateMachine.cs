namespace Domain.Incidents;

/// <summary>
/// Valid lifecycle: Open → Acknowledged → InProgress → Resolved → Closed. Acknowledged incidents may be
/// resolved directly and resolved incidents may be reopened. Closed is terminal.
/// </summary>
public static class IncidentStateMachine
{
    private static readonly IReadOnlyDictionary<IncidentStatus, IncidentStatus[]> Transitions =
        new Dictionary<IncidentStatus, IncidentStatus[]>
        {
            [IncidentStatus.Open] = [IncidentStatus.Acknowledged],
            [IncidentStatus.Acknowledged] = [IncidentStatus.InProgress, IncidentStatus.Resolved],
            [IncidentStatus.InProgress] = [IncidentStatus.Resolved],
            [IncidentStatus.Resolved] = [IncidentStatus.Closed, IncidentStatus.Open],
            [IncidentStatus.Closed] = []
        };

    public static IReadOnlyList<IncidentStatus> AllowedTransitions(IncidentStatus from) =>
        Transitions.TryGetValue(from, out var next) ? next : [];

    public static bool CanTransition(IncidentStatus from, IncidentStatus to) =>
        AllowedTransitions(from).Contains(to);
}
