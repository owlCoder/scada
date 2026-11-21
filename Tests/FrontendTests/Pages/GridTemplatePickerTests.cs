using Application.Common;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages.Grid;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridTemplatePickerTests
{
    [Fact]
    public async Task StartFromTemplateLoadsTheRuralTemplateIntoTheEditorDraft()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, "teacher", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();
        Assert.Equal("Training grid", page.FindAll(".form-row input")[0].GetAttribute("value"));

        page.Find("select.template-picker").Change("rural-overhead-20kv");
        Button(page, "Start from template").Click();
        Assert.Contains("Replace editor draft?", page.Find(".dialog").TextContent);
        Button(page, "Confirm").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Rural 20 kV overhead grid (20 MVA, PV parks)", page.FindAll(".form-row input")[0].GetAttribute("value"));
            Assert.Contains("Network definition is valid", page.Find(".validation").TextContent);
            Assert.Contains("N4-MV", page.Find(".palette").TextContent);
            Assert.Contains("PV2", page.Find(".palette").TextContent);
        });
        // The live/active definition is untouched; only the draft changed.
        Assert.Equal("Training grid", host.Grid.Definition.Name);
        Assert.Empty(host.GridAdministration.Saves);
        Assert.Empty(host.GridRuntime.Activations);
    }

    [Fact]
    public async Task StartFromTemplateLoadsTheUrbanTemplateAndCancelKeepsTheCurrentDraft()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, "teacher", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();

        page.Find("select.template-picker").Change("urban-cable-20kv");
        Button(page, "Start from template").Click();
        Button(page, "Cancel").Click();
        Assert.Equal("Training grid", page.FindAll(".form-row input")[0].GetAttribute("value"));
        Assert.DoesNotContain("SS11-MV", page.Find(".palette").TextContent);

        Button(page, "Start from template").Click();
        Button(page, "Confirm").Click();
        page.WaitForAssertion(() =>
        {
            Assert.Equal("Urban 20 kV cable grid (2x40 MVA)", page.FindAll(".form-row input")[0].GetAttribute("value"));
            Assert.Contains("Network definition is valid", page.Find(".validation").TextContent);
            Assert.Contains("SS11-MV", page.Find(".palette").TextContent);
            Assert.Contains("NOP-A", page.Find(".palette").TextContent);
            Assert.Contains("BC", page.Find(".palette").TextContent);
        });
        Assert.Equal("Training grid", host.Grid.Definition.Name);
    }

    [Fact]
    public async Task TemplatePickerOffersAllCatalogTemplates()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator, "teacher", mode: SimulationMode.Grid);
        var page = host.Context.Render<GridEditor>();
        var options = page.FindAll("select.template-picker option").Select(o => o.GetAttribute("value") ?? "").ToArray();
        Assert.Equal(["default", "urban-cable-20kv", "rural-overhead-20kv"], options);
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> page, string text) where T : IComponent =>
        page.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
