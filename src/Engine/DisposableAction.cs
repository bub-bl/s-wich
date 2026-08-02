namespace Crowbar.Engine;

public sealed class DisposableAction(Action action) : IDisposable
{
    public void Dispose()
    {
        action();
    }

    public static DisposableAction Create(Action action) => new(action);
}