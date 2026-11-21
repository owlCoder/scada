using Application.Events;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Microsoft.Extensions.Time.Testing;
using Protocols.Iec104;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class Iec104AsduHandlerTests
{
    private const string ActorA = "iec104-master@192.0.2.10:40001#1";
    private const string ActorB = "iec104-master@192.0.2.11:40002#2";

    private sealed record Rig(Iec104FakeGridView View, Iec104FakeGridControl Control, GridEngineEventCollector Events,
        Iec104AddressMapProvider Maps, Iec104AsduHandler Handler)
    {
        public static Rig Create()
        {
            var view = new Iec104FakeGridView();
            var control = new Iec104FakeGridControl();
            var events = new GridEngineEventCollector();
            var time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero));
            var maps = new Iec104AddressMapProvider(view, events, time);
            var handler = new Iec104AsduHandler(maps, view, control, new Iec104Options(), events, time);
            return new Rig(view, control, events, maps, handler);
        }
    }

    private static Asdu StepCommand(int commonAddress, int ioa, byte rcc) =>
        Asdu.Build(new AsduHeader(Iec104TypeId.C_RC_NA_1, false, 1, CauseOfTransmission.ActivationRequest, 0, commonAddress),
            [new Iec104InformationObject(ioa, [rcc])]);

    private static Asdu DoubleCommand(int commonAddress, int ioa, byte dco) =>
        Asdu.Build(new AsduHeader(Iec104TypeId.C_DC_NA_1, false, 1, CauseOfTransmission.ActivationRequest, 0, commonAddress),
            [new Iec104InformationObject(ioa, [dco])]);

    [Fact]
    public async Task StepCommandMapsRccOneToTapLowerAndTwoToTapRaise()
    {
        var rig = Rig.Create();
        // RCC bits 0-1 (IEC 60870-5-101/104): 1 = next step LOWER, 2 = next step HIGHER.
        var lower = await rig.Handler.HandleAsync(StepCommand(2, 3000, 0x01), "iec104:test", ActorA, CancellationToken.None);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, lower[0].Header.Cause);
        Assert.Equal(CauseOfTransmission.ActivationTermination, lower[^1].Header.Cause);
        var raise = await rig.Handler.HandleAsync(StepCommand(2, 3000, 0x02), "iec104:test", ActorA, CancellationToken.None);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, raise[0].Header.Cause);
        Assert.Equal([GridCommandType.TapLower, GridCommandType.TapRaise], rig.Control.Commands.Select(c => c.Command.Type));
        Assert.All(rig.Control.Commands, c => Assert.Equal(Iec104SampleGrid.TransformerId, c.Command.TargetId));
    }

    [Fact]
    public async Task StepCommandRccZeroAndThreeAreNegativeConfirmsWithoutOperating()
    {
        var rig = Rig.Create();
        foreach (var rcc in new byte[] { 0x00, 0x03, 0x80, 0x83 })
        {
            var responses = await rig.Handler.HandleAsync(StepCommand(2, 3000, rcc), "iec104:test", ActorA, CancellationToken.None);
            Assert.Single(responses);
            Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, responses[0].Header.Cause);
        }

        Assert.Empty(rig.Control.Commands);
        Assert.Contains(rig.Events.Items, e => e.Category == EventCategories.CommandRejected
            && e.Message.Contains("not permitted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StepCommandSelectConfirmsWithoutOperatingAndExecuteOperates()
    {
        var rig = Rig.Create();
        // S/E=1 (select): positive activation confirmation only, no grid command, no termination.
        var select = await rig.Handler.HandleAsync(StepCommand(2, 3000, 0x82), "iec104:test", ActorA, CancellationToken.None);
        Assert.Single(select);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, select[0].Header.Cause);
        Assert.Empty(rig.Control.Commands);

        // S/E=0 (execute): the step is operated and the activation terminates.
        var execute = await rig.Handler.HandleAsync(StepCommand(2, 3000, 0x02), "iec104:test", ActorA, CancellationToken.None);
        Assert.Equal(2, execute.Count);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, execute[0].Header.Cause);
        Assert.Equal(CauseOfTransmission.ActivationTermination, execute[^1].Header.Cause);
        Assert.Equal([GridCommandType.TapRaise], rig.Control.Commands.Select(c => c.Command.Type));
    }

    [Fact]
    public async Task DoubleCommandNotPermittedValuesAreNegativeConfirms()
    {
        var rig = Rig.Create();
        // DCO: (select, 0), (select, 3), (execute, 3) are not permitted; (execute, 0) is the deactivation (cancel).
        foreach (var dco in new byte[] { 0x80, 0x83, 0x03 })
        {
            var responses = await rig.Handler.HandleAsync(DoubleCommand(1, 2000, dco), "iec104:test", ActorA, CancellationToken.None);
            Assert.Single(responses);
            Assert.Equal(CauseOfTransmission.ActivationConfirmNegative, responses[0].Header.Cause);
        }

        var cancel = await rig.Handler.HandleAsync(DoubleCommand(1, 2000, 0x00), "iec104:test", ActorA, CancellationToken.None);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, cancel[0].Header.Cause);
        Assert.Equal([GridCommandType.Cancel], rig.Control.Commands.Select(c => c.Command.Type));
    }

    [Fact]
    public async Task CommandsRunUnderThePerConnectionActorNameWithTheConfiguredRole()
    {
        var rig = Rig.Create();
        await rig.Handler.HandleAsync(DoubleCommand(1, 2000, 0x81), "iec104:test", ActorA, CancellationToken.None);
        await rig.Handler.HandleAsync(DoubleCommand(1, 2000, 0x81), "iec104:test", ActorB, CancellationToken.None);
        Assert.Equal(2, rig.Control.Commands.Count);
        Assert.Equal(ActorA, rig.Control.Commands[0].Actor.Name);
        Assert.Equal(ActorB, rig.Control.Commands[1].Actor.Name);
        Assert.All(rig.Control.Commands, c =>
        {
            Assert.Equal(AccessRole.Operator, c.Actor.Role);
            Assert.Equal("iec104", c.Source);
            Assert.Equal(GridCommandType.SelectOpen, c.Command.Type);
        });
        // Audit stays readable: the command events name the per-connection actor.
        Assert.Contains(rig.Events.Items, e => e.Actor == ActorA && e.Message.Contains("SelectOpen", StringComparison.Ordinal));
        Assert.Contains(rig.Events.Items, e => e.Actor == ActorB && e.Message.Contains("SelectOpen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandlerUsesTheRebuiltMapAfterADefinitionChange()
    {
        var rig = Rig.Create();
        // Sanity: the tap point of RTU 2 (CA 2, IOA 3000) exists in the initial map.
        var before = await rig.Handler.HandleAsync(StepCommand(2, 3000, 0x02), "iec104:test", ActorA, CancellationToken.None);
        Assert.Equal(CauseOfTransmission.ActivationConfirm, before[0].Header.Cause);

        // A new definition version without the transformer on RTU 2 is activated.
        var definition = Iec104SampleGrid.CreateDefinition() with { Name = "Sample grid v2" };
        var rtu2 = definition.Rtus.Single(r => r.Id == Iec104SampleGrid.Rtu2Id);
        var withoutTransformer = rtu2 with { ElementIds = [.. rtu2.ElementIds.Where(e => e != Iec104SampleGrid.TransformerId)] };
        var v2 = definition with { Rtus = [.. definition.Rtus.Select(r => r.Id == rtu2.Id ? withoutTransformer : r)] };
        rig.View.SetDefinition(v2);

        // The same IOA is now unknown: COT 47, no command, and the rebuild was audited.
        var after = await rig.Handler.HandleAsync(StepCommand(2, 3000, 0x02), "iec104:test", ActorA, CancellationToken.None);
        Assert.Single(after);
        Assert.Equal(CauseOfTransmission.UnknownInformationObjectAddress, after[0].Header.Cause);
        Assert.Single(rig.Control.Commands);
        Assert.Equal("Sample grid v2", rig.Maps.Current.DefinitionName);
        Assert.Contains(rig.Events.Items, e => e.Category == EventCategories.Protocol
            && e.Message.Contains("rebuilt", StringComparison.OrdinalIgnoreCase) && e.Message.Contains("Sample grid v2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownCommonAddressStillGetsCot46()
    {
        var rig = Rig.Create();
        var responses = await rig.Handler.HandleAsync(StepCommand(99, 3000, 0x02), "iec104:test", ActorA, CancellationToken.None);
        Assert.Single(responses);
        Assert.Equal(CauseOfTransmission.UnknownCommonAddress, responses[0].Header.Cause);
        Assert.Empty(rig.Control.Commands);
    }
}
