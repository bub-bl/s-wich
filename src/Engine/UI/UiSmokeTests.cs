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
        var buttonText = button.Children[0];
        button.SetInlineStyle("padding", "10px");
        button.SetInlineStyle("text-align", "center");
        button.SetInlineStyle("vertical-align", "center");
        button.SetInlineStyle("line-height", "32px");
        renderer.MarkDirty();
        renderer.Render(root);
        if (button.ComputedStyle.PaddingLeft != 10 || buttonText.ComputedStyle.TextAlign != "center" || buttonText.ComputedStyle.VerticalAlign != "center" || buttonText.ComputedStyle.LineHeight != 32)
            throw new InvalidOperationException("Button layout/text styles were not recalculated or inherited.");
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
        var edit = new TextInput();
        edit.SetInlineStyle("width", "160px");
        edit.SetInlineStyle("height", "32px");
        edit.SetValue("Je t'aime");
        input.Screen.AddChild(edit);
        input.Render();
        input.ProcessPointerDown(edit.Layout.Right - 1, edit.Layout.Y + 1);
        input.ProcessKey(0x43, true);
        if (edit.Value != "Je t'aimec") throw new InvalidOperationException("Text input did not preserve lowercase input.");
        input.ProcessPointerDown(edit.Layout.Right - 1, edit.Layout.Y + 1);
        input.ProcessKey(0x43, true);
        if (edit.Value != "Je t'aimecc") throw new InvalidOperationException("Text input lost its value after refocusing.");
        input.ProcessKey(0x10, true);
        input.ProcessKey(0x31, true);
        input.ProcessKey(0x10, false);
        if (edit.Value != "Je t'aimecc!") throw new InvalidOperationException("Shift symbols were not inserted.");
        input.ProcessKey(0x11, true);
        input.ProcessKey(0x41, true);
        input.ProcessKey(0x11, false);
        if (!edit.HasSelection || edit.SelectionStart != 0 || edit.SelectionEnd != edit.Value.Length)
            throw new InvalidOperationException("Ctrl+A did not select the complete input.");
        input.ProcessKey(0x08, true);
        if (edit.Value != string.Empty) throw new InvalidOperationException("Backspace did not delete the selection.");
        edit.SetValue("one two three");
        input.ProcessPointerDown(edit.Layout.Right - 1, edit.Layout.Y + 1);
        input.ProcessKey(0x11, true);
        input.ProcessKey(0x08, true);
        input.ProcessKey(0x11, false);
        if (edit.Value != "one two ") throw new InvalidOperationException("Ctrl+Backspace did not delete one word.");
        edit.SetValue("select me");
        using (var testFont = new SkiaSharp.SKFont { Size = edit.ComputedStyle.FontSize })
        {
            var targetX = edit.Layout.X + edit.ComputedStyle.PaddingLeft + testFont.MeasureText("select");
            input.ProcessPointerDown(edit.Layout.X + edit.ComputedStyle.PaddingLeft + 1, edit.Layout.Y + 1);
            input.ProcessPointerMove(targetX, edit.Layout.Y + 1);
            input.ProcessPointerUp(targetX, edit.Layout.Y + 1);
        }
        if (!edit.HasSelection) throw new InvalidOperationException("Mouse drag did not select text.");

        edit.SetValue("a   b");
        using (var testFont = new SkiaSharp.SKFont { Size = edit.ComputedStyle.FontSize })
        {
            // Click right on character 'b' (which is at prefix "a   ")
            var clickXOnB = edit.Layout.X + edit.ComputedStyle.PaddingLeft + testFont.MeasureText("a   ") + 2;
            input.ProcessPointerDown(clickXOnB, edit.Layout.Y + 1);
            input.ProcessPointerUp(clickXOnB, edit.Layout.Y + 1);
            if (edit.CaretIndex != 4) throw new InvalidOperationException($"Click near 'b' after spaces set CaretIndex to {edit.CaretIndex} instead of 4.");
        }
        input.Dispose();

        TestReactiveRazor();
        TestRazorDirectivesAndLifecycle();
        TestScopedRazorCss();
    }

    private static void TestScopedRazorCss()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("ChildComp", @"
<div class=""box""><span class=""scoped-label"">Child</span></div>
", "ChildComp", cssSource: @"
.box { width: 50px; }
.scoped-label { color: #00ff00ff; }
");
        ui.LoadRazor(@"
<div class=""parent-box"">
  <span class=""scoped-label"">Parent</span>
  <ChildComp />
</div>
", className: "ParentComp", cssSource: @"
.scoped-label { color: #ff0000ff; }
");
        ui.Screen.SetViewport(320, 200);
        ui.Renderer.Resize(320, 200);
        ui.Render();

        var parentLabel = Find(ui.Screen, p => p.Text == "Parent");
        var childLabel = Find(ui.Screen, p => p.Text == "Child");
        if (parentLabel is null || childLabel is null)
            throw new InvalidOperationException("Scoped CSS smoke test markup failed.");

        if (parentLabel.ComputedStyle.Color != new UiColor(255, 0, 0, 255))
            throw new InvalidOperationException($"Parent label did not receive parent scoped style (color={parentLabel.ComputedStyle.Color}).");

        if (childLabel.ComputedStyle.Color != new UiColor(0, 255, 0, 255))
            throw new InvalidOperationException($"Child label did not receive child scoped style (color={childLabel.ComputedStyle.Color}).");
    }

    private static void TestReactiveRazor()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("CounterLabel", @"
<span>@Label</span>
@code {
    [Crowbar.Engine.UI.Parameter] public string Label { get; set; } = string.Empty;
}
", "CounterLabel");
        ui.LoadRazor(@"
<div class=""root"">
  <button @onclick=""@Increment"">Count: @count</button>
  <button @onclick=""@(() => Increment())"">LambdaCount: @count</button>
  <CounterLabel Label=""Child component"" />
  @if (count > 0) { <label>Visible</label> }
  @foreach (var item in items) { <span>@item</span> }
  <input value=""first"" @onchange=""Changed"" @bind-value=""@name"" />
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
        ui.ProcessPointerDown(textInput.Layout.Right - 1, textInput.Layout.Y + 1);
        ui.ProcessKey(0x43, true);
        ui.Update();
        ui.Render();
        var rerenderedInput = Find(ui.Screen, p => p is TextInput) as TextInput;
        if (rerenderedInput is null || rerenderedInput.Value != "secondc" || !rerenderedInput.IsFocused)
            throw new InvalidOperationException("Razor input lost its value or focus during binding rerender.");
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

    private static void TestRazorDirectivesAndLifecycle()
    {
        using var ui = new UiSystem();
        ui.LoadRazor(@"
@inherits Crowbar.Engine.UI.RazorSmokeBase
@implements Crowbar.Engine.UI.IRazorSmokeContract
@using System.Text
@namespace Crowbar.Engine.UI.Smoke.Generated
@{ var inline = new StringBuilder(""inline"").ToString(); }
<div><span>@inline</span><span>@BuildVersion</span></div>
@code {
    protected override void OnInitialized() { base.OnInitialized(); }
    protected override void OnAfterRender(bool firstRender) { AfterRenderCount++; }
}
", "DirectiveDemo");
        ui.Screen.SetViewport(320, 120);
        ui.Renderer.Resize(320, 120);
        ui.Render();
        if (ui.Content is not RazorSmokeBase component || component is not IRazorSmokeContract || component.GetType().Namespace != "Crowbar.Engine.UI.Smoke.Generated")
            throw new InvalidOperationException("Razor @inherits/@implements failed.");
        if (component.InitializedCount != 1 || component.ParametersCount != 1 || component.AfterRenderCount != 1)
            throw new InvalidOperationException("Razor lifecycle initial ordering failed.");

        component.AllowRender = false;
        component.BuildVersion = 1;
        ui.Update();
        if (component.AfterRenderCount != 1) throw new InvalidOperationException("Razor ShouldRender did not suppress rendering.");
        component.AllowRender = true;
        component.StateHasChanged();
        ui.Update();
        if (component.AfterRenderCount != 2 || component.InitializedCount != 1)
            throw new InvalidOperationException("Razor BuildHash/lifecycle rerender failed.");
    }
}

public interface IRazorSmokeContract { }

public abstract class RazorSmokeBase : RazorTemplateBase
{
    public int InitializedCount { get; protected set; }
    public int ParametersCount { get; protected set; }
    public int AfterRenderCount { get; protected set; }
    public bool AllowRender { get; set; } = true;
    public int BuildVersion { get; set; }
    protected override bool ShouldRender() => AllowRender;
    protected override int BuildHash() => BuildVersion;
    protected override void OnInitialized() => InitializedCount++;
    protected override void OnParametersSet() => ParametersCount++;
}
