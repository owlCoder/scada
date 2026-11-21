using Application.Events;
using Application.Identity;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Application.Simulation;

public sealed class SimulationCoordinatorTests
{
    [Fact]
    public Task PauseAndResumeRequireAnOperatorAndAuditTheActor()
    {
        var rig = new TestRig();
        var viewer = new Actor("guest", AccessRole.Viewer);
        var op = new Actor("eva", AccessRole.Operator);
        Assert.False(rig.Coordinator.SetPaused(true, viewer).IsPaused);
        Assert.True(rig.Coordinator.SetPaused(true, op).IsPaused);
        Assert.True(rig.Coordinator.SetPaused(false, viewer).IsPaused);
        Assert.False(rig.Coordinator.SetPaused(false, op).IsPaused);
        Assert.Equal(2, rig.Buffer.GetRecent().Count(e => e.Category == EventCategories.AuthorizationDenied && e.Actor == "guest"));
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Message == "Simulation paused" && e.Actor == "eva");
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Message == "Simulation resumed" && e.Actor == "eva");
        return Task.CompletedTask;
    }
}
