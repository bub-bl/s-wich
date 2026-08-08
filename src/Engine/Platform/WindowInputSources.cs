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

    // Mouse wheel messages carry coordinates and deltas that polling APIs cannot
    // see, so the window procedure is subclassed once to forward them here.
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;
    private const int WmNcDestroy = 0x0082;
    private const int GwlpWndProc = -4;
    private delegate nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam);
    private static readonly Dictionary<nint, WindowsWindowInputSource> SubclassedWindows = new();
    private static WindowProc? _subclassProc; // Kept alive for the process lifetime.
    private nint _originalWndProc;
    private nint _subclassedHandle;
    private bool _subclassed;

    public WindowsWindowInputSource(Func<nint> windowHandle) => _windowHandle = windowHandle;
    public event Action<PointerMoveEvent>? PointerMoved;
    public event Action<PointerButtonEvent>? PointerButtonChanged;
    public event Action<PointerWheelEvent>? PointerWheelChanged;
    public event Action<KeyEvent>? KeyChanged;

    public void Update()
    {
        var handle = _windowHandle();
        if (handle == 0) return;
        EnsureSubclassed(handle);
        if (GetForegroundWindow() != handle) return;
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

    private void EnsureSubclassed(nint handle)
    {
        if (_subclassed || handle == 0) return;
        _subclassProc ??= SubclassProc;
        var original = SetWindowLongPtr(handle, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_subclassProc));
        if (original != 0)
        {
            _originalWndProc = original;
            _subclassedHandle = handle;
            SubclassedWindows[handle] = this;
            _subclassed = true;
        }
    }

    private static nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if ((msg is WmMouseWheel or WmMouseHWheel) && SubclassedWindows.TryGetValue(hWnd, out var source))
        {
            var delta = (short)((uint)wParam >> 16); // HIWORD: multiples of 120 per notch.
            if (delta != 0 && source._originalWndProc != 0)
            {
                var point = new Point { X = (short)((uint)lParam & 0xFFFF), Y = (short)((uint)lParam >> 16) };
                if (ScreenToClient(hWnd, ref point))
                {
                    var normalized = delta / 120f;
                    source.PointerWheelChanged?.Invoke(new PointerWheelEvent(
                        point.X, point.Y,
                        msg == WmMouseHWheel ? normalized : 0,
                        msg == WmMouseWheel ? normalized : 0));
                }
            }
        }
        // The final WM_NCDESTROY must still reach the original procedure, and the
        // instance must forget its handle: the window is gone, so a later Dispose
        // must not touch a dead handle.
        if (msg == WmNcDestroy && SubclassedWindows.Remove(hWnd, out var destroyed))
            destroyed._subclassed = false;
        var original = SubclassedWindows.TryGetValue(hWnd, out var instance) ? instance._originalWndProc : 0;
        return original != 0 ? CallWindowProc(original, hWnd, msg, wParam, lParam) : 0;
    }

    public void Dispose()
    {
        if (!_subclassed) return;
        // Restore the original window procedure using the handle captured when
        // subclassing. Re-resolving the handle through the GLFW window is unsafe
        // during shutdown: the window may already be destroyed, and
        // glfwGetWin32Window on a freed window crashes. user32 validates raw
        // HWNDs, so SetWindowLongPtr on a dead handle is harmless.
        if (_subclassedHandle != 0 && _originalWndProc != 0)
            SetWindowLongPtr(_subclassedHandle, GwlpWndProc, _originalWndProc);
        SubclassedWindows.Remove(_subclassedHandle);
        _subclassedHandle = 0;
        _subclassed = false;
    }

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
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
    [DllImport("user32.dll")] private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);
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
