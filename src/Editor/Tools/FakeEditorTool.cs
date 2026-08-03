using Avalonia.Controls;

namespace Crowbar.Editor.Tools;

/// <summary>
/// Small example showing the intended tool authoring model.
/// </summary>
public sealed class FakeEditorTool : EditorWindow
{
    public FakeEditorTool(EditorContext context)
        : base(context)
    {
        Title = "Fake C# Tool";
        Width = 420;
        Height = 260;
        Content = new FakeEditorControl(context);
    }
}

public sealed class FakeEditorControl(EditorContext context) : EditorControl
{
    private int _clickCount;

    protected override Control BuildUi()
    {
        var title = new TextBlock
        {
            Text = "Custom control created entirely in C#",
            FontSize = 16,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };

        var state = new TextBlock
        {
            Text = $"Selected objects: {context.Scene.GameObjects.Count}\nButton clicks: {_clickCount}"
        };

        var button = new Button
        {
            Content = "Change state"
        };
        button.Click += (_, _) =>
        {
            _clickCount++;
            StateHasChanged();
        };

        return new Border
        {
            Padding = new Avalonia.Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    title,
                    state,
                    button
                }
            }
        };
    }
}
