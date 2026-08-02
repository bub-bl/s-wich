using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Crowbar.Engine.Rendering;

namespace Crowbar.Engine;

public class Scene : INotifyPropertyChanged
{
    // Mouse rotation sensitivity, in degrees per pixel.
    public const float CameraRotationSensitivity = 0.1f;
    private const float CameraPositionSmoothing = 18f;
    private const float CameraRotationSmoothing = 60f;
    private SceneObject? _selectedObject;
    private bool _isWireframe;
    private bool _isPaused;
    private float _cameraYaw = 45f;
    private float _cameraPitch = 30f;
    private float _cameraYawTarget = 45f;
    private float _cameraPitchTarget = 30f;
    private float _cameraDistance = 6.0f;
    private float _cameraTargetX = 0f;
    private float _cameraTargetY = 0f;
    private float _cameraTargetZ = 0f;
    private float _cameraPositionX = 4.242641f;
    private float _cameraPositionY = 3.0f;
    private float _cameraPositionZ = 4.242641f;
    private float _cameraPositionTargetX = 4.242641f;
    private float _cameraPositionTargetY = 3.0f;
    private float _cameraPositionTargetZ = 4.242641f;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SceneObject> Objects { get; } = new();

    public SceneObject? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (_selectedObject != value)
            {
                if (_selectedObject != null) _selectedObject.IsSelected = false;
                _selectedObject = value;
                if (_selectedObject != null) _selectedObject.IsSelected = true;
                OnPropertyChanged();
            }
        }
    }

    public bool IsWireframe
    {
        get => _isWireframe;
        set => SetField(ref _isWireframe, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        set => SetField(ref _isPaused, value);
    }

    public float CameraYaw
    {
        get => _cameraYaw;
        set
        {
            SetField(ref _cameraYaw, value);
            _cameraYawTarget = value;
        }
    }

    public float CameraPitch
    {
        get => _cameraPitch;
        set
        {
            SetField(ref _cameraPitch, value);
            _cameraPitchTarget = value;
        }
    }

    public float CameraDistance
    {
        get => _cameraDistance;
        set => SetField(ref _cameraDistance, value);
    }

    public float CameraTargetX
    {
        get => _cameraTargetX;
        set => SetField(ref _cameraTargetX, value);
    }

    public float CameraTargetY
    {
        get => _cameraTargetY;
        set => SetField(ref _cameraTargetY, value);
    }

    public float CameraTargetZ
    {
        get => _cameraTargetZ;
        set => SetField(ref _cameraTargetZ, value);
    }

    public float CameraPositionX
    {
        get => _cameraPositionX;
        set
        {
            SetField(ref _cameraPositionX, value);
            _cameraPositionTargetX = value;
        }
    }

    public float CameraPositionY
    {
        get => _cameraPositionY;
        set
        {
            SetField(ref _cameraPositionY, value);
            _cameraPositionTargetY = value;
        }
    }

    public float CameraPositionZ
    {
        get => _cameraPositionZ;
        set
        {
            SetField(ref _cameraPositionZ, value);
            _cameraPositionTargetZ = value;
        }
    }

    public void ResetCamera()
    {
        CameraYaw = 45f;
        CameraPitch = 30f;
        CameraDistance = 6.0f;
        CameraTargetX = 0f;
        CameraTargetY = 0f;
        CameraTargetZ = 0f;
        CameraPositionX = 4.242641f;
        CameraPositionY = 3.0f;
        CameraPositionZ = 4.242641f;
    }

    public void RotateCamera(float yawDelta, float pitchDelta)
    {
        _cameraYawTarget -= yawDelta;
        _cameraPitchTarget = Math.Clamp(_cameraPitchTarget + pitchDelta, -89f, 89f);
    }

    public void MoveCamera(Vector3 offset)
    {
        _cameraPositionTargetX += offset.X;
        _cameraPositionTargetY += offset.Y;
        _cameraPositionTargetZ += offset.Z;
    }

    public void UpdateCameraPositionSmoothing(float deltaTime)
    {
        float clampedDeltaTime = Math.Clamp(deltaTime, 0f, 0.1f);
        float positionSmoothing = 1f - MathF.Exp(-CameraPositionSmoothing * clampedDeltaTime);
        _cameraPositionX = Lerp(_cameraPositionX, _cameraPositionTargetX, positionSmoothing);
        _cameraPositionY = Lerp(_cameraPositionY, _cameraPositionTargetY, positionSmoothing);
        _cameraPositionZ = Lerp(_cameraPositionZ, _cameraPositionTargetZ, positionSmoothing);

        float rotationSmoothing = 1f - MathF.Exp(-CameraRotationSmoothing * clampedDeltaTime);
        _cameraYaw = Lerp(_cameraYaw, _cameraYawTarget, rotationSmoothing);
        _cameraPitch = Lerp(_cameraPitch, _cameraPitchTarget, rotationSmoothing);
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
