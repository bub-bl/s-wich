using Avalonia;
using Avalonia.Controls;
using System.ComponentModel;

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

    public static readonly StyledProperty<SceneObject?> SelectedObjectProperty =
        AvaloniaProperty.Register<InspectorPanel, SceneObject?>(nameof(SelectedObject));

    public SceneObject? SelectedObject
    {
        get => GetValue(SelectedObjectProperty);
        private set => SetValue(SelectedObjectProperty, value);
    }

    public InspectorPanel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SceneProperty) return;

        if (change.OldValue is Scene oldScene)
            oldScene.PropertyChanged -= OnScenePropertyChanged;

        if (change.NewValue is Scene newScene)
        {
            newScene.PropertyChanged += OnScenePropertyChanged;
            SelectedObject = newScene.SelectedObject;
        }
        else
        {
            SelectedObject = null;
        }
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Scene.SelectedObject) && sender is Scene scene)
            SelectedObject = scene.SelectedObject;
    }
}
