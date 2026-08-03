using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

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

public sealed class Toolbar : UserControl
{
    public event EventHandler<ToolbarActionEventArgs>? ActionRequested;
    public Button PlayButton { get; }
    private readonly TextBlock _fpsText;
    private readonly TextBlock _frameTimeText;

    public Toolbar()
    {
        PlayButton = CreateButton("▶ Play", ToolbarAction.TogglePlay, "#2563EB", "White", 12, 4);
        PlayButton.FontWeight = FontWeight.SemiBold;

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(Wrap(PlayButton));
        left.Children.Add(Wrap(new ToggleButton
        {
            Content = "🌐 Wireframe",
            Padding = new Thickness(8, 4),
            [!ToggleButton.IsCheckedProperty] = new Avalonia.Data.Binding("Scene.IsWireframe")
        }));
        left.Children.Add(Wrap(CreateButton("🎥 Reset Camera", ToolbarAction.ResetCamera, null, null, 8, 4)));
        left.Children.Add(new Separator { Width = 1, Height = 20, Background = Brush.Parse("#3F3F46"), Margin = new Thickness(4, 0) });
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
        _fpsText = new TextBlock { Text = "FPS: --", FontSize = 11, Foreground = Brush.Parse("#4ADE80"), FontWeight = FontWeight.Bold };
        _frameTimeText = new TextBlock { Text = "-- ms", FontSize = 11, Foreground = Brush.Parse("#A1A1AA") };
        right.Children.Add(_fpsText);
        right.Children.Add(_frameTimeText);
        right.Children.Add(new Border
        {
            Background = Brush.Parse("#27272A"), Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(4),
            Child = new TextBlock { Text = "WebGPU (WGPU)", FontSize = 11, Foreground = Brush.Parse("#60A5FA"), FontWeight = FontWeight.SemiBold }
        });

        var dock = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(left);
        dock.Children.Add(right);
        Content = new Border
        {
            Background = Brush.Parse("#202023"), BorderBrush = Brush.Parse("#3F3F46"),
            BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(8, 4), Child = dock
        };
    }

    public void BindMetrics(SilkViewportControl viewport)
    {
        _fpsText.Bind(TextBlock.TextProperty, new Binding("Fps") { Source = viewport, StringFormat = "FPS: {0}" });
        _frameTimeText.Bind(TextBlock.TextProperty, new Binding("FrameTimeMs") { Source = viewport, StringFormat = "{0:F1} ms" });
    }

    public void SetPlaying(bool isPaused)
    {
        PlayButton.Content = isPaused ? "▶ Play" : "⏸ Pause";
        PlayButton.Background = Brush.Parse(isPaused ? "#2563EB" : "#D97706");
    }

    private Button CreateButton(string content, ToolbarAction action, string? background, string? foreground, double horizontal, double vertical)
    {
        var button = new Button { Content = content, Padding = new Thickness(horizontal, vertical) };
        if (background != null) button.Background = Brush.Parse(background);
        if (foreground != null) button.Foreground = Brush.Parse(foreground);
        button.Click += (_, _) => ActionRequested?.Invoke(this, new ToolbarActionEventArgs(action));
        return button;
    }

    private static Border Wrap(Control child) => new() { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = child };
}
