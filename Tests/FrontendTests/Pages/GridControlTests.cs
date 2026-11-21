using Application.Common;
using Application.Identity;
using Bunit;
using Domain.Grid.Commands;
using FrontendTests.Support;
using Hmi.Components.Pages.Grid;
using Xunit;
using Microsoft.AspNetCore.Components;

namespace FrontendTests.Pages;

public sealed class GridControlTests
{
    [Fact]
    public async Task SelectConfirmExecuteUsesTheActorAndHmiChannel()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridFaceplate>(p => p.Add(x => x.ElementId, "Q1"));
        Button(page, "Select open").Click();
        Assert.Empty(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        Assert.True(host.Grid.State.Switch("Q1")!.Closed);
        Assert.Equal(GridCommandType.SelectOpen, host.GridControl.Calls[0].Command.Type);
        Assert.Contains("10 s remaining", page.Markup);
        Button(page, "Execute").Click();
        Assert.Single(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Equal(2, host.GridControl.Calls.Count));
        Assert.False(host.Grid.State.Switch("Q1")!.Closed);
        var call = host.GridControl.Calls[1];
        Assert.Equal(GridCommandType.Execute, call.Command.Type);
        Assert.Equal("eva", call.Actor.Name);
        Assert.Equal("hmi", call.Source);
    }

    [Fact]
    public async Task CancellingConfirmationDoesNotSelectAndCancelSelectionDoesNotOperate()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridFaceplate>(p => p.Add(x => x.ElementId, "Q1"));
        Button(page, "Select open").Click(); Button(page, "Cancel").Click();
        Assert.Empty(host.GridControl.Calls);
        Button(page, "Select open").Click(); Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        Button(page, "Cancel selection").Click();
        Assert.Single(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Equal(GridCommandType.Cancel, host.GridControl.Calls.Last().Command.Type));
        Assert.True(host.Grid.State.Switch("Q1")!.Closed);
        Assert.False(host.Grid.State.Switch("Q1")!.Selected);
    }

    [Fact]
    public async Task ExpiredLocalSelectionCannotExecute()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        var page = host.Context.Render<GridFaceplate>(p => p.Add(x => x.ElementId, "Q1"));
        Button(page, "Select open").Click(); Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        host.Rig.Time.Advance(TimeSpan.FromSeconds(11));
        page.WaitForAssertion(() => Assert.True(Button(page, "Execute").HasAttribute("disabled")));
        Assert.Single(host.GridControl.Calls);
    }

    [Theory]
    [InlineData("T1", "Raise tap", GridCommandType.TapRaise)]
    [InlineData("T1", "Lower tap", GridCommandType.TapLower)]
    [InlineData("T1", "Set AVR automatic", GridCommandType.SetTapAutomatic)]
    [InlineData("LD1", "Shed load", GridCommandType.ShedLoad)]
    [InlineData("C1", "Set steps", GridCommandType.SetCapacitorSteps)]
    [InlineData("Q1", "Reset protection", GridCommandType.ResetProtection)]
    public async Task FaceplateCommandsRequireConfirmation(string id, string text, GridCommandType expected)
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridFaceplate>(p => p.Add(x => x.ElementId, id));
        Button(page, text).Click(); Assert.Empty(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridControl.Calls));
        Assert.Equal(expected, host.GridControl.Calls[0].Command.Type);
        Assert.Equal("eva", host.GridControl.Calls[0].Actor.Name);
        if (expected == GridCommandType.TapRaise) Assert.Equal(1, host.Grid.State.Branch("T1")!.TapPosition);
    }

    [Fact]
    public async Task AlarmAcknowledgeRequiresConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator, "eva", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridAlarms>();
        Button(page, "Acknowledge").Click(); Assert.Empty(host.GridControl.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Contains("Acknowledged", page.Markup));
        var command = Assert.Single(host.GridControl.Calls).Command;
        Assert.Equal(GridCommandType.AcknowledgeAlarm, command.Type);
        Assert.Equal("AL1", command.TargetId);
        Assert.Equal("eva", host.Grid.State.Alarms[0].AcknowledgedBy);
    }

    [Fact]
    public async Task ViewersHaveNoLiveControls()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer, mode: SimulationMode.Grid);
        var faceplate = host.Context.Render<GridFaceplate>(p => p.Add(x => x.ElementId, "Q1"));
        Assert.Empty(faceplate.FindAll("button"));
        Assert.Empty(host.Context.Render<GridAlarms>().FindAll("button"));
    }

    [Fact]
    public async Task OnlyInstructorsCanEditProtectionAndChangesAreConfirmed()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, "teacher", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridProtection>();
        Button(page, "Edit P1").Click();
        page.Find("input[type=number]").Change("500");
        Button(page, "Apply settings").Click(); Assert.Empty(host.GridTraining.Calls);
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() => Assert.Single(host.GridTraining.Calls));
        Assert.StartsWith("Protection 500/", host.GridTraining.Calls[0].Action);
        Assert.Equal("teacher", host.GridTraining.Calls[0].Actor.Name);
        await using var operatorHost = HmiTestHost.Create(AccessRole.Operator, mode: SimulationMode.Grid);
        Assert.Empty(operatorHost.Context.Render<GridProtection>().FindAll("button"));
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> page, string text) where T : IComponent => page.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
