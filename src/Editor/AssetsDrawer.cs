using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Crowbar.Editor.Tools;

namespace Crowbar.Editor;

public sealed class AssetsDrawer : EditorControl
{
    private const double OuterMargin = 16;
    private bool _isOpen;
    private bool _showAssets = true;
    private double _width;
    private readonly List<string> _logs = [];
    private DispatcherTimer? _animationTimer;
    private Stopwatch? _animationClock;
    private double _animationStart;
    private double _animationTarget;
    private Border? _drawer;
    private Popup? _popup;
    private TextBox? _console;

    public event EventHandler? CloseRequested;
    public event EventHandler<bool>? TabChanged;
    public bool IsOpen => _isOpen;

    public void SetPlacementTarget(Control target)
    {
        if (_popup != null) _popup.PlacementTarget = target;
    }

    public void SetWidth(double windowWidth)
    {
        _width = Math.Max(0, windowWidth - OuterMargin);
        if (_drawer != null) _drawer.Width = _width;
    }

    public void SetOpen(bool isOpen)
    {
        _isOpen = isOpen;
        if (_popup != null)
        {
            _popup.IsOpen = isOpen;
            Animate(isOpen ? 0 : _drawer?.Height ?? 360);
        }
    }

    public void Toggle() => SetOpen(!_isOpen);

    public void SetTab(bool showAssets)
    {
        _showAssets = showAssets;
        StateHasChanged();
        TabChanged?.Invoke(this, showAssets);
    }

    public void AppendLog(string message)
    {
        _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_console != null)
        {
            _console.Text = string.Join(Environment.NewLine, _logs) + Environment.NewLine;
            _console.CaretIndex = _console.Text.Length;
        }
    }

    protected override Control BuildUi()
    {
        var anchor = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        };
        var consoleTab = new ToggleButton { Content = "Console", IsChecked = !_showAssets };
        consoleTab.Classes.Add("drawer-tab");
        var assetsTab = new ToggleButton { Content = "Project Assets", IsChecked = _showAssets };
        assetsTab.Classes.Add("drawer-tab");
        consoleTab.Click += (_, _) => SetTab(false);
        assetsTab.Click += (_, _) => SetTab(true);
        var close = new Button
        {
            Content = "×",
            Padding = new Thickness(8, 2),
            Background = Brushes.Transparent,
            Foreground = Brush.Parse("#A1A1AA")
        };
        close.Click += (_, _) =>
        {
            SetOpen(false);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, Auto, Auto, *, Auto") };
        var title = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                    { Text = "◆", Foreground = Brush.Parse("#60A5FA"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock
                {
                    Text = "Console / Project Assets", FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { consoleTab, assetsTab }
        };
        AddGrid(headerGrid, title, 0);
        AddGrid(headerGrid,
            new Border { Width = 1, Height = 18, Background = Brush.Parse("#52525B"), Margin = new Thickness(12, 0) },
            1);
        AddGrid(headerGrid, tabs, 2);
        AddGrid(headerGrid,
            new TextBlock
            {
                Text = "Space to toggle drawer",
                Foreground = Brush.Parse("#71717A"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            }, 3);
        AddGrid(headerGrid, close, 4);
        var header = new Border
        {
            Background = Brush.Parse("#27272A"),
            BorderBrush = Brush.Parse("#3F3F46"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Child = headerGrid
        };
        _console = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            Background = Brush.Parse("#121214"),
            Foreground = Brush.Parse("#A1A1AA"),
            FontFamily = "Consolas, Courier New",
            Text = string.Join(Environment.NewLine, _logs)
        };
        var consoleContent = new Border
        {
            Padding = new Thickness(4),
            Background = Brush.Parse("#121214"),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            IsVisible = !_showAssets,
            Child = _console
        };
        var assetsContent = new Border
        {
            Padding = new Thickness(12),
            Background = Brush.Parse("#121214"),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            IsVisible = _showAssets,
            Child = CreateAssetsContent()
        };
        var body = new Grid { Children = { consoleContent, assetsContent } };
        var drawerGrid = new Grid { RowDefinitions = new RowDefinitions("Auto, *") };
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        drawerGrid.Children.Add(header);
        drawerGrid.Children.Add(body);
        _drawer = new Border
        {
            Height = 360,
            Width = _width,
            Margin = new Thickness(8),
            Background = Brush.Parse("#18181B"),
            BorderBrush = Brush.Parse("#3F3F46"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = -2, Blur = 8, Color = Color.FromArgb(0x55, 0, 0, 0) }),
            Child = drawerGrid
        };
        _popup = new Popup
        {
            IsOpen = _isOpen,
            Placement = PlacementMode.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = _drawer
        };
        if (!_isOpen) _drawer.RenderTransform = new TranslateTransform(0, 360);
        var root = new Grid { Children = { anchor, _popup } };
        return root;
    }

    private void Animate(double target)
    {
        if (_drawer == null) return;
        _animationTimer?.Stop();
        _animationStart = (_drawer.RenderTransform as TranslateTransform)?.Y ?? _drawer.Height;
        _animationTarget = target;
        _animationClock = Stopwatch.StartNew();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        const double durationMs = 220;
        var progress = Math.Clamp((_animationClock?.Elapsed.TotalMilliseconds ?? durationMs) / durationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        if (_drawer != null)
            _drawer.RenderTransform =
                new TranslateTransform(0, _animationStart + (_animationTarget - _animationStart) * eased);
        if (progress >= 1)
        {
            _animationTimer?.Stop();
            _animationTimer = null;
            _animationClock = null;
        }
    }

    private static void AddGrid(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private static WrapPanel CreateAssetsContent() => new()
    { Children = { Asset("📁", "Shaders"), Asset("📁", "Textures"), Asset("🧊", "model.obj") } };

    private static StackPanel Asset(string icon, string name) => new()
    {
        Width = 80,
        Margin = new Thickness(8),
        HorizontalAlignment = HorizontalAlignment.Center,
        Children =
        {
            new TextBlock { Text = icon, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
        }
    };
}