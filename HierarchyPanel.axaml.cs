using Avalonia;
using Avalonia.Controls;

namespace Crowbar;

public partial class HierarchyPanel : UserControl
{
    public static readonly StyledProperty<Scene?> SceneProperty =
        AvaloniaProperty.Register<HierarchyPanel, Scene?>(nameof(Scene));

    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public HierarchyPanel()
    {
        InitializeComponent();
    }
}
