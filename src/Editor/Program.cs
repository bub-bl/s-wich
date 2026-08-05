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
            if (Environment.GetEnvironmentVariable("CROWBAR_UI_SMOKE_ONLY") == "1") return;
        }
        using IPlatform platform = new SdlPlatform();
        using IWindow window = platform.CreateWindow(new WindowOptions(
            Title: "Crowbar",
            Width: 1280,
            Height: 720));

        WebGpuContext? webGpu = null;
        using var ui = new UiSystem();
        var razorPath = ResolveUiFile("Demo.razor");
        var stylePath = ResolveUiFile("Demo.css");
        Console.WriteLine($"Razor UI source: {razorPath}");
        Console.WriteLine($"Razor UI styles: {stylePath}");
        ui.LoadRazor(File.ReadAllText(razorPath), "Demo");
        ui.LoadStyles(File.ReadAllText(stylePath));
        ui.WatchFiles(razorPath, stylePath, "Demo");
        window.PointerMoved += e => ui.ProcessPointerMove(e.X, e.Y);
        window.PointerButtonChanged += e =>
        {
            if (e.IsDown) ui.ProcessPointerDown(e.X, e.Y, (int)e.Button);
            else ui.ProcessPointerUp(e.X, e.Y, (int)e.Button);
        };
        window.PointerWheelChanged += e => ui.ProcessPointerWheel(e.X, e.Y, e.DeltaX, e.DeltaY);
        window.KeyChanged += e => ui.ProcessKey(e);

        window.Loaded += () =>
        {
            Console.WriteLine("Crowbar platform initialized.");
            int framebufferWidth = window.FramebufferWidth > 0 ? window.FramebufferWidth : window.Width;
            int framebufferHeight = window.FramebufferHeight > 0 ? window.FramebufferHeight : window.Height;
            webGpu = new WebGpuContext(window.NativeHandle, framebufferWidth, framebufferHeight) { Ui = ui };
            ui.SetViewport(framebufferWidth, framebufferHeight);
            ui.Render();
        };
        window.Updating += delta => { ui.Update((float)delta); webGpu?.Update(delta); };
        window.Rendering += delta => webGpu?.Render(delta);
        window.Resized += (width, height) => { webGpu?.Resize(width, height); ui.SetViewport(width, height); ui.Render(); };
        window.Closing += () =>
        {
            webGpu?.Dispose();
            Console.WriteLine("Crowbar shutting down.");
        };
        window.Run();
    }

    private static string ResolveUiFile(string fileName)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Ui", fileName);
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Editor", "Ui", fileName));
        return File.Exists(sourcePath) ? sourcePath : outputPath;
    }
}
