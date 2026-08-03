using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Crowbar.Editor.Tools;

namespace Crowbar.Editor;

public sealed class StatusBar : EditorControl
{
    protected override Control BuildUi()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto") };
        var ready = new TextBlock { Text = "● Engine Ready", FontSize = 10, Foreground = Brush.Parse("#22C55E") };
        var selected = new TextBlock
        {
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding("Scene.SelectedObject.Name")
            { StringFormat = "Selected: {0}" },
            FontSize = 10,
            Foreground = Brush.Parse("#A1A1AA"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var version = new TextBlock
        { Text = "Avalonia 12 + Silk.NET 2.23 (WGPU)", FontSize = 10, Foreground = Brush.Parse("#71717A") };
        Grid.SetColumn(ready, 0);
        Grid.SetColumn(selected, 1);
        Grid.SetColumn(version, 2);
        grid.Children.Add(ready);
        grid.Children.Add(selected);
        grid.Children.Add(version);
        return new Border { Background = Brush.Parse("#09090B"), Padding = new Thickness(8, 2), Child = grid };
    }
}