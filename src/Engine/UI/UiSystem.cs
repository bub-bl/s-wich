namespace Crowbar.Engine.UI;

public sealed partial class UiSystem : IDisposable
{
    public ScreenPanel Screen { get; } = new();
    public SkiaUiRenderer Renderer { get; } = new();
    public Panel? Content { get; private set; }
    private readonly Dictionary<string, StyleSheet> _scopedStyleSheets = new(StringComparer.OrdinalIgnoreCase);
    public StyleSheet? GlobalStyleSheet { get; private set; }
    public StyleSheet StyleSheet { get; private set; } = new();
    public bool IsDirty => Renderer.IsDirty || Screen.LayoutDirty || Screen.Layout is { Width: 0 };
    private RazorPanel? _razorRoot;
    private RazorComponentFactory? _razorFactory;
    private bool _razorRenderPending;
    private readonly Dictionary<string, Func<RazorPanel>> _razorComponents = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterRazorComponent(string tagName, string source, string className, string? cssSource = null)
    {
        var scopeId = $"b-{className.ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(cssSource))
        {
            LoadScopedStyles(tagName, cssSource, scopeId);
        }
        var factory = new RazorComponentFactory();
        _razorComponents[tagName] = () =>
        {
            var template = factory.CompileTemplate(source, className, typeof(PanelComponent), typeof(UiSystem).Assembly);
            template.ScopeId = scopeId;
            return template;
        };
    }

    public void RegisterRazorComponentFromFile(string tagName, string razorPath, string className)
    {
        razorPath = Path.GetFullPath(razorPath);
        var source = File.ReadAllText(razorPath);
        var associatedCss = GetAssociatedCssPath(razorPath);
        string? cssSource = File.Exists(associatedCss) ? File.ReadAllText(associatedCss) : null;
        RegisterRazorComponent(tagName, source, className, cssSource);
    }

    /// <summary>
    /// Discovers every .razor file under the given directory and registers it as
    /// a reusable component keyed by file name (e.g. <c>MyButton.razor</c>
    /// becomes the <c>&lt;MyButton&gt;</c> tag). Files whose name starts with an
    /// underscore (such as <c>_Imports.razor</c>) are skipped.
    /// </summary>
    public int RegisterRazorComponentsFromDirectory(string directory, bool recursive = true)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) return 0;
        var options = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var registered = 0;
        foreach (var razorPath in Directory.EnumerateFiles(directory, "*.razor", options))
        {
            var fileName = Path.GetFileName(razorPath);
            if (fileName.StartsWith("_", StringComparison.Ordinal)) continue;
            var className = Path.GetFileNameWithoutExtension(fileName);
            if (_razorComponents.ContainsKey(className))
                throw new InvalidOperationException($"Duplicate Razor component tag '{className}' found at '{razorPath}' (already registered).");
            RegisterRazorComponentFromFile(className, razorPath, className);
            registered++;
        }
        return registered;
    }

    public void SetViewport(int width, int height) => Renderer.Resize(width, height);

    public void LoadRazorFromFile(string razorPath, string className = "Root")
    {
        razorPath = Path.GetFullPath(razorPath);
        var source = File.ReadAllText(razorPath);
        var scopeId = $"b-{className.ToLowerInvariant()}";
        var scopedCssPath = GetAssociatedCssPath(razorPath);
        if (File.Exists(scopedCssPath))
        {
            var css = File.ReadAllText(scopedCssPath);
            LoadScopedStyles(scopedCssPath, css, scopeId);
        }
        LoadRazor(source, className);
    }

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

    public void LoadRazor(string source, string className, string cssSource)
    {
        var scopeId = $"b-{className.ToLowerInvariant()}";
        LoadScopedStyles(className, cssSource, scopeId);
        LoadRazor(source, className);
    }

    public void LoadStyles(string css)
    {
        GlobalStyleSheet = Crowbar.Engine.UI.StyleSheet.Parse(css);
        RebuildCombinedStyleSheet();
    }

    public void LoadScopedStyles(string key, string css, string scopeId)
    {
        _scopedStyleSheets[key] = Crowbar.Engine.UI.StyleSheet.Parse(css, scopeId);
        RebuildCombinedStyleSheet();
    }

    private void RebuildCombinedStyleSheet()
    {
        var combined = new StyleSheet();
        if (GlobalStyleSheet is not null)
        {
            combined.AddRules(GlobalStyleSheet.Rules);
        }
        foreach (var sheet in _scopedStyleSheets.Values)
        {
            combined.AddRules(sheet.Rules);
        }
        StyleSheet = combined;
        Renderer.StyleSheet = StyleSheet;
        Renderer.MarkDirty();
    }

    public static string GetAssociatedCssPath(string razorPath)
    {
        if (razorPath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            return razorPath + ".css";
        return Path.ChangeExtension(razorPath, ".razor.css");
    }

    public ReadOnlyMemory<byte> Render()
    {
        if (Screen.LayoutDirty) Renderer.MarkDirty();
        return Renderer.Render(Screen);
    }
    internal void RenderRazorIfNeeded()
    {
        if (_razorRoot is null || (!_razorRenderPending && !_razorRoot.NeedsBuild())) return;
        if (!_razorRoot.CanRender())
        {
            _razorRoot.MarkRenderSkipped();
            _razorRenderPending = false;
            return;
        }
        _razorRenderPending = false;
        var old = Content;
        var oldFocused = FocusedPanel;
        Content = (_razorFactory ?? new RazorComponentFactory(_razorComponents)).BuildTree(_razorRoot);
        if (old is not null) Screen.RemoveChild(old);
        Screen.AddChild(Content);
        if (oldFocused is TextInput && FindPanel<TextInput>(Content) is { } replacement)
        {
            replacement.SetFocused(true);
            FocusedPanel = replacement;
        }
        Renderer.MarkDirty();
    }

    private static T? FindPanel<T>(Panel panel) where T : Panel
    {
        if (panel is T match) return match;
        foreach (var child in panel.Children)
            if (FindPanel<T>(child) is { } nested) return nested;
        return null;
    }
    public void Dispose() { StopWatching(); Renderer.Dispose(); }
}
