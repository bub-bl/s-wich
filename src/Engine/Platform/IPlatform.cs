namespace Crowbar.Engine.Platform;

public interface IPlatform : IDisposable
{
    IWindow CreateWindow(WindowOptions options);
}
