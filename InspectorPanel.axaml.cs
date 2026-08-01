using Avalonia;
using Avalonia.Controls;

namespace Crowbar;

public partial class InspectorPanel : UserControl
{
    public static readonly StyledProperty<Scene?> SceneProperty =
        AvaloniaProperty.Register<InspectorPanel, Scene?>(nameof(Scene));

    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public InspectorPanel()
    {
        InitializeComponent();
    }
}
