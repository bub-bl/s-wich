namespace Crowbar.UI;

public sealed partial class UiSystem
{
    private Panel? _hovered;
    private Panel? _captured;
    private Panel? _scrollDragPanel;
    private bool _scrollDragVertical;
    public Panel? FocusedPanel { get; private set; }
    public event Action<Panel, UiPointerEvent>? PointerMoved;
    public event Action<Panel, UiPointerEvent>? PointerDown;
    public event Action<Panel, UiPointerEvent>? PointerUp;
    public event Action<Panel, KeyEvent>? KeyChanged;
    public event Action<Panel, float, float>? PointerWheelChanged;

    public Panel? ProcessPointerMove(float x, float y)
    {
        if (_scrollDragPanel is not null)
            ApplyScrollDrag(_scrollDragPanel, x, y, _scrollDragVertical);
        var hit = Screen.HitTest(x / Math.Max(0.01f, Screen.Scale), y / Math.Max(0.01f, Screen.Scale));
        UpdateHoverPath(hit);
        _hovered = hit;
        if (_captured is TextInput textInput) textInput.UpdatePointerSelection(x / Math.Max(0.01f, Screen.Scale));
        if (hit is not null) PointerMoved?.Invoke(hit, new UiPointerEvent(x, y));
        return hit;
    }

    public Panel? ProcessPointerDown(float x, float y, int button = 0)
    {
        var hit = ProcessPointerMove(x, y);
        if (hit is null || !hit.IsEnabled) return null;
        TryStartScrollDrag(hit, x, y, button);
        UpdateFocus(hit);
        if (hit is TextInput textInput && button == 0) textInput.BeginPointerSelection(x / Math.Max(0.01f, Screen.Scale));
        UpdatePressedPath(hit, true);
        var e = new UiPointerEvent(x, y, button);
        PointerDown?.Invoke(hit, e);
        for (var current = hit; current is not null; current = current.Parent) current.RaisePointerDown(e);
        _captured = hit is Button or TextInput ? hit : null;
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
        if (_captured is TextInput textInput && button == 0) textInput.EndPointerSelection();
        _captured = null;
        _scrollDragPanel = null;
        _scrollDragVertical = false;
        return hit;
    }

    /// <summary>Starts a scrollbar drag when the press lands on a visible scrollbar of the hit container.</summary>
    private void TryStartScrollDrag(Panel hit, float x, float y, int button)
    {
        if (button != 0 || !hit.IsScrollContainer) return;
        if (ScrollBars.HitTestVertical(hit, x, y))
        {
            _scrollDragPanel = hit;
            _scrollDragVertical = true;
            ApplyScrollDrag(hit, x, y, vertical: true);
        }
        else if (ScrollBars.HitTestHorizontal(hit, x, y))
        {
            _scrollDragPanel = hit;
            _scrollDragVertical = false;
            ApplyScrollDrag(hit, x, y, vertical: false);
        }
    }

    private static void ApplyScrollDrag(Panel panel, float x, float y, bool vertical)
    {
        var offset = ScrollBars.OffsetFromPoint(panel, vertical ? y : x, vertical);
        if (vertical) panel.ScrollTo(panel.ScrollX, offset);
        else panel.ScrollTo(offset, panel.ScrollY);
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

    private const float WheelScrollStep = 40f;

    private static IEnumerable<Panel> PathToRoot(Panel? panel)
    {
        for (var current = panel; current is not null; current = current.Parent) yield return current;
    }

    public void ProcessPointerWheel(float x, float y, float deltaX, float deltaY)
    {
        var hit = Screen.HitTest(x / Math.Max(0.01f, Screen.Scale), y / Math.Max(0.01f, Screen.Scale));
        if (hit is null) return;
        PointerWheelChanged?.Invoke(hit, deltaX, deltaY);
        // Scroll the nearest scrollable ancestor under the cursor: vertical wheel
        // deltas prefer vertical scrolling, horizontal deltas horizontal. A delta
        // is reused on the other axis when the preferred one cannot scroll.
        for (var current = hit; current is not null; current = current.Parent)
        {
            if (deltaY != 0 && current.CanScrollVertically) { current.ScrollBy(0, -deltaY * WheelScrollStep); return; }
            if (deltaX != 0 && current.CanScrollHorizontally) { current.ScrollBy(deltaX * WheelScrollStep, 0); return; }
            if (deltaY != 0 && current.CanScrollHorizontally) { current.ScrollBy(-deltaY * WheelScrollStep, 0); return; }
            if (deltaX != 0 && current.CanScrollVertically) { current.ScrollBy(0, deltaX * WheelScrollStep); return; }
        }
    }

    public void ProcessKey(int keyCode, bool isDown, bool isRepeat = false)
    {
        ProcessKey(new KeyEvent(keyCode, isDown, isRepeat));
    }

    public void ProcessKey(KeyEvent keyEvent)
    {
        if (FocusedPanel is TextInput input) input.HandleKey(keyEvent.KeyCode, keyEvent.IsDown, keyEvent.Text);
        else if (FocusedPanel is not null) TryScrollFromKeyboard(FocusedPanel, keyEvent);
        if (FocusedPanel is not null) KeyChanged?.Invoke(FocusedPanel, keyEvent);
    }

    /// <summary>Scrolls the nearest scrollable ancestor of the focused panel with the arrow/page keys.</summary>
    private static void TryScrollFromKeyboard(Panel focused, KeyEvent keyEvent)
    {
        if (!keyEvent.IsDown) return;
        var scrollable = PathToRoot(focused).FirstOrDefault(p => p.CanScrollVertically || p.CanScrollHorizontally);
        if (scrollable is null) return;
        switch (keyEvent.KeyCode)
        {
            case 0x26: scrollable.ScrollBy(0, -WheelScrollStep); break; // Up
            case 0x28: scrollable.ScrollBy(0, WheelScrollStep); break; // Down
            case 0x25: scrollable.ScrollBy(-WheelScrollStep, 0); break; // Left
            case 0x27: scrollable.ScrollBy(WheelScrollStep, 0); break; // Right
            case 0x21: scrollable.ScrollBy(0, -scrollable.ClientHeight); break; // PageUp
            case 0x22: scrollable.ScrollBy(0, scrollable.ClientHeight); break; // PageDown
            case 0x24: scrollable.ScrollTo(0, 0); break; // Home
            case 0x23: scrollable.ScrollTo(scrollable.ScrollX, scrollable.MaxScrollY); break; // End
        }
    }

    private static IEnumerable<TextInput> Inputs(Panel panel)
    {
        if (panel is TextInput input) yield return input;
        foreach (var child in panel.Children)
            foreach (var nested in Inputs(child)) yield return nested;
    }
}
