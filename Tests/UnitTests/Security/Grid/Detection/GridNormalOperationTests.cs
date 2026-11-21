using Application.Detection;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Detection;

/// <summary>
/// Normal operation over a full simulated day on the default network (60 s steps, 0.2 % telemetry noise, daily load
/// profile) must produce zero grid findings.
/// </summary>
public sealed class GridNormalOperationTests
{
    [Fact]
    public async Task NormalSimulatedDayProducesZeroFindings()
    {
        var rig = new SecurityGridTestRig();

        // One simulated day at 60 s steps = 1440 steps.
        for (var i = 0; i < 1440; i++)
            await rig.StepAsync(60);

        var detections = await rig.DetectionsAsync();
        Assert.True(detections.Count == 0,
            "Unexpected detections: " + string.Join("; ", detections.Select(d => $"{d.RuleId}: {d.Title}")));
        Assert.All(rig.Detection.Metrics, m => Assert.Equal(1440, m.Evaluations));
    }

    [Fact]
    public async Task NormalOperationOnTheAlternativeNetworkProducesZeroFindings()
    {
        var rig = new SecurityGridTestRig(SecurityFakeGridNetworks.Alternative(), seed: 11);

        for (var i = 0; i < 720; i++)
            await rig.StepAsync(60);

        var detections = await rig.DetectionsAsync();
        Assert.True(detections.Count == 0,
            "Unexpected detections: " + string.Join("; ", detections.Select(d => $"{d.RuleId}: {d.Title}")));
    }
}
