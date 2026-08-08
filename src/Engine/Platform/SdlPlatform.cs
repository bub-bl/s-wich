using Crowbar.UI;
using Silk.NET.SDL;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using SilkWindowOptions = Silk.NET.Windowing.WindowOptions;

namespace Crowbar.Engine.Platform;

public sealed class SdlPlatform : IPlatform
{
    private readonly Sdl _sdl;
    private bool _disposed;

    public SdlPlatform()
    {
        _sdl = Sdl.GetApi();
        if (_sdl.Init(Sdl.InitVideo) < 0)
            throw new InvalidOperationException($"SDL initialization failed: {_sdl.GetErrorS()}");
    }

    public IWindow CreateWindow(WindowOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SilkWindow(options);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _sdl.Quit();
        _sdl.Dispose();
        _disposed = true;
    }

    private sealed class SilkWindow : IWindow
    {
        private readonly Silk.NET.Windowing.IWindow _window;
        private readonly IWindowInputSource _input;
        private bool _disposed;

        public SilkWindow(WindowOptions options)
        {
            _title = options.Title;
            SilkWindowOptions windowOptions = SilkWindowOptions.Default;
            windowOptions.Title = options.Title;
            windowOptions.Size = new Silk.NET.Maths.Vector2D<int>(options.Width, options.Height);
            windowOptions.WindowBorder = options.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed;
            windowOptions.IsVisible = true;
            windowOptions.ShouldSwapAutomatically = false;
            _window = Silk.NET.Windowing.Window.Create(windowOptions);
            _window.IsVisible = true;
            _input = OperatingSystem.IsWindows() ? new WindowsWindowInputSource(() => NativeHandle) : new NullWindowInputSource();
            _input.PointerMoved += e => PointerMoved?.Invoke(e);
            _input.PointerButtonChanged += e => PointerButtonChanged?.Invoke(e);
            _input.PointerWheelChanged += e => PointerWheelChanged?.Invoke(e);
            _input.KeyChanged += e => KeyChanged?.Invoke(e);

            _window.Load += () => Loaded?.Invoke();
            _window.Closing += () => Closing?.Invoke();
            _window.Update += delta => { _input.Update(); Updating?.Invoke(delta); };
            _window.Render += delta => Rendering?.Invoke(delta);
            _window.Resize += size => Resized?.Invoke(size.X, size.Y);
            _window.FramebufferResize += size => Resized?.Invoke(size.X, size.Y);
        }

        private string _title;
        public string Title => _title;
        public void SetTitle(string title)
        {
            _title = title;
            _window.Title = title;
        }
        public int Width => _window.Size.X;
        public int Height => _window.Size.Y;
        public int FramebufferWidth => _window.FramebufferSize.X;
        public int FramebufferHeight => _window.FramebufferSize.Y;
        public bool IsClosing => _window.IsClosing;
        public nint NativeHandle => OperatingSystem.IsWindows()
            ? GetWin32Window(_window.Handle)
            : _window.Handle;

        public event Action? Loaded;
        public event Action? Closing;
        public event Action<double>? Updating;
        public event Action<double>? Rendering;
        public event Action<int, int>? Resized;
        public event Action<PointerMoveEvent>? PointerMoved;
        public event Action<PointerButtonEvent>? PointerButtonChanged;
        public event Action<PointerWheelEvent>? PointerWheelChanged;
        public event Action<KeyEvent>? KeyChanged;

        public void Run()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _window.Run();
        }

        public void Close() => _window.Close();

        public void Dispose()
        {
            if (_disposed)
                return;

            // Tear the input source down before the window: on Windows it
            // restores its subclassed window procedure through the native
            // handle, which is invalid once the GLFW window is destroyed.
            _input.Dispose();
            _window.Dispose();
            _disposed = true;
        }

        [DllImport("glfw3", EntryPoint = "glfwGetWin32Window")]
        private static extern nint GetWin32Window(nint glfwWindow);
    }
}
