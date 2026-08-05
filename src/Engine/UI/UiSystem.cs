namespace Crowbar.Engine.UI;

public sealed partial class UiSystem : IDisposable
{
    public ScreenPanel Screen { get; } = new();
    public SkiaUiRenderer Renderer { get; } = new();
    public Panel? Content { get; private set; }
    public StyleSheet? StyleSheet { get; private set; }
    public bool IsDirty => Renderer.IsDirty || Screen.LayoutDirty || Screen.Layout is { Width: 0 };

    public void SetViewport(int width, int height) => Renderer.Resize(width, height);

    public void LoadRazor(string source, string className = "Root")
    {
        Content = new RazorComponentFactory().Compile(source, className, typeof(PanelComponent), typeof(UiSystem).Assembly);
        Screen.ClearChildren();
        Screen.AddChild(Content);
        Renderer.MarkDirty();
    }

    public void LoadStyles(string css) { StyleSheet = Crowbar.Engine.UI.StyleSheet.Parse(css); Renderer.StyleSheet = StyleSheet; Renderer.MarkDirty(); }

    public ReadOnlyMemory<byte> Render()
    {
        if (Screen.LayoutDirty) Renderer.MarkDirty();
        return Renderer.Render(Screen);
    }
    public void Dispose() { StopWatching(); Renderer.Dispose(); }
}
