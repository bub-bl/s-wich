using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public sealed class SceneObject : INotifyPropertyChanged
{
    internal Renderer? OwnerRenderer { get; set; }
    public Renderer? Renderer => OwnerRenderer;

    public bool RenderingEnabled { get; set; } = true;
    private Transform _localWorldTransform = Transform.Zero;

    public Transform WorldTransform
    {
        get => OwnerRenderer?.GameObject?.WorldTransform ?? _localWorldTransform;
        set
        {
            _localWorldTransform = value;
            if (OwnerRenderer?.GameObject is { } gameObject)
                gameObject.WorldTransform = value;
            OnPropertyChanged();
        }
    }
    
    public Vector3 WorldPosition
    {
        get => WorldTransform.Position;
        set
        {
            WorldTransform = WorldTransform.WithPosition(value);
        }
    }
    
    public Rotation WorldRotation
    {
        get => WorldTransform.Rotation;
        set
        {
            WorldTransform = WorldTransform.WithRotation(value);
        }
    }

    public Vector3 WorldScale
    {
        get => WorldTransform.Scale;
        set
        {
            WorldTransform = WorldTransform.WithScale(value);
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get;
        set => SetField(ref field, value);
    } = "GameObject";

    public float PositionX { get => WorldPosition.X; set => WorldPosition = new(value, WorldPosition.Y, WorldPosition.Z); }
    public float PositionY { get => WorldPosition.Y; set => WorldPosition = new(WorldPosition.X, value, WorldPosition.Z); }
    public float PositionZ { get => WorldPosition.Z; set => WorldPosition = new(WorldPosition.X, WorldPosition.Y, value); }
    public float RotationX { get => WorldRotation.Pitch(); set => WorldRotation = Rotation.From(new Angles(value, RotationY, RotationZ)); }
    public float RotationY { get => WorldRotation.Yaw(); set => WorldRotation = Rotation.From(new Angles(RotationX, value, RotationZ)); }
    public float RotationZ { get => WorldRotation.Roll(); set => WorldRotation = Rotation.From(new Angles(RotationX, RotationY, value)); }
    public float ScaleX { get => WorldScale.X; set => WorldScale = new(value, WorldScale.Y, WorldScale.Z); }
    public float ScaleY { get => WorldScale.Y; set => WorldScale = new(WorldScale.X, value, WorldScale.Z); }
    public float ScaleZ { get => WorldScale.Z; set => WorldScale = new(WorldScale.X, WorldScale.Y, value); }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
