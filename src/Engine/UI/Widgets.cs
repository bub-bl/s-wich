namespace Crowbar.Engine.UI;

public readonly record struct UiPointerEvent(float X, float Y, int Button = 0);

public class Label : Panel
{
    public Label(string text = "") { TagName = "text"; Text = text; }
}

public class Button : Panel
{
    public Button(string text = "")
    {
        TagName = "button";
        AddChild(new Label(text));
    }
    public event Action<UiPointerEvent>? Clicked;
    internal void RaiseClicked(UiPointerEvent e) => Clicked?.Invoke(e);
}

public class TextInput : Panel
{
    public TextInput() { TagName = "input"; }
    public string Value { get; private set; } = string.Empty;
    public event Action<string>? ValueChanged;
    public void SetValue(string value) { Value = value; ValueChanged?.Invoke(value); Invalidate(); }
}

public class Image : Panel
{
    public Image() { TagName = "image"; }
    public string? Source { get; set; }
}

public static class PanelExtensions
{
    public static Panel? HitTest(this Panel panel, float x, float y)
    {
        if (!panel.IsVisible || x < panel.Layout.X || y < panel.Layout.Y || x > panel.Layout.Right || y > panel.Layout.Bottom) return null;
        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            var hit = panel.Children[i].HitTest(x, y);
            if (hit is not null) return hit;
        }
        return panel;
    }
}
