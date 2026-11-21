using Domain.Grid.Snapshots;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineCoordinatorTests
{
    [Fact]
    public async Task ObserversRunOutsideEngineLockAndFailuresDoNotPreventOtherObservers()
    {
        GridEngineTestRig? rig = null;
        var called = 0;
        var failing = new GridEngineCallbackObserver(_ => throw new InvalidOperationException("test failure"));
        var succeeding = new GridEngineCallbackObserver(async report =>
        {
            // A different thread must acquire the engine lock before this callback can complete.
            await Task.Run(() => rig!.Engine.Mutate(w => w.OperateSwitch("QF1", false, "observer", GridChangeCause.System)))
                .WaitAsync(TimeSpan.FromSeconds(2));
            called++;
            Assert.True(report.Snapshot.Converged);
            return 1;
        });
        rig = new(observers: [failing, succeeding]);
        Assert.NotNull(await rig.Coordinator.StepAsync(false, CancellationToken.None));
        Assert.Equal(1, called);
    }
}
