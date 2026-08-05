namespace Crowbar.Engine.UI;

public sealed partial class UiSystem : IDisposable
{
    public ScreenPanel Screen { get; } = new();
    public SkiaUiRenderer Renderer { get; } = new();
    public Panel? Content { get; private set; }
    public StyleSheet? StyleSheet { get; private set; }
    public bool IsDirty => Renderer.IsDirty || Screen.LayoutDirty || Screen.Layout is { Width: 0 };
    private RazorTemplateBase? _razorRoot;
    private RazorComponentFactory? _razorFactory;
    private bool _razorRenderPending;
    private readonly Dictionary<string, Func<RazorTemplateBase>> _razorComponents = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterRazorComponent(string tagName, string source, string className)
    {
        var factory = new RazorComponentFactory();
        _razorComponents[tagName] = () => factory.CompileTemplate(source, className, typeof(PanelComponent), typeof(UiSystem).Assembly);
    }

    public void SetViewport(int width, int height) => Renderer.Resize(width, height);

    public void LoadRazor(string source, string className = "Root")
    {
        _razorFactory = new RazorComponentFactory(_razorComponents);
        _razorRoot = _razorFactory.CompileTemplate(source, className, typeof(PanelComponent), typeof(UiSystem).Assembly);
        _razorRoot.StateChanged = () => _razorRenderPending = true;
        Content = _razorFactory.BuildTree(_razorRoot);
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
    internal void RenderRazorIfNeeded()
    {
        if (!_razorRenderPending || _razorRoot is null) return;
        _razorRenderPending = false;
        var old = Content;
        Content = (_razorFactory ?? new RazorComponentFactory(_razorComponents)).BuildTree(_razorRoot);
        if (old is not null) Screen.RemoveChild(old);
        Screen.AddChild(Content);
        Renderer.MarkDirty();
    }
    public void Dispose() { StopWatching(); Renderer.Dispose(); }
}
