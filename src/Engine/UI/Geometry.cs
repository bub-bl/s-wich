namespace Crowbar.Engine.UI;

public readonly record struct UiSize(float Width, float Height);
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}
