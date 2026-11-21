using Domain.Incidents;
using Xunit;

namespace UnitTests.Domain.Incidents;

public sealed class IncidentWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static Incident NewIncident(IncidentStatus status = IncidentStatus.Open) => new(Guid.NewGuid(), 1, "Title", "Description",
        IncidentCategory.Cybersecurity, IncidentSeverity.High, IncidentPriority.High, "test", status, null, Now, Now, null, null,
        null, null, null, [], null, "key", [], 1);

    [Theory]
    [InlineData(IncidentStatus.Open, IncidentStatus.Acknowledged, true)]
    [InlineData(IncidentStatus.Acknowledged, IncidentStatus.InProgress, true)]
    [InlineData(IncidentStatus.Acknowledged, IncidentStatus.Resolved, true)]
    [InlineData(IncidentStatus.InProgress, IncidentStatus.Resolved, true)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Closed, true)]
    [InlineData(IncidentStatus.Resolved, IncidentStatus.Open, true)]
    [InlineData(IncidentStatus.Open, IncidentStatus.Closed, false)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Open, false)]
    [InlineData(IncidentStatus.Open, IncidentStatus.Resolved, false)]
    public Task StateMachineAllowsOnlyDefinedTransitions(IncidentStatus from, IncidentStatus to, bool allowed)
    {
        Assert.Equal(allowed, IncidentStateMachine.CanTransition(from, to));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ResolvingRequiresNotesAndRecordsTheResolver()
    {
        var incident = NewIncident(IncidentStatus.InProgress);

        Assert.False(IncidentWorkflow.ChangeStatus(incident, IncidentStatus.Resolved, "ana", null, Now).Succeeded);
        var resolved = IncidentWorkflow.ChangeStatus(incident, IncidentStatus.Resolved, "ana", "Sensor recalibrated", Now);

        Assert.True(resolved.Succeeded);
        Assert.Equal("ana", resolved.Incident!.ResolvedBy);
        Assert.Equal("Sensor recalibrated", resolved.Incident.ResolutionNotes);
        Assert.Contains(resolved.Timeline, e => e.FromStatus == IncidentStatus.InProgress && e.ToStatus == IncidentStatus.Resolved);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AcknowledgementRecordsWhoAndWhen()
    {
        var change = IncidentWorkflow.ChangeStatus(NewIncident(), IncidentStatus.Acknowledged, "bob", null, Now.AddMinutes(1));
        Assert.Equal("bob", change.Incident!.AcknowledgedBy);
        Assert.Equal(Now.AddMinutes(1), change.Incident.AcknowledgedAt);
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidTransitionIsReportedWithoutChangingTheIncident()
    {
        var change = IncidentWorkflow.ChangeStatus(NewIncident(), IncidentStatus.Closed, "bob", "x", Now);
        Assert.Equal(IncidentOperationStatus.InvalidTransition, change.Status);
        Assert.Null(change.Incident);
        return Task.CompletedTask;
    }
}
