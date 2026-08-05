namespace Crowbar.Engine.Platform;

public enum PointerButton
{
    Left,
    Right,
    Middle,
    X1,
    X2
}

public readonly record struct PointerMoveEvent(float X, float Y);
public readonly record struct PointerButtonEvent(float X, float Y, PointerButton Button, bool IsDown);
public readonly record struct PointerWheelEvent(float X, float Y, float DeltaX, float DeltaY);
public readonly record struct KeyEvent(int KeyCode, bool IsDown, bool IsRepeat);

public interface IWindowInputSource : IDisposable
{
    event Action<PointerMoveEvent>? PointerMoved;
    event Action<PointerButtonEvent>? PointerButtonChanged;
    event Action<PointerWheelEvent>? PointerWheelChanged;
    event Action<KeyEvent>? KeyChanged;
    void Update();
}
