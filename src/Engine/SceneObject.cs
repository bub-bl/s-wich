using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public class SceneObject : INotifyPropertyChanged
{
    public bool RenderingEnabled { get; set; } = true;
    public Transform Transform { get; set; } = new();
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get;
        set => SetField(ref field, value);
    } = "GameObject";

    public float PositionX
    {
        get;
        set => SetField(ref field, value);
    }

    public float PositionY
    {
        get;
        set => SetField(ref field, value);
    }

    public float PositionZ
    {
        get;
        set => SetField(ref field, value);
    }

    public float RotationX
    {
        get;
        set => SetField(ref field, value);
    }

    public float RotationY
    {
        get;
        set => SetField(ref field, value);
    }

    public float RotationZ
    {
        get;
        set => SetField(ref field, value);
    }

    public float ScaleX
    {
        get;
        set => SetField(ref field, value);
    } = 1f;

    public float ScaleY
    {
        get;
        set => SetField(ref field, value);
    } = 1f;

    public float ScaleZ
    {
        get;
        set => SetField(ref field, value);
    } = 1f;

    public float ColorR
    {
        get;
        set => SetField(ref field, value);
    } = 0.2f;

    public float ColorG
    {
        get;
        set => SetField(ref field, value);
    } = 0.6f;

    public float ColorB
    {
        get;
        set => SetField(ref field, value);
    } = 1.0f;

    public float ColorA
    {
        get;
        set => SetField(ref field, value);
    } = 1.0f;

    public bool IsVisible
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool IsSelected
    {
        get;
        set => SetField(ref field, value);
    }

    public string MeshType
    {
        get;
        set => SetField(ref field, value);
    } = "Cube";

    /// <summary>
    /// Optional imported model. When set, the renderer uses it instead of the
    /// built-in cube or pyramid selected by <see cref="MeshType"/>.
    /// </summary>
    public Model? Model
    {
        get;
        set => SetField(ref field, value);
    }

    public Material? Material
    {
        get;
        set => SetField(ref field, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
