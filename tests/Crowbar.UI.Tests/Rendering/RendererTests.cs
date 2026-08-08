using Crowbar.UI;

namespace Crowbar.UI.Tests.Rendering;

public class RendererTests
{
    [Fact]
    public void RasterizesToViewportSizedBuffer()
    {
        using var ui = TestUi.Create(width: 320, height: 200);
        var root = ui.Screen;
        var child = new Panel { TagName = "div" };
        child.AddClass("box");
        root.AddChild(child);
        ui.LoadStyles(".box { width: 100px; height: 40px; background-color: #ff0000; }");

        var pixels = ui.Render();
        Assert.Equal(320 * 200 * 4, pixels.Length);
    }

    [Fact]
    public void LayoutAndStyleRefreshOnInlineStyleChanges()
    {
        using var ui = TestUi.Create(width: 320, height: 200);
        var button = new Button("Click");
        button.SetInlineStyle("width", "80px");
        button.SetInlineStyle("height", "32px");
        ui.Screen.AddChild(button);
        ui.Render();

        button.SetInlineStyle("padding", "10px");
        button.SetInlineStyle("text-align", "center");
        button.SetInlineStyle("vertical-align", "center");
        button.SetInlineStyle("line-height", "32px");
        ui.Render();

        Assert.Equal(10, button.ComputedStyle.PaddingLeft);
        var buttonText = button.Children[0];
        Assert.Equal("center", buttonText.ComputedStyle.TextAlign);
        Assert.Equal("center", buttonText.ComputedStyle.VerticalAlign);
        Assert.Equal(32, buttonText.ComputedStyle.LineHeight);
    }

    [Fact]
    public void RendererReportsDirtyState()
    {
        using var renderer = new SkiaUiRenderer();
        renderer.Resize(100, 100);
        Assert.True(renderer.IsDirty);
        var screen = new ScreenPanel();
        renderer.Render(screen);
        Assert.False(renderer.IsDirty);
        renderer.MarkDirty();
        Assert.True(renderer.IsDirty);
    }
}
