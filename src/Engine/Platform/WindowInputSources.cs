using Crowbar.UI;
using System.Runtime.InteropServices;
using System.Diagnostics;

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
    private const double KeyRepeatDelaySeconds = 0.4;
    private const double KeyRepeatIntervalSeconds = 0.02;
    private readonly Func<nint> _windowHandle;
    private readonly bool[] _previousKeys = new bool[256];
    private readonly bool[] _previousButtons = new bool[5];
    private readonly double[] _nextKeyRepeat = new double[256];
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
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
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
            if (down != _previousKeys[key])
            {
                _previousKeys[key] = down;
                _nextKeyRepeat[key] = down ? now + KeyRepeatDelaySeconds : 0;
                KeyChanged?.Invoke(new KeyEvent(key, down, false, down ? TranslateKey(key) : null));
            }
            else if (down && now >= _nextKeyRepeat[key])
            {
                _nextKeyRepeat[key] = now + KeyRepeatIntervalSeconds;
                KeyChanged?.Invoke(new KeyEvent(key, true, true, TranslateKey(key)));
            }
        }
    }

    public void Dispose() { }
    private static readonly int[] ButtonKeys = [0x01, 0x02, 0x04, 0x05, 0x06];

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetKeyboardState(byte[] keyState);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, [Out] char[] characters, int characterCount, uint flags, nint keyboardLayout);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window, ref Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }

    private static string? TranslateKey(int key)
    {
        var state = new byte[256];
        if (GetKeyboardState(state) == 0) return null;

        var characters = new char[8];
        var count = ToUnicodeEx((uint)key, MapVirtualKey((uint)key, 0), state, characters, characters.Length, 0, GetKeyboardLayout(0));
        if (count <= 0) return null;
        return new string(characters, 0, count);
    }
}
