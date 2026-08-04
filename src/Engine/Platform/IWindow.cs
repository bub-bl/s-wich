namespace Crowbar.Engine.Platform;

public interface IWindow : IDisposable
{
    string Title { get; }
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

    void Run();
    void Close();
}
