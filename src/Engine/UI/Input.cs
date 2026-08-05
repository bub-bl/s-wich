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
        _hovered = hit;
        if (hit is not null) PointerMoved?.Invoke(hit, new UiPointerEvent(x, y));
        return hit;
    }

    public Panel? ProcessPointerDown(float x, float y, int button = 0)
    {
        var hit = ProcessPointerMove(x, y);
        if (hit is null) return null;
        FocusedPanel = hit;
        var e = new UiPointerEvent(x, y, button);
        PointerDown?.Invoke(hit, e);
        _captured = hit is Button ? hit : null;
        for (var current = hit; current is not null; current = current.Parent)
            if (current is Button buttonPanel) { buttonPanel.RaiseClicked(e); break; }
        return hit;
    }

    public Panel? ProcessPointerUp(float x, float y, int button = 0)
    {
        var hit = _captured ?? Screen.HitTest(x / Math.Max(0.01f, Screen.Scale), y / Math.Max(0.01f, Screen.Scale));
        if (hit is not null) PointerUp?.Invoke(hit, new UiPointerEvent(x, y, button));
        _captured = null;
        return hit;
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
}
