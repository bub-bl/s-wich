using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Crowbar.Editor;

public sealed class AssetsDrawer : UserControl
{
    public Border Anchor { get; }
    public Popup Popup { get; }
    public Border Drawer { get; }
    public ToggleButton ConsoleTabButton { get; }
    public ToggleButton AssetsTabButton { get; }
    public Border ConsoleContent { get; }
    public Border AssetsContent { get; }
    public TextBox ConsoleTextBox { get; }
    
    public event EventHandler? CloseRequested;
    public event EventHandler<bool>? TabChanged;

    public AssetsDrawer()
    {
        Anchor = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Background = Brushes.Transparent };
        ConsoleTabButton = new ToggleButton { Content = "Console", IsChecked = false };
        AssetsTabButton = new ToggleButton { Content = "Project Assets", IsChecked = true };
        ConsoleTabButton.Classes.Add("drawer-tab");
        AssetsTabButton.Classes.Add("drawer-tab");
        ConsoleTabButton.Click += (_, _) => TabChanged?.Invoke(this, false);
        AssetsTabButton.Click += (_, _) => TabChanged?.Invoke(this, true);

        var close = new Button { Content = "×", Padding = new Thickness(8, 2), Background = Brushes.Transparent, Foreground = Brush.Parse("#A1A1AA") };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        var header = new Border
        {
            Background = Brush.Parse("#27272A"), BorderBrush = Brush.Parse("#3F3F46"), BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(8, 8, 0, 0),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, Auto, Auto, *, Auto") }
        };
        var headerGrid = (Grid)header.Child!;
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = "◆", Foreground = Brush.Parse("#60A5FA"), VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = "Console / Project Assets", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        tabs.Children.Add(ConsoleTabButton); tabs.Children.Add(AssetsTabButton);
        AddGrid(headerGrid, title, 0); AddGrid(headerGrid, new Border { Width = 1, Height = 18, Background = Brush.Parse("#52525B"), Margin = new Thickness(12, 0) }, 1);
        AddGrid(headerGrid, tabs, 2); AddGrid(headerGrid, new TextBlock { Text = "Space to toggle drawer", Foreground = Brush.Parse("#71717A"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }, 3); AddGrid(headerGrid, close, 4);

        ConsoleTextBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, Background = Brush.Parse("#121214"), Foreground = Brush.Parse("#A1A1AA"), FontFamily = "Consolas, Courier New" };
        ConsoleContent = new Border { Padding = new Thickness(4), Background = Brush.Parse("#121214"), Margin = new Thickness(4), CornerRadius = new CornerRadius(4), IsVisible = false, Child = ConsoleTextBox };
        AssetsContent = new Border { Padding = new Thickness(12), Background = Brush.Parse("#121214"), Margin = new Thickness(4), CornerRadius = new CornerRadius(4), Child = CreateAssetsContent() };
        var body = new Grid(); body.Children.Add(ConsoleContent); body.Children.Add(AssetsContent);
        var drawerGrid = new Grid { RowDefinitions = new RowDefinitions("Auto, *") }; Grid.SetRow(header, 0); Grid.SetRow(body, 1); drawerGrid.Children.Add(header); drawerGrid.Children.Add(body);
        Drawer = new Border { Height = 360, Margin = new Thickness(8), Background = Brush.Parse("#18181B"), BorderBrush = Brush.Parse("#3F3F46"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8, 8, 0, 0), BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = -2, Blur = 8, Spread = 0, Color = Color.FromArgb(0x55, 0, 0, 0) }), Child = drawerGrid };
        Popup = new Popup { IsOpen = false, Placement = PlacementMode.Top, HorizontalAlignment = HorizontalAlignment.Stretch, Child = Drawer };
        var root = new Grid(); root.Children.Add(Anchor); root.Children.Add(Popup); Content = root;
    }

    private static void AddGrid(Grid grid, Control child, int column) { Grid.SetColumn(child, column); grid.Children.Add(child); }
    private static WrapPanel CreateAssetsContent() => new() { Children = { Asset("📁", "Shaders"), Asset("📁", "Textures"), Asset("🧊", "model.obj") } };
    private static StackPanel Asset(string icon, string name) => new() { Width = 80, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = icon, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } } };
}
