using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Crowbar.Engine;
using Crowbar.Editor.Tools;
using EngineComponent = Crowbar.Engine.Component;

namespace Crowbar.Editor;

public sealed class InspectorPanel : EditorControl
{
    private Scene? _scene;

    public Scene? Scene
    {
        get => _scene;
        set
        {
            if (ReferenceEquals(_scene, value)) return;

            if (_scene is not null)
                _scene.PropertyChanged -= OnScenePropertyChanged;

            _scene = value;

            if (_scene is not null)
                _scene.PropertyChanged += OnScenePropertyChanged;

            StateHasChanged();
        }
    }

    protected override Control BuildUi()
    {
        var selected = _scene?.SelectedObject;
        var content = new StackPanel { Spacing = 12 };

        if (selected is null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "No object selected",
                Foreground = Brush.Parse("#A1A1AA")
            });
        }
        else
        {
            content.Children.Add(CreateObjectNameSection(selected));
            content.Children.Add(CreateComponentsSection(selected));
            content.Children.Add(new Separator { Background = Brush.Parse("#27272A") });
            content.Children.Add(CreateTransformSection(selected));
            content.Children.Add(new Separator { Background = Brush.Parse("#27272A") });
            content.Children.Add(CreateMaterialSection(selected));
        }

        return new Border
        {
            Background = Brush.Parse("#1F1F23"),
            BorderBrush = Brush.Parse("#27272A"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto, *"),
                Children =
                {
                    new Border
                    {
                        Background = Brush.Parse("#27272A"),
                        Padding = new Thickness(10, 6),
                        Child = new TextBlock
                        {
                            Text = "INSPECTOR",
                            FontWeight = FontWeight.Bold,
                            FontSize = 11,
                            Foreground = Brush.Parse("#A1A1AA")
                        }
                    },
                    new ScrollViewer
                    {
                        [Grid.RowProperty] = 1,
                        Padding = new Thickness(12),
                        Content = content
                    }
                }
            }
        };
    }

    private Control CreateObjectNameSection(SceneObject selected)
    {
        var name = new TextBox { Text = selected.Name };
        name.TextChanged += (_, _) => selected.Name = name.Text ?? string.Empty;
        return CreateSection("Object Name", name);
    }

    private Control CreateComponentsSection(SceneObject selected)
    {
        var components = new StackPanel { Spacing = 8 };
        GameObject? gameObject = selected.Renderer?.GameObject;

        if (gameObject is not null)
        {
            foreach (EngineComponent component in gameObject.Components)
                components.Children.Add(CreateComponentView(component));
        }

        return components;
    }

    private static Control CreateComponentView(EngineComponent component)
    {
        var properties = new StackPanel { Spacing = 7 };
        foreach (PropertyInfo property in GetInspectableProperties(component))
            properties.Children.Add(CreatePropertyView(component, property));

        var enabled = new CheckBox
        {
            IsChecked = component.Enabled
        };
        ToolTip.SetTip(enabled, "Enabled");
        enabled.IsCheckedChanged += (_, _) => component.Enabled = enabled.IsChecked == true;

        return new Border
        {
            Background = Brush.Parse("#27272A"),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(3),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*, Auto"),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = component.GetType().Name,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = Brush.Parse("#E4E4E7")
                            },
                            new ContentControl
                            {
                                [Grid.ColumnProperty] = 1,
                                Content = enabled
                            }
                        }
                    },
                    properties
                }
            }
        };
    }

    private static Control CreatePropertyView(object instance, PropertyInfo property)
    {
        var value = new TextBox
        {
            Text = FormatValue(property.GetValue(instance)),
            IsReadOnly = !IsEditable(property)
        };
        value.TextChanged += (_, _) =>
        {
            if (TryConvert(value.Text, property.PropertyType, out object? converted))
                property.SetValue(instance, converted);
        };

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, *"),
            Margin = new Thickness(0, 2),
            Children =
            {
                new TextBlock
                {
                    Text = property.Name,
                    Foreground = Brush.Parse("#A1A1AA"),
                    VerticalAlignment = VerticalAlignment.Center
                },
                new ContentControl
                {
                    [Grid.ColumnProperty] = 1,
                    Content = value
                }
            }
        };
    }

    private static Control CreateTransformSection(SceneObject selected)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(CreateHeader("TRANSFORM"));
        section.Children.Add(CreateVectorRow("Position", selected,
            () => selected.PositionX, value => selected.PositionX = value,
            () => selected.PositionY, value => selected.PositionY = value,
            () => selected.PositionZ, value => selected.PositionZ = value, 0.1, "F2"));
        section.Children.Add(CreateVectorRow("Rotation (Degrees)", selected,
            () => selected.RotationX, value => selected.RotationX = value,
            () => selected.RotationY, value => selected.RotationY = value,
            () => selected.RotationZ, value => selected.RotationZ = value, 5.0, "F1"));
        section.Children.Add(CreateVectorRow("Scale", selected,
            () => selected.ScaleX, value => selected.ScaleX = value,
            () => selected.ScaleY, value => selected.ScaleY = value,
            () => selected.ScaleZ, value => selected.ScaleZ = value, 0.1, "F2", 0.01));
        return section;
    }

    private static Control CreateVectorRow(
        string title,
        SceneObject selected,
        Func<float> getX, Action<float> setX,
        Func<float> getY, Action<float> setY,
        Func<float> getZ, Action<float> setZ,
        double increment, string format, double? minimum = null)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = Brush.Parse("#6B7280") });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, *, *") };
        grid.Children.Add(CreateNumericField("X", getX, setX, "#EF4444", increment, format, minimum));
        grid.Children.Add(CreateNumericField("Y", getY, setY, "#10B981", increment, format, minimum, 1));
        grid.Children.Add(CreateNumericField("Z", getZ, setZ, "#3B82F6", increment, format, minimum, 2));
        row.Children.Add(grid);
        return row;
    }

    private static Control CreateNumericField(string label, Func<float> get, Action<float> set,
        string color, double increment, string format, double? minimum, int column = 0)
    {
        var input = new NumericUpDown
        {
            Value = (decimal)get(),
            Increment = (decimal)increment,
            FormatString = format,
            [Grid.ColumnProperty] = column
        };
        if (minimum is double minimumValue)
            input.Minimum = (decimal)minimumValue;

        input.ValueChanged += (_, args) =>
        {
            if (args.NewValue is decimal value)
                set((float)value);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(column == 0 ? 0 : 2, 0, column == 2 ? 0 : 2, 0),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Width = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brush.Parse(color)
                },
                input
            }
        };
    }

    private static Control CreateMaterialSection(SceneObject selected)
    {
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(CreateHeader("MATERIAL & COLOR"));
        section.Children.Add(CreateSliderGrid(selected, "Red", () => selected.ColorR, value => selected.ColorR = value,
            "Green", () => selected.ColorG, value => selected.ColorG = value));
        section.Children.Add(CreateSliderGrid(selected, "Blue", () => selected.ColorB, value => selected.ColorB = value,
            "Alpha", () => selected.ColorA, value => selected.ColorA = value));

        var visible = new CheckBox { Content = "Is Visible", IsChecked = selected.IsVisible };
        visible.IsCheckedChanged += (_, _) => selected.IsVisible = visible.IsChecked == true;
        section.Children.Add(visible);
        return section;
    }

    private static Control CreateSliderGrid(SceneObject selected, string leftName, Func<float> leftGet, Action<float> leftSet,
        string rightName, Func<float> rightGet, Action<float> rightSet)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*, *") };
        grid.Children.Add(CreateSlider(leftName, leftGet, leftSet));
        grid.Children.Add(CreateSlider(rightName, rightGet, rightSet, 1));
        return grid;
    }

    private static Control CreateSlider(string name, Func<float> get, Action<float> set, int column = 0)
    {
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = get()
        };
        slider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
                set((float)slider.Value);
        };

        return new StackPanel
        {
            [Grid.ColumnProperty] = column,
            Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 1 ? 0 : 4, 0),
            Children =
            {
                new TextBlock { Text = name, FontSize = 11 },
                slider
            }
        };
    }

    private static Control CreateSection(string title, Control child) =>
        new StackPanel
        {
            Spacing = 4,
            Children = { CreateHeader(title), child }
        };

    private static TextBlock CreateHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Foreground = Brush.Parse("#9CA3AF")
        };

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Scene.SelectedObject) || sender is SceneObject)
            StateHasChanged();
    }

    private static IEnumerable<PropertyInfo> GetInspectableProperties(EngineComponent component) =>
        component.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetCustomAttribute<PropertyAttribute>() is not null && property.CanRead);

    private static string FormatValue(object? value) => value switch
    {
        null => "None",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static bool IsEditable(PropertyInfo property) => property.CanWrite && IsSimpleType(property.PropertyType);

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
        catch (FormatException) { return false; }
        catch (InvalidCastException) { return false; }
    }
}
