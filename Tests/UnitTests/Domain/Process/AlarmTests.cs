using Domain.Process.Alarms;
using Xunit;

namespace UnitTests.Domain.Process;

public sealed class AlarmTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task HighLevelRaisesOneAlarmThatCanBeAcknowledged()
    {
        var plant = PlantTests.CreatePlant();
        plant.SetTankLevel("T-101", 90);

        var first = plant.Step(TimeSpan.FromSeconds(0.5), Now);
        var second = plant.Step(TimeSpan.FromSeconds(0.5), Now.AddSeconds(0.5));

        var raised = Assert.Single(first.AlarmTransitions, t => t.Alarm.SourceId == "T-101" && t.Kind == AlarmTransitionKind.Raised);
        Assert.Equal(AlarmType.High, raised.Alarm.Type);
        Assert.DoesNotContain(second.AlarmTransitions, t => t.Alarm.Key == raised.Alarm.Key && t.Kind == AlarmTransitionKind.Raised);

        var acknowledged = plant.AcknowledgeAlarm(raised.Alarm.Key, "operator", Now.AddSeconds(1));
        Assert.Equal(AlarmTransitionKind.Acknowledged, acknowledged!.Kind);
        Assert.Null(plant.AcknowledgeAlarm(raised.Alarm.Key, "operator", Now.AddSeconds(2)));
        return Task.CompletedTask;
    }

    [Fact]
    public Task LevelAlarmsFollowTheReportedValue()
    {
        var plant = PlantTests.CreatePlant();
        plant.SpoofSensor("LT-103", 3);

        var step = plant.Step(TimeSpan.FromSeconds(0.5), Now);

        Assert.Contains(step.AlarmTransitions, t => t.Alarm.SourceId == "T-103" && t.Alarm.Type == AlarmType.LowLow);
        Assert.True(plant.CaptureSnapshot().Tank("T-103")!.LevelPercent > 20);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AlarmClearsWhenTheConditionReturnsToNormal()
    {
        var plant = PlantTests.CreatePlant();
        plant.SetTankLevel("T-101", 90);
        plant.Step(TimeSpan.FromSeconds(0.5), Now);
        plant.SetTankLevel("T-101", 50);

        var step = plant.Step(TimeSpan.FromSeconds(0.5), Now.AddSeconds(0.5));

        Assert.Contains(step.AlarmTransitions, t => t.Alarm.SourceId == "T-101" && t.Kind == AlarmTransitionKind.Cleared);
        return Task.CompletedTask;
    }
}
