using Crowbar.Engine.Platform;
using Crowbar.Engine;
using Crowbar.Engine.UI;

namespace Crowbar.Editor;

internal static class Program
{
    public static void Main()
    {
        if (Environment.GetEnvironmentVariable("CROWBAR_UI_SMOKE_TESTS") == "1")
        {
            UiSmokeTests.Run();
            Console.WriteLine("Crowbar UI smoke tests passed.");
        }
        using IPlatform platform = new SdlPlatform();
        using IWindow window = platform.CreateWindow(new WindowOptions(
            Title: "Crowbar",
            Width: 1280,
            Height: 720));

        WebGpuContext? webGpu = null;
        using var ui = new UiSystem();
        ui.LoadRazor(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", "Demo.razor")), "Demo");
        ui.LoadStyles(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ui", "Demo.css")));
        ui.WatchFiles(Path.Combine(AppContext.BaseDirectory, "Ui", "Demo.razor"), Path.Combine(AppContext.BaseDirectory, "Ui", "Demo.css"), "Demo");

        window.Loaded += () =>
        {
            Console.WriteLine("Crowbar platform initialized.");
            int framebufferWidth = window.FramebufferWidth > 0 ? window.FramebufferWidth : window.Width;
            int framebufferHeight = window.FramebufferHeight > 0 ? window.FramebufferHeight : window.Height;
            webGpu = new WebGpuContext(window.NativeHandle, framebufferWidth, framebufferHeight) { Ui = ui };
            ui.SetViewport(framebufferWidth, framebufferHeight);
            ui.Render();
        };
        window.Updating += delta => { ui.Update(); webGpu?.Update(delta); };
        window.Rendering += delta => webGpu?.Render(delta);
        window.Resized += (width, height) => { webGpu?.Resize(width, height); ui.SetViewport(width, height); ui.Render(); };
        window.Closing += () =>
        {
            webGpu?.Dispose();
            Console.WriteLine("Crowbar shutting down.");
        };
        window.Run();
    }
}
