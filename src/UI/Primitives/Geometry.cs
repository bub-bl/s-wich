namespace Crowbar.UI;

public readonly record struct UiSize(float Width, float Height);
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

/// <summary>Resolved per-side values (padding, border or margin) from the layout pass.</summary>
public readonly record struct UiThickness(float Top, float Right, float Bottom, float Left);
