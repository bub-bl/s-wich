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
    public int CaretIndex { get; private set; }
    internal bool CaretVisible { get; private set; } = true;
    private float _caretTime;
    public event Action<string>? ValueChanged;
    public void SetValue(string value) { Value = value; CaretIndex = Math.Clamp(value.Length, 0, value.Length); ValueChanged?.Invoke(value); Invalidate(); }
    internal void AdvanceCaret(float deltaTime)
    {
        if (!IsFocused) { CaretVisible = false; _caretTime = 0; return; }
        _caretTime += Math.Max(0, deltaTime);
        if (_caretTime >= 0.5f) { _caretTime = 0; CaretVisible = !CaretVisible; Invalidate(); }
    }
    internal void HandleKey(int keyCode, bool isDown)
    {
        if (!isDown) return;
        if (keyCode == 0x25) CaretIndex = Math.Max(0, CaretIndex - 1);
        else if (keyCode == 0x27) CaretIndex = Math.Min(Value.Length, CaretIndex + 1);
        else if (keyCode == 0x24) CaretIndex = 0;
        else if (keyCode == 0x23) CaretIndex = Value.Length;
        else if (keyCode == 0x08 && CaretIndex > 0) SetValue(Value.Remove(--CaretIndex, 1));
        else if (keyCode == 0x2E && CaretIndex < Value.Length) SetValue(Value.Remove(CaretIndex, 1));
        else if (keyCode == 0x20) Insert(' ');
        else if (keyCode is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) Insert((char)keyCode);
        CaretVisible = true; _caretTime = 0; Invalidate();
    }
    private void Insert(char value) { Value = Value.Insert(CaretIndex++, value.ToString()); ValueChanged?.Invoke(Value); Invalidate(); }
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
