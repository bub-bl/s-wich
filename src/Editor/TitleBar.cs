using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Crowbar.Editor.Tools;

namespace Crowbar.Editor;

public sealed class TitleBar : EditorControl
{
    private readonly List<MenuItem> _windowItems = [];
    public event EventHandler<TitleBarActionEventArgs>? ActionRequested;
    public event EventHandler<PointerPressedEventArgs>? DragAreaPointerPressed;

    public void AddWindowMenuItem(MenuItem item)
    {
        _windowItems.Add(item);
        StateHasChanged();
    }

    protected override Control BuildUi()
    {
        var windowMenu = CreateMenuItem("_Window");
        windowMenu.Items.Add(CreateMenuItem("Reset Viewport Camera", TitleBarAction.ResetCamera));
        foreach (var item in _windowItems) windowMenu.Items.Add(item);
        var dragArea = new Grid { Background = Brushes.Transparent, [Grid.ColumnProperty] = 1 };
        WindowDecorationProperties.SetElementRole(dragArea, WindowDecorationsElementRole.TitleBar);
        dragArea.PointerPressed += (sender, e) => DragAreaPointerPressed?.Invoke(sender, e);
        dragArea.Children.Add(new TextBlock
        {
            Text = "Crowbar Engine",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = EditorTheme.Brush(EditorTheme.TextTitle),
            IsHitTestVisible = false
        });
        var menu = new Menu
            { Background = Brushes.Transparent, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        menu.Items.Add(CreateFileMenu());
        menu.Items.Add(CreateEditMenu());
        menu.Items.Add(CreateGameObjectMenu());
        menu.Items.Add(windowMenu);
        menu.Items.Add(CreateMenuItem("_Help", new MenuItem { Header = "About Engine" }));
        var left = new StackPanel
            { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Border
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(11, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "◆",
                Foreground = EditorTheme.Brush(EditorTheme.AccentBlue),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        left.Children.Add(menu);
        var content = new Grid
            { ColumnDefinitions = new ColumnDefinitions("Auto, *"), Height = 32, Margin = new Thickness(0, 0, 138, 0) };
        content.Children.Add(left);
        content.Children.Add(dragArea);
        return new Border
        {
            Background = EditorTheme.Brush(EditorTheme.WindowBackground),
            BorderBrush = EditorTheme.Brush(EditorTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content
        };
    }

    private MenuItem CreateFileMenu() => CreateMenuItem("_File", CreateMenuItem("_New Scene", TitleBarAction.NewScene),
        new Separator(), CreateMenuItem("_Exit", TitleBarAction.Exit));

    private MenuItem CreateEditMenu() => CreateMenuItem("_Edit", new MenuItem { Header = "_Duplicate" },
        CreateMenuItem("_Delete", TitleBarAction.DeleteObject));

    private MenuItem CreateGameObjectMenu() => CreateMenuItem("_GameObject",
        CreateMenuItem("3D Object > Cube", TitleBarAction.AddCube),
        CreateMenuItem("3D Object > Pyramid", TitleBarAction.AddPyramid));

    private static MenuItem CreateMenuItem(string header, params object[] items)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Classes.Add("title-menu-item");
        foreach (var item in items) menuItem.Items.Add(item);
        return menuItem;
    }

    private MenuItem CreateMenuItem(string header, TitleBarAction action)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += (_, _) => ActionRequested?.Invoke(this, new TitleBarActionEventArgs(action));
        return menuItem;
    }
}

public enum TitleBarAction
{
    NewScene,
    Exit,
    DeleteObject,
    AddCube,
    AddPyramid,
    ResetCamera
}

public sealed class TitleBarActionEventArgs(TitleBarAction action) : EventArgs
{
    public TitleBarAction Action { get; } = action;
}
