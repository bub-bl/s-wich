namespace Crowbar.Engine.Platform;

public readonly record struct WindowOptions(
    string Title,
    int Width,
    int Height,
    bool Resizable = true
);