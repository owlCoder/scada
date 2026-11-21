using System.Diagnostics;
using Application.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PerformanceTests.Support;
using Security.Detection;
using Security.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTests.Simulation;

[Trait("Category", "Performance")]
public sealed class SimulationPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DefaultPlantExecutes2000StepsWithScenarioAndDetectionObservers()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var host = PerformanceHost.Create(time);
        var services = host.Services;
        var observers = services.GetServices<ISimulationObserver>().ToArray();
        Assert.Contains(observers, observer => observer is DetectionEngine);
        Assert.Contains(observers, observer => observer is ScenarioEngine);
        var coordinator = services.GetRequiredService<SimulationCoordinator>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        // Warm up the complete path; reset state so the measured run starts on the default plant.
        await coordinator.StepManyAsync(10, timeout.Token);
        coordinator.Reset("performance");
        var detection = services.GetRequiredService<DetectionEngine>();
        detection.ResetState();
        var before = detection.Metrics.Select(metric => metric.Evaluations).ToArray();
        const int steps = 2000;
        var watch = Stopwatch.StartNew();
        for (var step = 0; step < steps; step++)
        {
            time.Advance(TimeSpan.FromSeconds(.5));
            Assert.NotNull(await coordinator.StepAsync(true, timeout.Token));
        }
        watch.Stop();
        output.WriteLine("Simulation: {0} steps in {1:F1} ms; {2:F0} steps/s (default plant, all observers, persistence and realtime).",
            steps, watch.Elapsed.TotalMilliseconds, steps / watch.Elapsed.TotalSeconds);
        Assert.Equal(steps, services.GetRequiredService<SimulationEngine>().Status.StepCount);
        Assert.All(detection.Metrics.Select((metric, i) => metric.Evaluations - before[i]), count => Assert.Equal(steps, count));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"2000 simulation steps took {watch.Elapsed}.");
    }

    [Fact]
    public async Task AllDetectionRulesEvaluateWithinPerStepBudget()
    {
        await using var host = PerformanceHost.Create();
        var services = host.Services;
        var engine = services.GetRequiredService<SimulationEngine>();
        var detection = services.GetRequiredService<DetectionEngine>();
        var report = Assert.IsType<SimulationStepReport>(engine.Step(true));
        await detection.OnStepAsync(report, CancellationToken.None);
        var before = detection.Metrics.ToArray();
        const int steps = 2000;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var wall = Stopwatch.StartNew();
        long detectionTicks = 0;
        for (var step = 0; step < steps; step++)
        {
            report = Assert.IsType<SimulationStepReport>(engine.Step(true));
            var start = Stopwatch.GetTimestamp();
            await detection.OnStepAsync(report, timeout.Token);
            detectionTicks += Stopwatch.GetTimestamp() - start;
        }
        wall.Stop();
        var elapsed = TimeSpan.FromSeconds((double)detectionTicks / Stopwatch.Frequency);
        var after = detection.Metrics;
        Assert.Equal(17, after.Count);
        for (var i = 0; i < after.Count; i++)
            Assert.Equal(steps, after[i].Evaluations - before[i].Evaluations);
        var microseconds = elapsed.TotalMicroseconds / steps;
        output.WriteLine("Detection: {0} steps x {1} rules in {2:F1} ms; {3:F1} us/step; rule bodies {4:F1} ms total.",
            steps, after.Count, elapsed.TotalMilliseconds, microseconds,
            after.Sum(metric => metric.TotalMilliseconds) - before.Sum(metric => metric.TotalMilliseconds));
        Assert.True(wall.Elapsed < TimeSpan.FromSeconds(8), $"Detection run took {wall.Elapsed}.");
        Assert.True(microseconds < 3000, $"Detection cost {microseconds:F1} us/step (budget 3000 us).");
    }
}
