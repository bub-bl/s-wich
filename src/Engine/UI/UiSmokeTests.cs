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
    }
}
