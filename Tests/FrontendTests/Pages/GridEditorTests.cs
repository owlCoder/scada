using Application.Common;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages.Grid;
using Xunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FrontendTests.Pages;

public sealed class GridEditorTests
{
    [Fact]
    public async Task AddBusesAndLineConnectTerminalsAndSaveANewVersion()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, "teacher", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();
        Button(page, "Start empty").Click(); Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Contains("At least one bus is required", page.Markup));
        Button(page, "Add Bus").Click(); Button(page, "Add Bus").Click(); Button(page, "Add Line").Click();
        Assert.Contains("both bus terminals must be connected", page.Markup);
        var nodes = page.FindAll("g.grid-node");
        Assert.Equal(2, nodes.Count);
        nodes[0].Click(); page.FindAll("g.grid-node")[1].Click();
        Assert.Contains("Network definition is valid", page.Markup);
        page.FindAll(".form-row input")[2].Change("Connect the town feeder");
        Button(page, "Save new version").Click();
        Assert.Empty(host.GridAdministration.Saves);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridAdministration.Saves));
        var save = host.GridAdministration.Saves[0];
        Assert.Equal(2, save.Definition.Buses.Count);
        var line = Assert.Single(save.Definition.Lines);
        Assert.Equal(save.Definition.Buses[0].Id, line.FromBusId);
        Assert.Equal(save.Definition.Buses[1].Id, line.ToBusId);
        Assert.Equal("Connect the town feeder", save.Comment);
        Assert.Equal("teacher", save.Actor.Name);
        Assert.Equal(3, host.GridStore.Versions.Count);
        Assert.Empty(host.GridRuntime.Activations);
    }

    [Fact]
    public async Task ValidationMessagesAreShownAndPreventSaving()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        host.GridAdministration.AdditionalErrors = ["Transformer voltage mismatch.", "Source island is disconnected."];
        var page = host.Context.Render<GridEditor>();
        Assert.Contains("Transformer voltage mismatch", page.Find(".validation").TextContent);
        Assert.Contains("Source island is disconnected", page.Find(".validation").TextContent);
        Assert.True(Button(page, "Save new version").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ActivateWarnsOfRestartAndRequiresConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, "teacher", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();
        Button(page, "Activate version 2").Click();
        Assert.Contains("restarts the grid simulation", page.Find(".dialog").TextContent);
        Assert.Empty(host.GridRuntime.Activations);
        Button(page, "Cancel").Click(); Assert.Empty(host.GridRuntime.Activations);
        Button(page, "Activate version 2").Click(); Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridRuntime.Activations));
        Assert.Equal(2, host.GridRuntime.Activations[0].Version);
        Assert.Equal("teacher", host.GridRuntime.Activations[0].Actor.Name);
    }

    [Fact]
    public async Task PropertiesApplyCatalogValuesAndEditorDoesNotMutateActiveDefinition()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();
        Button(page, "L1").Click();
        page.Find(".editor-properties select").Change("ACSR 150 mm²");
        var panel = page.Find(".editor-properties");
        Assert.Contains("440", panel.InnerHtml);
        Assert.Equal(400, host.Grid.Definition.Lines[0].RatedCurrentA);
        Assert.Contains("Unsaved draft", page.Markup);
    }


    [Fact]
    public async Task PointerDragMovesOnlyTheDraftAndSnapsCoordinates()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var module = host.Context.JSInterop.SetupModule("./_content/Hmi/grid-pointer.js");
        module.Setup<Domain.Grid.Definition.DiagramPosition>("capture", _ => true).SetResult(new(80, 80));
        module.Setup<Domain.Grid.Definition.DiagramPosition>("point", _ => true).SetResult(new(205, 118));
        module.Setup<bool>("release", _ => true).SetResult(true);
        var page = host.Context.Render<GridEditor>();
        var bus = page.FindAll("g.grid-node").Single(n => n.GetAttribute("aria-label") == "B-HV");
        bus.PointerDown(new PointerEventArgs { PointerId = 1, ClientX = 80, ClientY = 80 });
        page.Find("svg.editor-canvas").PointerMove(new PointerEventArgs { PointerId = 1, ClientX = 205, ClientY = 118 });
        page.Find("svg.editor-canvas").PointerUp(new PointerEventArgs { PointerId = 1 });
        page.WaitForAssertion(() => Assert.Equal("translate(200 120)", page.FindAll("g.grid-node").Single(n => n.GetAttribute("aria-label") == "B-HV").GetAttribute("transform")));
        Assert.Equal(new Domain.Grid.Definition.DiagramPosition(2, 2), host.Grid.Definition.Buses[0].Position);
        Assert.Contains(module.Invocations, call => call.Identifier == "release");
    }

    [Fact]
    public async Task EveryPaletteItemCanBeAddedAndSelected()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();
        foreach (var kind in new Hmi.Services.Grid.GridEquipmentCatalog().Palette)
        {
            Button(page, $"Add {kind}").Click();
            Assert.Contains(kind.ToUpperInvariant(), page.Find(".editor-properties h2").TextContent);
            Assert.DoesNotContain("Live data could not be refreshed", page.Markup);
        }
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> page, string text) where T : IComponent => page.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
