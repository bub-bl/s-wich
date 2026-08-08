using System.Collections.ObjectModel;
using System.Text;

namespace Crowbar.UI;

public class Panel
{
    private readonly List<Panel> _children = [];
    private readonly HashSet<string> _classes = new(StringComparer.OrdinalIgnoreCase);
    private ComputedStyle? _styleTarget;
    private ComputedStyle? _styleFrom;
    private float _styleAnimationTime;
    private bool _styleAnimating;
    private bool _hasComputedStyle;
    private bool _isEnabled = true;
    private bool _isChecked;
    internal bool LayoutDirty { get; private set; } = true;

    private readonly HashSet<string> _scopeIds = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> ScopeIds => _scopeIds;
    public Panel? Parent { get; private set; }
    public IReadOnlyList<Panel> Children => new ReadOnlyCollection<Panel>(_children);
    public string TagName { get; set; } = "div";
    public string? Id { get; set; }
    public IReadOnlySet<string> Classes => _classes;
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> InlineStyle { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Text { get; set; } = string.Empty;

    public void AddScope(string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId)) return;
        if (_scopeIds.Add(scopeId))
        {
            Attributes[scopeId] = string.Empty;
            Invalidate();
        }
    }
    public bool HasScope(string scopeId) => !string.IsNullOrEmpty(scopeId) && _scopeIds.Contains(scopeId);
    public ComputedStyle ComputedStyle { get; internal set; } = new();
    public UiRect Layout { get; internal set; }
    public bool IsVisible { get; set; } = true;
    public bool IsEnabled { get => _isEnabled; set { if (_isEnabled != value) { _isEnabled = value; Invalidate(); } } }
    public bool IsChecked { get => _isChecked; set { if (_isChecked != value) { _isChecked = value; Invalidate(); } } }
    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }
    public bool IsFocused { get; private set; }
    public event Action<Panel>? PointerEnter;
    public event Action<Panel>? PointerExit;
    public event Action<Panel, UiPointerEvent>? PointerDown;
    public event Action<Panel, UiPointerEvent>? PointerUp;

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
    internal void ApplyComputedStyle(ComputedStyle target)
    {
        if (!_hasComputedStyle)
        {
            ComputedStyle = target;
            _styleTarget = target.Clone();
            _hasComputedStyle = true;
            return;
        }

        if (_styleTarget is not null && StylesEqual(_styleTarget, target))
        {
            // ComputedStyle can receive inherited values during the layout
            // pass. Restore the unmodified target before inheritance is
            // applied again, otherwise values such as opacity accumulate.
            if (!_styleAnimating) ComputedStyle = target;
            return;
        }
        var duration = target.TransitionDuration > 0 ? target.TransitionDuration : ComputedStyle.TransitionDuration;
        var canAnimate = duration > 0 && HasAnimatableTransition(target);
        _styleTarget = target.Clone();
        if (!canAnimate)
        {
            ComputedStyle = target;
            _styleAnimating = false;
            return;
        }
        _styleFrom = ComputedStyle.Clone();
        _styleAnimationTime = 0;
        _styleAnimating = true;
    }

    internal bool AdvanceStyleAnimation(float deltaTime)
    {
        if (!_styleAnimating || _styleTarget is null || _styleFrom is null) return false;
        var duration = Math.Max(0.001f, _styleTarget.TransitionDuration > 0 ? _styleTarget.TransitionDuration : _styleFrom.TransitionDuration);
        _styleAnimationTime = Math.Min(duration, _styleAnimationTime + Math.Max(0, deltaTime));
        var t = _styleAnimationTime / duration;
        if (_styleTarget.TransitionTimingFunction.Equals("ease", StringComparison.OrdinalIgnoreCase)) t = t * t * (3 - 2 * t);
        ComputedStyle = Interpolate(_styleFrom, _styleTarget, t);
        Invalidate();
        if (_styleAnimationTime >= duration)
        {
            ComputedStyle = _styleTarget;
            _styleAnimating = false;
        }
        return true;
    }

    /// <summary>
    /// True when the transition-property list names at least one registered
    /// animatable property. Data-driven through <see cref="CssProperties"/> so
    /// a newly registered animatable property transitions without extra wiring.
    /// </summary>
    private static bool HasAnimatableTransition(ComputedStyle style)
    {
        if (style.TransitionProperty.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        var names = style.TransitionProperty.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return CssProperties.All.Any(p => p.Animatable && names.Any(n => n.Equals(p.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool StylesEqual(ComputedStyle a, ComputedStyle b)
    {
        foreach (var property in CssProperties.All)
            if (!property.ValuesEqual(property.GetValue(a), property.GetValue(b))) return false;
        return true;
    }

    private static ComputedStyle Interpolate(ComputedStyle from, ComputedStyle to, float t)
    {
        var result = to.Clone();
        foreach (var property in CssProperties.All.Where(p => p.Animatable))
        {
            var value = property.Lerp(property.GetValue(from), property.GetValue(to), t);
            if (value is not null) property.SetValue(result, value);
        }
        return result;
    }
    internal void SetHovered(bool value)
    {
        if (IsHovered == value) return;
        IsHovered = value;
        if (value) PointerEnter?.Invoke(this); else PointerExit?.Invoke(this);
        Invalidate();
    }
    internal void SetPressed(bool value) { if (IsPressed != value) { IsPressed = value; Invalidate(); } }
    internal void SetFocused(bool value) { if (IsFocused != value) { IsFocused = value; Invalidate(); } }
    internal void RaisePointerDown(UiPointerEvent e) => PointerDown?.Invoke(this, e);
    internal void RaisePointerUp(UiPointerEvent e) => PointerUp?.Invoke(this, e);
    internal void ClearDirty() { LayoutDirty = false; foreach (var child in _children) child.ClearDirty(); }
}

public abstract class PanelComponent : Panel
{
    private int _lastBuildHash;
    private int? _skippedBuildHash;
    private bool _built;
    public bool StateDirty { get; private set; } = true;
    public string? RazorFile { get; internal set; }
    public StyleSheet? StyleSheet { get; private set; }
    internal Action? StateChanged { get; set; }

    protected virtual int BuildHash() => 0;
    protected virtual bool ShouldRender() => true;
    protected virtual void OnTreeFirstBuilt() { }
    protected virtual void OnTreeBuilt() { }
    public void StateHasChanged() { _skippedBuildHash = null; StateDirty = true; Invalidate(); StateChanged?.Invoke(); }
    internal bool NeedsBuild()
    {
        var hash = BuildHash();
        if (!StateDirty && _skippedBuildHash == hash) return false;
        return StateDirty || !_built || _lastBuildHash != hash;
    }
    internal bool CanRender() => ShouldRender();
    internal void MarkRenderSkipped() { _skippedBuildHash = BuildHash(); _lastBuildHash = _skippedBuildHash.Value; StateDirty = false; }
    internal bool MarkBuilt(StyleSheet? styleSheet)
    {
        var firstRender = !_built;
        StyleSheet = styleSheet;
        _lastBuildHash = BuildHash();
        StateDirty = false;
        if (firstRender) { _built = true; OnTreeFirstBuilt(); }
        OnTreeBuilt();
        return firstRender;
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
