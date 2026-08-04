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
        private bool _disposed;

        public SilkWindow(WindowOptions options)
        {
            Title = options.Title;
            SilkWindowOptions windowOptions = SilkWindowOptions.Default;
            windowOptions.Title = options.Title;
            windowOptions.Size = new Silk.NET.Maths.Vector2D<int>(options.Width, options.Height);
            windowOptions.WindowBorder = options.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed;
            windowOptions.IsVisible = true;
            windowOptions.ShouldSwapAutomatically = false;
            _window = Silk.NET.Windowing.Window.Create(windowOptions);
            _window.IsVisible = true;

            _window.Load += () => Loaded?.Invoke();
            _window.Closing += () => Closing?.Invoke();
            _window.Update += delta => Updating?.Invoke(delta);
            _window.Render += delta => Rendering?.Invoke(delta);
            _window.FramebufferResize += size => Resized?.Invoke(size.X, size.Y);
        }

        public string Title { get; }
        public int Width => _window.Size.X;
        public int Height => _window.Size.Y;
        public bool IsClosing => _window.IsClosing;
        public nint NativeHandle => OperatingSystem.IsWindows()
            ? GetWin32Window(_window.Handle)
            : _window.Handle;

        public event Action? Loaded;
        public event Action? Closing;
        public event Action<double>? Updating;
        public event Action<double>? Rendering;
        public event Action<int, int>? Resized;

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

            _window.Dispose();
            _disposed = true;
        }

        [DllImport("glfw3", EntryPoint = "glfwGetWin32Window")]
        private static extern nint GetWin32Window(nint glfwWindow);
    }
}
