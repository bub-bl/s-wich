using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Crowbar.Editor.Tools;

namespace Crowbar.Editor;

public enum ToolbarAction
{
    TogglePlay,
    ResetCamera,
    AddCube,
    AddPyramid,
    DeleteObject
}

public sealed class ToolbarActionEventArgs(ToolbarAction action) : EventArgs
{
    public ToolbarAction Action { get; } = action;
}

public sealed class Toolbar : EditorControl
{
    private bool _isPaused = true;
    private SilkViewportControl? _viewport;

    public event EventHandler<ToolbarActionEventArgs>? ActionRequested;

    public void BindMetrics(SilkViewportControl viewport)
    {
        _viewport = viewport;
        StateHasChanged();
    }

    public void SetPlaying(bool isPaused)
    {
        _isPaused = isPaused;
        StateHasChanged();
    }

    protected override Control BuildUi()
    {
        var play = CreateButton(_isPaused ? "▶ Play" : "⏸ Pause", ToolbarAction.TogglePlay,
            _isPaused ? EditorTheme.AccentBlueStrong : EditorTheme.AccentOrange, EditorTheme.TextWhite, 12, 4);
        play.FontWeight = FontWeight.SemiBold;
        var fps = new TextBlock { FontSize = 11, Foreground = EditorTheme.Brush(EditorTheme.SuccessBright), FontWeight = FontWeight.Bold };
        var frameTime = new TextBlock { FontSize = 11, Foreground = EditorTheme.Brush(EditorTheme.TextMuted) };
        if (_viewport != null)
        {
            fps.Bind(TextBlock.TextProperty, new Binding("Fps") { Source = _viewport, StringFormat = "FPS: {0}" });
            frameTime.Bind(TextBlock.TextProperty,
                new Binding("FrameTimeMs") { Source = _viewport, StringFormat = "{0:F1} ms" });
        }
        else
        {
            fps.Text = "FPS: --";
            frameTime.Text = "-- ms";
        }

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(Wrap(play));
        left.Children.Add(Wrap(new ToggleButton
        {
            Content = "🌐 Wireframe",
            Padding = new Thickness(8, 4),
            [!ToggleButton.IsCheckedProperty] = new Binding("Scene.IsWireframe")
        }));
        left.Children.Add(Wrap(CreateButton("🎥 Reset Camera", ToolbarAction.ResetCamera, null, null, 8, 4)));
        left.Children.Add(new Separator
        { Width = 1, Height = 20, Background = EditorTheme.Brush(EditorTheme.Border), Margin = new Thickness(4, 0) });
        left.Children.Add(Wrap(CreateButton("+ Add Cube", ToolbarAction.AddCube, null, null, 8, 4)));
        left.Children.Add(Wrap(CreateButton("+ Add Pyramid", ToolbarAction.AddPyramid, null, null, 8, 4)));
        left.Children.Add(Wrap(CreateButton("🗑 Remove", ToolbarAction.DeleteObject, null, null, 8, 4)));

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        right.Children.Add(fps);
        right.Children.Add(frameTime);
        right.Children.Add(new Border
        {
            Background = EditorTheme.Brush(EditorTheme.SurfaceRaised),
            Padding = new Thickness(6, 2),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = "WebGPU (WGPU)",
                FontSize = 11,
                Foreground = EditorTheme.Brush(EditorTheme.AccentBlue),
                FontWeight = FontWeight.SemiBold
            }
        });
        var dock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(left);
        dock.Children.Add(right);
        return new Border
        {
            Background = EditorTheme.Brush(EditorTheme.Surface),
            BorderBrush = EditorTheme.Brush(EditorTheme.Border),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(8, 4),
            Child = dock
        };
    }

    private Button CreateButton(string content, ToolbarAction action, string? background, string? foreground,
        double horizontal, double vertical)
    {
        var button = new Button { Content = content, Padding = new Thickness(horizontal, vertical) };
        if (background != null) button.Background = EditorTheme.Brush(background);
        if (foreground != null) button.Foreground = EditorTheme.Brush(foreground);
        button.Click += (_, _) => ActionRequested?.Invoke(this, new ToolbarActionEventArgs(action));
        return button;
    }

    private static Border Wrap(Control child) => new()
    { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = child };
}
