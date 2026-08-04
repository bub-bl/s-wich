using Crowbar.Engine.Platform;
using Crowbar.Engine;

namespace Crowbar.Editor;

internal static class Program
{
    public static void Main()
    {
        using IPlatform platform = new SdlPlatform();
        using IWindow window = platform.CreateWindow(new WindowOptions(
            Title: "Crowbar",
            Width: 1280,
            Height: 720));

        WebGpuContext? webGpu = null;

        window.Loaded += () =>
        {
            Console.WriteLine("Crowbar platform initialized.");
            int framebufferWidth = window.FramebufferWidth > 0 ? window.FramebufferWidth : window.Width;
            int framebufferHeight = window.FramebufferHeight > 0 ? window.FramebufferHeight : window.Height;
            webGpu = new WebGpuContext(window.NativeHandle, framebufferWidth, framebufferHeight);
        };
        window.Updating += delta => webGpu?.Update(delta);
        window.Rendering += delta => webGpu?.Render(delta);
        window.Resized += (width, height) => webGpu?.Resize(width, height);
        window.Closing += () =>
        {
            webGpu?.Dispose();
            Console.WriteLine("Crowbar shutting down.");
        };
        window.Run();
    }
}
