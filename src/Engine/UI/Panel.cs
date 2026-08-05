using System.Collections.ObjectModel;
using System.Text;

namespace Crowbar.Engine.UI;

public class Panel
{
    private readonly List<Panel> _children = [];
    private readonly HashSet<string> _classes = new(StringComparer.OrdinalIgnoreCase);
    internal bool LayoutDirty { get; private set; } = true;

    public Panel? Parent { get; private set; }
    public IReadOnlyList<Panel> Children => new ReadOnlyCollection<Panel>(_children);
    public string TagName { get; set; } = "div";
    public string? Id { get; set; }
    public IReadOnlySet<string> Classes => _classes;
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> InlineStyle { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Text { get; set; } = string.Empty;
    public ComputedStyle ComputedStyle { get; internal set; } = new();
    public UiRect Layout { get; internal set; }
    public bool IsVisible { get; set; } = true;
    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }
    public bool IsFocused { get; private set; }
    public event Action<Panel>? PointerEnter;
    public event Action<Panel>? PointerExit;

    public void AddClass(string value) { if (_classes.Add(value)) Invalidate(); }
    public void RemoveClass(string value) { if (_classes.Remove(value)) Invalidate(); }
    public void AddChild(Panel child)
    {
        child.Parent?._children.Remove(child);
        child.Parent = this;
        _children.Add(child);
        Invalidate();
    }
    public void RemoveChild(Panel child) { if (_children.Remove(child)) { child.Parent = null; Invalidate(); } }
    public void ClearChildren() { foreach (var child in _children) child.Parent = null; _children.Clear(); Invalidate(); }
    public void SetInlineStyle(string key, string value) { InlineStyle[key] = value; Invalidate(); }
    public void Invalidate() { LayoutDirty = true; Parent?.Invalidate(); }
    internal void SetHovered(bool value)
    {
        if (IsHovered == value) return;
        IsHovered = value;
        if (value) PointerEnter?.Invoke(this); else PointerExit?.Invoke(this);
        Invalidate();
    }
    internal void SetPressed(bool value) { if (IsPressed != value) { IsPressed = value; Invalidate(); } }
    internal void SetFocused(bool value) { if (IsFocused != value) { IsFocused = value; Invalidate(); } }
    internal void ClearDirty() { LayoutDirty = false; foreach (var child in _children) child.ClearDirty(); }
}

public abstract class PanelComponent : Panel
{
    private int _lastBuildHash;
    private bool _built;
    public bool StateDirty { get; private set; } = true;
    public string? RazorFile { get; internal set; }
    public StyleSheet? StyleSheet { get; private set; }

    protected virtual int BuildHash() => 0;
    protected virtual void OnTreeFirstBuilt() { }
    protected virtual void OnTreeBuilt() { }
    public void StateHasChanged() { StateDirty = true; Invalidate(); }
    internal bool NeedsBuild() => StateDirty || !_built || _lastBuildHash != BuildHash();
    internal void MarkBuilt(StyleSheet? styleSheet)
    {
        StyleSheet = styleSheet;
        _lastBuildHash = BuildHash();
        StateDirty = false;
        if (!_built) { _built = true; OnTreeFirstBuilt(); }
        OnTreeBuilt();
    }
}

public sealed class ScreenPanel : Panel
{
    public float Scale { get; set; } = 1;
    public float Opacity { get; set; } = 1;
    public int ZIndex { get; set; }
    public bool AutoScreenScale { get; set; }
    public float ScreenWidth { get; private set; }
    public float ScreenHeight { get; private set; }

    public void SetViewport(float width, float height)
    {
        ScreenWidth = width; ScreenHeight = height;
        Layout = new UiRect(0, 0, width / Scale, height / Scale);
        Invalidate();
    }
}
