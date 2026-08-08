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
        TestScopedHoverShorthand();
        TestAutoRegisteredComponents();
        TestRazorRouting();
        TestChildContent();
        TestChildContentTransitions();
        TestMultipleFragments();
        TestMixedFragments();
        TestChildComponentPositionShift();
    }

    private static void TestScopedHoverShorthand()
    {
        // Pseudo-class rules on a component with #RGB shorthand colors: the
        // scoped :hover rule must match AND its color must be applied (regression
        // for MyButton, whose `.btn:hover { background-color: #333 }` was silently
        // dropped because UiColor only accepted #RRGGBB/#RRGGBBAA).
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("HoverBtn", @"
<div class=""btn""><div class=""title"">@Label</div></div>
@code {
    [Microsoft.AspNetCore.Components.Parameter] public string Label { get; set; } = string.Empty;
}
", "HoverBtn", cssSource: @"
root { width: 300px; height: 40px; }
.btn { width: 300px; height: 40px; background-color: black; }
.btn:hover { background-color: #333; }
.title { color: #333; }
");
        ui.LoadRazor(@"
<div class=""root""><HoverBtn Label=""hi"" /></div>
", "HoverShorthandDemo");
        ui.Screen.SetViewport(640, 200);
        ui.Renderer.Resize(640, 200);
        ui.Render();

        var btn = Find(ui.Screen, p => p.Classes.Contains("btn"));
        var title = Find(ui.Screen, p => p.Classes.Contains("title"));
        if (btn is null || title is null) throw new InvalidOperationException("Hover shorthand test markup failed.");
        if (btn.ComputedStyle.BackgroundColor != new UiColor(0, 0, 0, 255))
            throw new InvalidOperationException($"Hover shorthand base color was not applied (bg={btn.ComputedStyle.BackgroundColor}).");
        if (title.ComputedStyle.Color != new UiColor(51, 51, 51, 255))
            throw new InvalidOperationException($"#RGB color shorthand was not parsed for text (color={title.ComputedStyle.Color}).");

        ui.ProcessPointerMove(btn.Layout.X + btn.Layout.Width / 2, btn.Layout.Y + btn.Layout.Height / 2);
        ui.Render();
        if (!btn.IsHovered)
            throw new InvalidOperationException("Hover state was not routed to the component panel.");
        if (btn.ComputedStyle.BackgroundColor != new UiColor(51, 51, 51, 255))
            throw new InvalidOperationException($"Scoped :hover rule did not change the component background (bg={btn.ComputedStyle.BackgroundColor}).");

        ui.ProcessPointerMove(639, 199);
        ui.Render();
        if (btn.ComputedStyle.BackgroundColor != new UiColor(0, 0, 0, 255))
            throw new InvalidOperationException("Component background did not restore after hover exit.");
    }

    private static void TestAutoRegisteredComponents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CrowbarRazorComponents_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "AutoLabel.razor"), @"
<div class=""auto-box""><span>@Label</span></div>
@code {
    [Microsoft.AspNetCore.Components.Parameter] public string Label { get; set; } = string.Empty;
}
");
            File.WriteAllText(Path.Combine(directory, "_Imports.razor"), "@inherits Crowbar.Engine.UI.RazorPanel");
            using var ui = new UiSystem();
            var registered = ui.RegisterRazorComponentsFromDirectory(directory);
            if (registered != 1)
                throw new InvalidOperationException($"Auto registration registered {registered} component(s) instead of 1 (underscore files must be skipped).");
            ui.LoadRazor("<AutoLabel Label=\"auto resolved\" />", "AutoRoot");
            ui.Screen.SetViewport(320, 120);
            ui.Renderer.Resize(320, 120);
            ui.Render();
            var label = Find(ui.Screen, p => p.Text == "auto resolved");
            if (label is null)
                throw new InvalidOperationException("Auto-registered component was not resolved from markup.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestRazorRouting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CrowbarRazorPages_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Home.razor"), "@page \"/\"\n@page \"/home\"\n<div class=\"home-page\"><span>Home</span></div>\n");
            File.WriteAllText(Path.Combine(directory, "Item.razor"), "@page \"/items/{id:int}\"\n<div><span>Item @Id</span></div>\n@code {\n    [Microsoft.AspNetCore.Components.Parameter] public int Id { get; set; }\n}\n");
            File.WriteAllText(Path.Combine(directory, "Nav.razor"), "@page \"/nav\"\n<div><button @onclick=\"GoHome\">Back</button></div>\n@code {\n    private void GoHome() { NavigateTo(\"/home\"); }\n}\n");
            using var ui = new UiSystem();
            ui.RegisterRazorComponentsFromDirectory(directory);
            if (ui.Pages.Count != 4)
                throw new InvalidOperationException($"Routing registered {ui.Pages.Count} route(s) instead of 4.");
            ui.Screen.SetViewport(640, 240);
            ui.Renderer.Resize(640, 240);

            ui.Navigate("/");
            ui.Render();
            if (ui.CurrentUrl != "/" || Find(ui.Screen, p => p.Text == "Home") is null)
                throw new InvalidOperationException("Razor routing default route / did not resolve the home page.");

            ui.Navigate("/home");
            ui.Render();
            if (Find(ui.Screen, p => p.Text == "Home") is null)
                throw new InvalidOperationException("Razor routing alias /home did not resolve the home page.");

            ui.Navigate("/items/42");
            ui.Render();
            if (Find(ui.Screen, p => p.Text == "Item 42") is null)
                throw new InvalidOperationException("Razor routing did not convert the {id} route parameter to the typed page parameter.");

            ui.Navigate("/items/abc");
            ui.Render();
            if (Find(ui.Screen, p => p.Text.Contains("404")) is null)
                throw new InvalidOperationException("Razor routing did not reject a route parameter failing the {id:int} constraint.");

            ui.Navigate("/missing");
            ui.Render();
            if (ui.CurrentUrl != "/missing" || Find(ui.Screen, p => p.Text.Contains("404")) is null)
                throw new InvalidOperationException("Razor routing did not render the 404 fallback for an unknown URL.");

            ui.Navigate("/nav");
            ui.Render();
            var button = Find(ui.Screen, p => p is Button);
            if (button is null) throw new InvalidOperationException("Razor routing page button was not found.");
            ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
            ui.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
            ui.Update();
            ui.Render();
            if (ui.CurrentUrl != "/home" || Find(ui.Screen, p => p.Text == "Home") is null)
                throw new InvalidOperationException("NavigateTo from a page did not navigate to /home.");

            using (var single = new UiSystem())
            {
                single.RegisterRazorComponentFromFile("Solo", Path.Combine(directory, "Home.razor"), "Solo");
                if (single.Pages.Count != 2)
                    throw new InvalidOperationException($"Single-file registration registered {single.Pages.Count} route(s) instead of 2.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestScopedRazorCss()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("ChildComp", @"
<div class=""box""><span class=""scoped-label"">Child</span></div>
", "ChildComp", cssSource: @"
root { width: 50px; }
.box { width: 50px; }
.scoped-label { color: #00ff00ff; }
");
        ui.LoadRazor(@"
<div class=""parent-box"">
  <span class=""scoped-label"">Parent</span>
  <ChildComp />
</div>
", className: "ParentComp", cssSource: @"
root { width: 99px; height: 99px; }
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

        // CSS isolation must stop at the component boundary: the child root is
        // still styled by its own scoped `root` rule, but the parent's scoped
        // `root` rule must not leak into it.
        var childRoot = Find(ui.Screen, p => p.TagName == "root" && !ReferenceEquals(p, ui.Content));
        if (childRoot is null) throw new InvalidOperationException("Child component root was not found.");
        if (childRoot.ComputedStyle.Width != 50)
            throw new InvalidOperationException($"Child root lost its own scoped style (width={childRoot.ComputedStyle.Width}).");
        if (childRoot.ComputedStyle.Height == 99)
            throw new InvalidOperationException("Parent scoped root rule leaked into the child component root.");
    }

    private static void TestReactiveRazor()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("CounterLabel", @"
<span>@Label</span>
@code {
    [Microsoft.AspNetCore.Components.Parameter] public string Label { get; set; } = string.Empty;
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

    private static void TestChildContent()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("Card", @"
<div class=""card""><div class=""inner"">@ChildContent</div></div>
@code {
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }
}
", "Card");
        ui.RegisterRazorComponent("NestedLabel", @"
<span>@Label</span>
@code {
    [Microsoft.AspNetCore.Components.Parameter] public string Label { get; set; } = string.Empty;
}
", "NestedLabel");
        ui.LoadRazor(@"
<div class=""root"">
  <Card>
    <span class=""card-title"">Card title</span>
    <input value=""@name"" @bind-value=""name"" />
    <NestedLabel Label=""inside card"" />
    <label>@name</label>
  </Card>
</div>
@code {
    private string name = ""first"";
}
", "ChildContentDemo");
        ui.Screen.SetViewport(640, 480);
        ui.Renderer.Resize(640, 480);
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Card title") is null)
            throw new InvalidOperationException("ChildContent static markup did not render.");
        if (Find(ui.Screen, p => p.Text == "first") is null)
            throw new InvalidOperationException("ChildContent parent expression did not render.");
        if (Find(ui.Screen, p => p.Text == "inside card") is null)
            throw new InvalidOperationException("Nested component inside ChildContent did not render.");
        var textInput = Find(ui.Screen, p => p is TextInput) as TextInput;
        if (textInput is null) throw new InvalidOperationException("ChildContent input was not created.");
        textInput.SetValue("second");
        ui.Update();
        ui.Render();
        var rerenderedInput = Find(ui.Screen, p => p is TextInput) as TextInput;
        if (rerenderedInput is null || rerenderedInput.Value != "second")
            throw new InvalidOperationException("ChildContent input lost its value across the parent rerender.");
        if (Find(ui.Screen, p => p.Text == "second") is null)
            throw new InvalidOperationException("ChildContent @bind-value did not update the parent state.");
        if (Find(ui.Screen, p => p.Text == "inside card") is null)
            throw new InvalidOperationException("Nested component inside ChildContent was not preserved across the rerender.");
    }

    private static void TestChildContentTransitions()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("ToggleCard", @"
<div class=""card""><div class=""inner"">@ChildContent</div></div>
@code {
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }
}
", "ToggleCard");
        ui.LoadRazor(@"
<div class=""root"">
  <button @onclick=""Toggle"">Toggle</button>
  @if (show) {
    <ToggleCard>
      <span class=""card-title"">Card title</span>
    </ToggleCard>
  }
</div>
@code {
    private bool show = true;
    private void Toggle() { show = !show; StateHasChanged(); }
}
", "ToggleDemo");
        ui.Screen.SetViewport(640, 480);
        ui.Renderer.Resize(640, 480);
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Card title") is null)
            throw new InvalidOperationException("ChildContent did not render before the transition test.");
        var button = Find(ui.Screen, p => p is Button);
        if (button is null) throw new InvalidOperationException("Transition test toggle button was not found.");
        // Toggle off: the child content (and the whole component) must disappear.
        ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        ui.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
        ui.Update();
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Card title") is not null)
            throw new InvalidOperationException("ChildContent was not removed when the component vanished.");
        // Toggle back on: content must be restored (empty -> populated transition).
        button = Find(ui.Screen, p => p is Button);
        if (button is null) throw new InvalidOperationException("Toggle button was lost after the removal.");
        ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        ui.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
        ui.Update();
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Card title") is null)
            throw new InvalidOperationException("ChildContent was not restored after being re-added.");
    }

    private static void TestMultipleFragments()
    {
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("Window", @"
<div class=""window"">
    @if (Header is not null)
    {
        <div class=""window-header"">@Header</div>
    }

    <div class=""window-body"">@Body</div>

    @if (Footer is not null)
    {
        <div class=""window-footer"">@Footer</div>
    }
</div>
@code {
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? Header { get; set; }
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? Body { get; set; }
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? Footer { get; set; }
}
", "Window");
        ui.LoadRazor(@"
<div class=""root"">
    <button @onclick=""ToggleHeader"">Toggle</button>
    <Window>
        @if (showHeader)
        {
            <Header><span class=""win-title"">Title @name</span></Header>
        }

        <Body>
            <span>Body content</span>
            <input value=""@name"" @bind-value=""name"" />
        </Body>

        <Footer><span class=""win-foot"">Footer @name</span></Footer>
    </Window>
</div>
@code {
    private string name = ""first"";
    private bool showHeader = true;
    private void ToggleHeader() { showHeader = !showHeader; StateHasChanged(); }
}
", "MultiFragmentDemo");
        ui.Screen.SetViewport(640, 480);
        ui.Renderer.Resize(640, 480);
        ui.Render();

        // All three named fragments render, in declaration order.
        if (Find(ui.Screen, p => p.Text == "Title first") is null)
            throw new InvalidOperationException("Header fragment did not render.");
        if (Find(ui.Screen, p => p.Text == "Body content") is null)
            throw new InvalidOperationException("Body fragment did not render.");
        if (Find(ui.Screen, p => p.Text == "Footer first") is null)
            throw new InvalidOperationException("Footer fragment did not render.");
        var texts = CollectTexts(ui.Screen);
        if (texts.IndexOf("Title first") >= texts.IndexOf("Body content") ||
            texts.IndexOf("Body content") >= texts.IndexOf("Footer first"))
            throw new InvalidOperationException("Named fragments rendered out of order.");

        // A bind inside a fragment updates the parent; the parent rerender
        // refreshes every fragment that reads the parent state.
        var textInput = Find(ui.Screen, p => p is TextInput) as TextInput;
        if (textInput is null) throw new InvalidOperationException("Fragment input was not created.");
        textInput.SetValue("second");
        ui.Update();
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Title second") is null || Find(ui.Screen, p => p.Text == "Footer second") is null)
            throw new InvalidOperationException("Parent rerender did not refresh all named fragments.");

        // Removing a LEADING region shifts the sibling indices (body/footer
        // move up); the input's preserved-state key misses but its value is
        // restored from the value="@name" attribute, and the removed region's
        // content must disappear.
        var button = Find(ui.Screen, p => p is Button);
        if (button is null) throw new InvalidOperationException("Toggle button was not found.");
        ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        ui.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
        ui.Update();
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Title second") is not null)
            throw new InvalidOperationException("Header fragment was not removed when its region vanished.");
        var shiftedInput = Find(ui.Screen, p => p is TextInput) as TextInput;
        if (shiftedInput is null || shiftedInput.Value != "second")
            throw new InvalidOperationException("Input value was lost when a leading region disappeared.");
        if (Find(ui.Screen, p => p.Text == "Body content") is null || Find(ui.Screen, p => p.Text == "Footer second") is null)
            throw new InvalidOperationException("Sibling fragments were lost when a leading region disappeared.");

        // Re-adding the region restores it.
        button = Find(ui.Screen, p => p is Button);
        if (button is null) throw new InvalidOperationException("Toggle button was lost after the removal.");
        ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
        ui.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
        ui.Update();
        ui.Render();
        if (Find(ui.Screen, p => p.Text == "Title second") is null)
            throw new InvalidOperationException("Header fragment was not restored when its region came back.");
    }

    private static void TestMixedFragments()
    {
        // A component using both the default ChildContent and named fragments,
        // with the parent providing the regions in a different order than the
        // component renders them: each fragment must be spliced at its own
        // marker, so the rendered order follows the component, not the parent.
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("Layout", @"
<div class=""layout"">
    <div class=""layout-head"">@Title</div>
    <div class=""layout-body"">@ChildContent</div>
    <div class=""layout-foot"">@Footer</div>
</div>
@code {
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? Title { get; set; }
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? Footer { get; set; }
}
", "Layout");
        ui.LoadRazor(@"
<div class=""root"">
    <Layout>
        <Footer><span class=""lf"">Tail</span></Footer>
        <span class=""cc"">Middle</span>
        <Title><span class=""lt"">Head</span></Title>
    </Layout>
</div>
", "MixedFragmentsDemo");
        ui.Screen.SetViewport(640, 480);
        ui.Renderer.Resize(640, 480);
        ui.Render();

        if (Find(ui.Screen, p => p.Text == "Head") is null || Find(ui.Screen, p => p.Text == "Middle") is null ||
            Find(ui.Screen, p => p.Text == "Tail") is null)
            throw new InvalidOperationException("Mixed ChildContent + named fragments did not all render.");
        var texts = CollectTexts(ui.Screen);
        if (texts.IndexOf("Head") >= texts.IndexOf("Middle") || texts.IndexOf("Middle") >= texts.IndexOf("Tail"))
            throw new InvalidOperationException("Fragments spliced in the parent's order instead of the component's order.");
    }

    private static void TestChildComponentPositionShift()
    {
        // Regression: child components are cached by positional key, so a
        // conditional sibling (an @if block) shifting every key by one used to
        // hand the component at a key a stale instance of the component that
        // previously occupied it, throwing when a parameter did not exist on
        // the recycled type (e.g. Value on a ChildContent-only component).
        using var ui = new UiSystem();
        ui.RegisterRazorComponent("MyButton", @"
<div class=""btn""><span>@Value</span></div>
@code {
    [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = string.Empty;
    protected override int BuildHash() => HashCode.Combine(Value);
}
", "MyButton");
        ui.RegisterRazorComponent("Card", @"
<div class=""card""><div class=""inner"">@ChildContent</div></div>
@code {
    [Microsoft.AspNetCore.Components.Parameter]
    public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }
}
", "Card");
        ui.LoadRazor(@"
<div class=""root"">
    <button @onclick=""Toggle"">Toggle</button>
    @if (show) { <label class=""extra"">Extra node</label> }
    <MyButton Value=""first"" />
    <MyButton Value=""second"" />
    <MyButton Value=""third"" />
    <Card><span class=""card-title"">Card content</span></Card>
</div>
@code {
    private bool show = true;
    private void Toggle() { show = !show; StateHasChanged(); }
}
", "PositionShiftDemo");
        ui.Screen.SetViewport(640, 480);
        ui.Renderer.Resize(640, 480);
        ui.Render();

        // Inserting the extra node (show=false -> true after the first toggle)
        // shifts the MyButton/Card keys down by one; removing it shifts them
        // back up. Neither transition may crash or mix up component instances.
        foreach (var expected in new[] { true, false, true })
        {
            if ((Find(ui.Screen, p => p.Text == "Extra node") is null) == expected)
                throw new InvalidOperationException("Position shift: conditional node visibility is wrong.");
            if (Find(ui.Screen, p => p.Text == "first") is null || Find(ui.Screen, p => p.Text == "third") is null)
                throw new InvalidOperationException("Position shift: parameterized component instance was lost.");
            if (Find(ui.Screen, p => p.Text == "Card content") is null)
                throw new InvalidOperationException("Position shift: child content component was lost.");
            var button = Find(ui.Screen, p => p is Button);
            if (button is null) throw new InvalidOperationException("Position shift: toggle button was not found.");
            ui.ProcessPointerDown(button.Layout.X + 1, button.Layout.Y + 1);
            ui.ProcessPointerUp(button.Layout.X + 1, button.Layout.Y + 1);
            ui.Update();
            ui.Render();
        }
    }

    private static List<string> CollectTexts(Panel panel)
    {
        var result = new List<string>();
        if (!string.IsNullOrEmpty(panel.Text)) result.Add(panel.Text);
        foreach (var child in panel.Children) result.AddRange(CollectTexts(child));
        return result;
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
