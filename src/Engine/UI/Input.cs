using Crowbar.Engine.Platform;

namespace Crowbar.Engine.UI;

public sealed partial class UiSystem
{
    private Panel? _hovered;
    private Panel? _captured;
    public Panel? FocusedPanel { get; private set; }
    public event Action<Panel, UiPointerEvent>? PointerMoved;
    public event Action<Panel, UiPointerEvent>? PointerDown;
    public event Action<Panel, UiPointerEvent>? PointerUp;
    public event Action<Panel, KeyEvent>? KeyChanged;
    public event Action<Panel, float, float>? PointerWheelChanged;

    public Panel? ProcessPointerMove(float x, float y)
    {
        var hit = Screen.HitTest(x / Math.Max(0.01f, Screen.Scale), y / Math.Max(0.01f, Screen.Scale));
        UpdateHoverPath(hit);
        _hovered = hit;
        if (hit is not null) PointerMoved?.Invoke(hit, new UiPointerEvent(x, y));
        return hit;
    }

    public Panel? ProcessPointerDown(float x, float y, int button = 0)
    {
        var hit = ProcessPointerMove(x, y);
        if (hit is null || !hit.IsEnabled) return null;
        UpdateFocus(hit);
        if (hit is TextInput textInput) textInput.FocusAtEnd();
        UpdatePressedPath(hit, true);
        var e = new UiPointerEvent(x, y, button);
        PointerDown?.Invoke(hit, e);
        for (var current = hit; current is not null; current = current.Parent) current.RaisePointerDown(e);
        _captured = hit is Button ? hit : null;
        for (var current = hit; current is not null; current = current.Parent)
            if (current is Button buttonPanel) { buttonPanel.RaiseClicked(e); break; }
        return hit;
    }

    public Panel? ProcessPointerUp(float x, float y, int button = 0)
    {
        var hit = _captured ?? Screen.HitTest(x / Math.Max(0.01f, Screen.Scale), y / Math.Max(0.01f, Screen.Scale));
        if (hit is not null) PointerUp?.Invoke(hit, new UiPointerEvent(x, y, button));
        if (hit is not null)
        {
            var e = new UiPointerEvent(x, y, button);
            for (var current = hit; current is not null; current = current.Parent) current.RaisePointerUp(e);
        }
        UpdatePressedPath(_captured ?? hit, false);
        _captured = null;
        return hit;
    }

    private void UpdateHoverPath(Panel? hit)
    {
        var oldPath = PathToRoot(_hovered).ToHashSet();
        var newPath = PathToRoot(hit).ToHashSet();
        foreach (var panel in oldPath.Except(newPath)) panel.SetHovered(false);
        foreach (var panel in newPath.Except(oldPath)) panel.SetHovered(true);
    }

    private void UpdatePressedPath(Panel? hit, bool pressed)
    {
        foreach (var panel in PathToRoot(hit)) panel.SetPressed(pressed);
    }

    private void UpdateFocus(Panel? panel)
    {
        if (FocusedPanel is not null && !ReferenceEquals(FocusedPanel, panel)) FocusedPanel.SetFocused(false);
        panel?.SetFocused(true);
        FocusedPanel = panel;
    }

    private static IEnumerable<Panel> PathToRoot(Panel? panel)
    {
        for (var current = panel; current is not null; current = current.Parent) yield return current;
    }

    public void ProcessPointerWheel(float x, float y, float deltaX, float deltaY)
    {
        var hit = Screen.HitTest(x / Math.Max(0.01f, Screen.Scale), y / Math.Max(0.01f, Screen.Scale));
        if (hit is not null) PointerWheelChanged?.Invoke(hit, deltaX, deltaY);
    }

    public void ProcessKey(int keyCode, bool isDown, bool isRepeat = false)
    {
        if (FocusedPanel is TextInput input) input.HandleKey(keyCode, isDown);
        if (FocusedPanel is not null) KeyChanged?.Invoke(FocusedPanel, new KeyEvent(keyCode, isDown, isRepeat));
    }

    private static IEnumerable<TextInput> Inputs(Panel panel)
    {
        if (panel is TextInput input) yield return input;
        foreach (var child in panel.Children)
            foreach (var nested in Inputs(child)) yield return nested;
    }
}
