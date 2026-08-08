using Crowbar.UI;

namespace Crowbar.UI.Tests.Razor;

public class RazorDirectiveTests
{
    [Fact]
    public void NativeDirectivesCompileIntoTheComponent()
    {
        var source = """
            @inherits Crowbar.UI.Tests.RazorTestBase
            @implements Crowbar.UI.Tests.IRazorTestContract
            @using System.Text
            @namespace Crowbar.UI.Tests.Smoke.Generated
            @{ var inline = new StringBuilder("inline").ToString(); }
            <div><span>@inline</span><span>@BuildVersion</span></div>
            @code {
                protected override void OnInitialized() { base.OnInitialized(); }
                protected override void OnAfterRender(bool firstRender) { AfterRenderCount++; }
            }
            """;

        var factory = new RazorComponentFactory();
        var template = factory.CompileTemplate(source, "DirectiveDemo", typeof(PanelComponent),
            typeof(RazorTestBase).Assembly, typeof(UiSystem).Assembly);

        // @inherits and @implements are honored natively.
        Assert.IsAssignableFrom<RazorTestBase>(template);
        Assert.IsAssignableFrom<IRazorTestContract>(template);
        // @namespace is honored natively.
        Assert.Equal("Crowbar.UI.Tests.Smoke.Generated", template.GetType().Namespace);

        factory.BuildTree(template);
        // @code members (incl. overrides) are generated natively.
        Assert.Equal("inline", TestUi.Find(template, p => p.TagName == "text")?.Text);
        Assert.Equal(1, ((RazorTestBase)template).InitializedCount);
        Assert.Equal(1, ((RazorTestBase)template).ParametersCount);
        Assert.Equal(1, ((RazorTestBase)template).AfterRenderCount);
    }

    [Fact]
    public void ShouldRenderSuppressesRendering()
    {
        var source = """
            @inherits Crowbar.UI.Tests.RazorTestBase
            <div><span>@BuildVersion</span></div>
            @code {
                protected override void OnAfterRender(bool firstRender) { AfterRenderCount++; }
            }
            """;
        var factory = new RazorComponentFactory();
        var template = (RazorTestBase)factory.CompileTemplate(source, "SuppressDemo", typeof(PanelComponent),
            typeof(RazorTestBase).Assembly, typeof(UiSystem).Assembly);
        factory.BuildTree(template);
        Assert.Equal(1, template.AfterRenderCount);

        template.AllowRender = false;
        template.BuildVersion = 1;
        factory.BuildTree(template);
        Assert.Equal(1, template.AfterRenderCount);

        template.AllowRender = true;
        template.StateHasChanged();
        factory.BuildTree(template);
        Assert.Equal(2, template.AfterRenderCount);
    }

    [Fact]
    public void ParameterlessConstructorAndBaseValidationAreEnforced()
    {
        var factory = new RazorComponentFactory();
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CompileTemplate(
            "@inherits System.IO.FileStream\n<div>bad</div>", "BadBase", typeof(PanelComponent)));
        Assert.Contains("must derive from RazorPanel", ex.Message);
    }

    [Theory]
    [InlineData("@page \"/\"\n<div/>", new[] { "/" })]
    [InlineData("@page \"/home\"\n@page \"/items/{id:int}\"\n<div/>", new[] { "/home", "/items/{id:int}" })]
    [InlineData("<div>no page here</div>", new string[0])]
    public void ExtractPagesFindsRoutes(string source, string[] expected)
    {
        var pages = RazorComponentFactory.ExtractPages(source);
        Assert.Equal(expected, pages);
    }
}
