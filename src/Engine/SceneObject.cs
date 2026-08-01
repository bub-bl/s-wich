using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public class SceneObject : INotifyPropertyChanged
{
    private string _name = "GameObject";
    private float _positionX;
    private float _positionY;
    private float _positionZ;
    private float _rotationX;
    private float _rotationY;
    private float _rotationZ;
    private float _scaleX = 1f;
    private float _scaleY = 1f;
    private float _scaleZ = 1f;
    private float _colorR = 0.2f;
    private float _colorG = 0.6f;
    private float _colorB = 1.0f;
    private float _colorA = 1.0f;
    private bool _isVisible = true;
    private bool _isSelected;
    private string _meshType = "Cube";
    private Material? _material;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public float PositionX
    {
        get => _positionX;
        set => SetField(ref _positionX, value);
    }

    public float PositionY
    {
        get => _positionY;
        set => SetField(ref _positionY, value);
    }

    public float PositionZ
    {
        get => _positionZ;
        set => SetField(ref _positionZ, value);
    }

    public float RotationX
    {
        get => _rotationX;
        set => SetField(ref _rotationX, value);
    }

    public float RotationY
    {
        get => _rotationY;
        set => SetField(ref _rotationY, value);
    }

    public float RotationZ
    {
        get => _rotationZ;
        set => SetField(ref _rotationZ, value);
    }

    public float ScaleX
    {
        get => _scaleX;
        set => SetField(ref _scaleX, value);
    }

    public float ScaleY
    {
        get => _scaleY;
        set => SetField(ref _scaleY, value);
    }

    public float ScaleZ
    {
        get => _scaleZ;
        set => SetField(ref _scaleZ, value);
    }

    public float ColorR
    {
        get => _colorR;
        set => SetField(ref _colorR, value);
    }

    public float ColorG
    {
        get => _colorG;
        set => SetField(ref _colorG, value);
    }

    public float ColorB
    {
        get => _colorB;
        set => SetField(ref _colorB, value);
    }

    public float ColorA
    {
        get => _colorA;
        set => SetField(ref _colorA, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string MeshType
    {
        get => _meshType;
        set => SetField(ref _meshType, value);
    }

    public Material? Material
    {
        get => _material;
        set => SetField(ref _material, value);
    }

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
