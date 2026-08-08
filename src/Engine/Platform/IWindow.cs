using Crowbar.UI;

namespace Crowbar.Engine.Platform;

public interface IWindow : IDisposable
{
    string Title { get; }
    void SetTitle(string title);
    int Width { get; }
    int Height { get; }
    int FramebufferWidth { get; }
    int FramebufferHeight { get; }
    bool IsClosing { get; }
    nint NativeHandle { get; }

    event Action? Loaded;
    event Action? Closing;
    event Action<double>? Updating;
    event Action<double>? Rendering;
    event Action<int, int>? Resized;
    event Action<PointerMoveEvent>? PointerMoved;
    event Action<PointerButtonEvent>? PointerButtonChanged;
    event Action<PointerWheelEvent>? PointerWheelChanged;
    event Action<KeyEvent>? KeyChanged;

    void Run();
    void Close();
}
