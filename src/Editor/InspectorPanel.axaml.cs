using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Crowbar.Engine;
using EngineComponent = Crowbar.Engine.Component;

namespace Crowbar.Editor;

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

    public ObservableCollection<ComponentInspectorViewModel> Components { get; } = [];

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
            RebuildComponents();
        }
        else
        {
            SelectedObject = null;
            Components.Clear();
        }
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Scene.SelectedObject) && sender is Scene scene)
        {
            SelectedObject = scene.SelectedObject;
            RebuildComponents();
        }
    }

    private void RebuildComponents()
    {
        Components.Clear();

        GameObject? gameObject = SelectedObject?.Renderer?.GameObject;
        if (gameObject == null) return;

        foreach (EngineComponent component in gameObject.Components)
            Components.Add(new ComponentInspectorViewModel(component));
    }
}

public sealed class ComponentInspectorViewModel
{
    public ComponentInspectorViewModel(EngineComponent component)
    {
        Component = component;
        Properties = new(GetInspectableProperties(component)
            .Select(property => new PropertyInspectorViewModel(component, property)));
    }

    public EngineComponent Component { get; }
    public string Name => Component.GetType().Name;
    public bool Enabled
    {
        get => Component.Enabled;
        set => Component.Enabled = value;
    }

    public ObservableCollection<PropertyInspectorViewModel> Properties { get; }

    private static IEnumerable<PropertyInfo> GetInspectableProperties(EngineComponent component) =>
        component.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetCustomAttribute<PropertyAttribute>() != null && property.CanRead);
}

public sealed class PropertyInspectorViewModel
{
    private readonly object _instance;
    private readonly PropertyInfo _property;

    public PropertyInspectorViewModel(object instance, PropertyInfo property)
    {
        _instance = instance;
        _property = property;
    }

    public string Name => _property.Name;
    public bool IsEditable => _property.CanWrite && IsSimpleType(_property.PropertyType);
    public bool IsReadOnly => !IsEditable;
    public string ValueText
    {
        get
        {
            object? value = _property.GetValue(_instance);
            return value switch
            {
                null => "None",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };
        }
        set
        {
            if (!IsEditable) return;
            if (TryConvert(value, _property.PropertyType, out object? converted))
                _property.SetValue(_instance, converted);
        }
    }

    private static bool IsSimpleType(Type type) =>
        type == typeof(string) || type == typeof(bool) || type.IsEnum ||
        type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
        type == typeof(long) || type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);

    private static bool TryConvert(string? text, Type type, out object? value)
    {
        value = null;
        if (type == typeof(string))
        {
            value = text ?? string.Empty;
            return true;
        }

        if (type == typeof(bool) && bool.TryParse(text, out bool boolean))
        {
            value = boolean;
            return true;
        }

        if (type.IsEnum && Enum.TryParse(type, text, true, out object? enumValue))
        {
            value = enumValue;
            return true;
        }

        try
        {
            value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }
}
