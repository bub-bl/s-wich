using System.Runtime.InteropServices;

#pragma warning disable CS0067

namespace Crowbar.Engine.Platform;

internal sealed class NullWindowInputSource : IWindowInputSource
{
    public event Action<PointerMoveEvent>? PointerMoved;
    public event Action<PointerButtonEvent>? PointerButtonChanged;
    public event Action<PointerWheelEvent>? PointerWheelChanged;
    public event Action<KeyEvent>? KeyChanged;
    public void Update() { }
    public void Dispose() { }
}

internal sealed class WindowsWindowInputSource : IWindowInputSource
{
    private readonly Func<nint> _windowHandle;
    private readonly bool[] _previousKeys = new bool[256];
    private readonly bool[] _previousButtons = new bool[5];
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;

    public WindowsWindowInputSource(Func<nint> windowHandle) => _windowHandle = windowHandle;
    public event Action<PointerMoveEvent>? PointerMoved;
    public event Action<PointerButtonEvent>? PointerButtonChanged;
    public event Action<PointerWheelEvent>? PointerWheelChanged;
    public event Action<KeyEvent>? KeyChanged;

    public void Update()
    {
        var handle = _windowHandle();
        if (handle == 0 || GetForegroundWindow() != handle) return;
        if (GetCursorPos(out var point) && ScreenToClient(handle, ref point))
        {
            if (point.X != _lastX || point.Y != _lastY)
            {
                _lastX = point.X; _lastY = point.Y;
                PointerMoved?.Invoke(new PointerMoveEvent(point.X, point.Y));
            }
            for (var i = 0; i < 5; i++)
            {
                var down = (GetAsyncKeyState(ButtonKeys[i]) & 0x8000) != 0;
                if (down == _previousButtons[i]) continue;
                _previousButtons[i] = down;
                PointerButtonChanged?.Invoke(new PointerButtonEvent(point.X, point.Y, (PointerButton)i, down));
            }
        }
        for (var key = 8; key < _previousKeys.Length; key++)
        {
            var down = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (down == _previousKeys[key]) continue;
            _previousKeys[key] = down;
            KeyChanged?.Invoke(new KeyEvent(key, down, false));
        }
    }

    public void Dispose() { }
    private static readonly int[] ButtonKeys = [0x01, 0x02, 0x04, 0x05, 0x06];

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window, ref Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
}
