using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public sealed class SceneObject : INotifyPropertyChanged
{
    internal Renderer? OwnerRenderer { get; set; }
    public Renderer? Renderer => OwnerRenderer;

    public bool RenderingEnabled { get; set; } = true;

    public Transform WorldTransform
    {
        get => OwnerRenderer?.GameObject?.WorldTransform ?? field;
        set
        {
            var current = OwnerRenderer?.GameObject?.WorldTransform ?? field;
            if (current.Equals(value)) return;

            field = value;
            
            if (OwnerRenderer?.GameObject is { } gameObject)
            {
                if (!gameObject.WorldTransform.Equals(value))
                    gameObject.WorldTransform = value;
            }

            OnPropertyChanged(nameof(WorldTransform));
            OnPropertyChanged(nameof(WorldPosition));
            OnPropertyChanged(nameof(WorldRotation));
            OnPropertyChanged(nameof(WorldScale));
            OnPropertyChanged(nameof(PositionX));
            OnPropertyChanged(nameof(PositionY));
            OnPropertyChanged(nameof(PositionZ));
            OnPropertyChanged(nameof(RotationX));
            OnPropertyChanged(nameof(RotationY));
            OnPropertyChanged(nameof(RotationZ));
            OnPropertyChanged(nameof(ScaleX));
            OnPropertyChanged(nameof(ScaleY));
            OnPropertyChanged(nameof(ScaleZ));
        }
    } = Transform.Zero;

    public Vector3 WorldPosition
    {
        get => WorldTransform.Position;
        set
        {
            if (WorldPosition != value)
                WorldTransform = WorldTransform.WithPosition(value);
        }
    }

    public Rotation WorldRotation
    {
        get => WorldTransform.Rotation;
        set
        {
            if (WorldRotation != value)
                WorldTransform = WorldTransform.WithRotation(value);
        }
    }

    public Vector3 WorldScale
    {
        get => WorldTransform.Scale;
        set
        {
            if (WorldScale != value)
                WorldTransform = WorldTransform.WithScale(value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get;
        set => SetField(ref field, value);
    } = "GameObject";

    public float PositionX
    {
        get => WorldPosition.X;
        set => WorldPosition = WorldPosition with { X = value };
    }

    public float PositionY
    {
        get => WorldPosition.Y;
        set => WorldPosition = WorldPosition with { Y = value };
    }

    public float PositionZ
    {
        get => WorldPosition.Z;
        set => WorldPosition = WorldPosition with { Z = value };
    }

    public float RotationX
    {
        get => WorldRotation.Pitch();
        set => WorldRotation = Rotation.From(new Angles(value, RotationY, RotationZ));
    }

    public float RotationY
    {
        get => WorldRotation.Yaw();
        set => WorldRotation = Rotation.From(new Angles(RotationX, value, RotationZ));
    }

    public float RotationZ
    {
        get => WorldRotation.Roll();
        set => WorldRotation = Rotation.From(new Angles(RotationX, RotationY, value));
    }

    public float ScaleX
    {
        get => WorldScale.X;
        set => WorldScale = WorldScale with { X = value };
    }

    public float ScaleY
    {
        get => WorldScale.Y;
        set => WorldScale = WorldScale with { Y = value };
    }

    public float ScaleZ
    {
        get => WorldScale.Z;
        set => WorldScale = WorldScale with { Z = value };
    }

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