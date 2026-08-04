using System.Diagnostics;

namespace Crowbar.Engine.UI;

/// <summary>Dependency-free smoke checks that can be run with CROWBAR_UI_SMOKE_TESTS=1.</summary>
public static class UiSmokeTests
{
    public static void Run()
    {
        var root = new ScreenPanel();
        var child = new Panel { TagName = "div" };
        child.AddClass("box");
        root.AddChild(child);
        var sheet = StyleSheet.Parse(".box { width: 100px; height: 40px; background-color: #ff0000ff; }");
        var layout = new YogaLayoutEngine();
        layout.Layout(root, 320, 200, sheet);
        Debug.Assert(child.Layout.Width == 100 && child.Layout.Height == 40);
        using var renderer = new SkiaUiRenderer { StyleSheet = sheet };
        renderer.Resize(320, 200);
        var pixels = renderer.Render(root);
        if (pixels.Length != 320 * 200 * 4) throw new InvalidOperationException("UI raster size is invalid.");
        if (layout.LayoutPasses != 1) throw new InvalidOperationException("Unexpected UI layout pass count.");
        var button = new Button("Click");
        button.SetInlineStyle("width", "80px");
        button.SetInlineStyle("height", "32px");
        var clicked = false;
        button.Clicked += _ => clicked = true;
        root.AddChild(button);
        root.SetViewport(320, 200);
        renderer.MarkDirty();
        renderer.Render(root);
        var input = new UiSystem();
        input.Screen.SetViewport(320, 200);
        input.Screen.AddChild(button);
        input.Renderer.Resize(320, 200);
        input.Render();
        input.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        if (!clicked) throw new InvalidOperationException("Button input routing failed.");
        input.Dispose();
    }
}
