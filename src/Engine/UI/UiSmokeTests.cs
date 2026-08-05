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
        button.AddClass("hover-target");
        input.LoadStyles(".hover-target:hover { background-color: #00ff00ff; }");
        input.Screen.SetViewport(320, 200);
        input.Screen.AddChild(button);
        input.Renderer.Resize(320, 200);
        input.Render();
        input.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        if (!clicked) throw new InvalidOperationException("Button input routing failed.");
        if (!button.IsHovered || !button.IsPressed) throw new InvalidOperationException("UI hover/active state was not routed.");
        input.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
        if (button.IsPressed) throw new InvalidOperationException("UI active state was not cleared.");
        input.ProcessPointerMove(319, 199);
        if (button.IsHovered) throw new InvalidOperationException("UI hover exit was not routed.");
        input.Dispose();

        TestReactiveRazor();
    }

    private static void TestReactiveRazor()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("CounterLabel", "<span>Child component</span>", "CounterLabel");
        ui.LoadRazor(@"
<div class=""root"">
  <button @onclick=""Increment"">Count: @count</button>
  <CounterLabel />
  @if (count > 0) { <label>Visible</label> }
  @foreach (var item in items) { <span>@item</span> }
  <input value=""first"" @onchange=""Changed"" @bind-value=""name"" />
  <label>@name</label>
  @if (changed) { <label>Changed</label> }
</div>
@code {
    private int count;
    private string name = ""first"";
    private bool changed;
    private readonly string[] items = [""A"", ""B""];
    private void Increment() { count++; StateHasChanged(); }
    private void Changed(string value) { changed = value == ""second""; }
}
", "ReactiveDemo");
        ui.Screen.SetViewport(640, 480);
        ui.Renderer.Resize(640, 480);
        ui.Render();
        var button = Find(ui.Screen, p => p is Button);
        if (button is null || !Find(ui.Screen, p => p.Text == "A")!.Text.Equals("A")) throw new InvalidOperationException("Razor C# loop/expression rendering failed.");
        ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        ui.Update();
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Visible") is null || Find(ui.Screen, p => p.Text == "Count: 1") is null)
            throw new InvalidOperationException("Razor StateHasChanged rerender failed.");
        var textInput = Find(ui.Screen, p => p is TextInput) as TextInput;
        if (textInput is null) throw new InvalidOperationException("Razor input rendering failed.");
        textInput.SetValue("second");
        ui.Update();
        ui.Render();
        var bound = Find(ui.Screen, p => p.Text == "second") is not null;
        var changed = Find(ui.Screen, p => p.Text == "Changed") is not null;
        var child = Find(ui.Screen, p => p.Text == "Child component") is not null;
        if (!bound || !changed || !child)
            throw new InvalidOperationException($"Razor binding test failed (bound={bound}, onchange={changed}, child={child}).");
    }

    private static Panel? Find(Panel panel, Func<Panel, bool> predicate)
    {
        if (predicate(panel)) return panel;
        foreach (var child in panel.Children)
        {
            var result = Find(child, predicate);
            if (result is not null) return result;
        }
        return null;
    }
}
