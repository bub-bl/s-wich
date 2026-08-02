using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public class SceneObject : INotifyPropertyChanged
{
    public SceneObject()
    {
    }

    internal Renderer? OwnerRenderer { get; set; }
    public Renderer? Renderer => OwnerRenderer;
    public bool RenderingEnabled { get; set; } = true;
    public Transform Transform { get; set; } = new();
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get;
        set => SetField(ref field, value);
    } = "GameObject";

    public float PositionX { get => Transform.Position.X; set => SetPosition(new(value, Transform.Position.Y, Transform.Position.Z)); }

    public float PositionY { get => Transform.Position.Y; set => SetPosition(new(Transform.Position.X, value, Transform.Position.Z)); }

    public float PositionZ { get => Transform.Position.Z; set => SetPosition(new(Transform.Position.X, Transform.Position.Y, value)); }

    public float RotationX { get => Transform.Rotation.Pitch(); set => SetRotation(new Angles(value, Transform.Rotation.Yaw(), Transform.Rotation.Roll())); }

    public float RotationY { get => Transform.Rotation.Yaw(); set => SetRotation(new Angles(Transform.Rotation.Pitch(), value, Transform.Rotation.Roll())); }

    public float RotationZ { get => Transform.Rotation.Roll(); set => SetRotation(new Angles(Transform.Rotation.Pitch(), Transform.Rotation.Yaw(), value)); }

    public float ScaleX { get => Transform.Scale.X; set => SetScale(new(value, Transform.Scale.Y, Transform.Scale.Z)); }

    public float ScaleY { get => Transform.Scale.Y; set => SetScale(new(Transform.Scale.X, value, Transform.Scale.Z)); }

    public float ScaleZ { get => Transform.Scale.Z; set => SetScale(new(Transform.Scale.X, Transform.Scale.Y, value)); }

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

    private void SetPosition(Vector3 position)
    {
        if (Transform.Position == position) return;
        Transform = Transform.WithPosition(position);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Transform)));
    }

    private void SetRotation(Angles angles)
    {
        Rotation rotation = Rotation.From(angles);
        if (Transform.Rotation.AlmostEqual(rotation)) return;
        Transform = Transform.WithRotation(rotation);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Transform)));
    }

    private void SetScale(Vector3 scale)
    {
        if (Transform.Scale == scale) return;
        Transform = Transform.WithScale(scale);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Transform)));
    }
}
