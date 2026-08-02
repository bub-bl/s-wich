using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public class Scene : IValid, INotifyPropertyChanged
{
    // Mouse rotation sensitivity, in degrees per pixel.
    public const float CameraRotationSensitivity = 0.1f;
    private const float CAMERA_POSITION_SMOOTHING = 18f;
    private const float CAMERA_ROTATION_SMOOTHING = 60f;
    private float _cameraYaw = 45f;
    private float _cameraPitch = 30f;
    private float _cameraYawTarget = 45f;
    private float _cameraPitchTarget = 30f;
    private float _cameraPositionX = 4.242641f;
    private float _cameraPositionY = 3.0f;
    private float _cameraPositionZ = 4.242641f;
    private float _cameraPositionTargetX = 4.242641f;
    private float _cameraPositionTargetY = 3.0f;
    private float _cameraPositionTargetZ = 4.242641f;

    public event PropertyChangedEventHandler? PropertyChanged;
    
    public SceneFileMetadata? Metadata { get; internal init; }
    public ObservableCollection<SceneObject> GameObjects { get; } = [];
    public bool IsValid => Current == this;
    
    public static Scene? Current { get; private set; }

    public SceneObject? SelectedObject
    {
        get;
        set
        {
            if (field == value) return;
            
            field?.IsSelected = false;
            field = value;
            field?.IsSelected = true;
                
            OnPropertyChanged();
        }
    }

    public bool IsWireframe
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsPaused
    {
        get;
        set => SetField(ref field, value);
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
        get;
        set => SetField(ref field, value);
    } = 6.0f;

    public float CameraTargetX
    {
        get;
        set => SetField(ref field, value);
    }

    public float CameraTargetY
    {
        get;
        set => SetField(ref field, value);
    }

    public float CameraTargetZ
    {
        get;
        set => SetField(ref field, value);
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
    
    public IDisposable Push()
    {
        var previous = Current;
        Current = this;

        return new DisposableAction(() =>
        {
            Current = previous;
        });
    }
    
    internal void AddGameObject(GameObject gameObject)
    {
        // GameObjects.Add(gameObject);
    }

    internal bool RemoveGameObject(GameObject gameObject)
    {
        // return GameObjects.Remove(gameObject);
        return false;
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
        var clampedDeltaTime = Math.Clamp(deltaTime, 0f, 0.1f);
        var positionSmoothing = 1f - MathF.Exp(-CAMERA_POSITION_SMOOTHING * clampedDeltaTime);
        
        _cameraPositionX = float.Lerp(_cameraPositionX, _cameraPositionTargetX, positionSmoothing);
        _cameraPositionY = float.Lerp(_cameraPositionY, _cameraPositionTargetY, positionSmoothing);
        _cameraPositionZ = float.Lerp(_cameraPositionZ, _cameraPositionTargetZ, positionSmoothing);

        var rotationSmoothing = 1f - MathF.Exp(-CAMERA_ROTATION_SMOOTHING * clampedDeltaTime);
        
        _cameraYaw = float.Lerp(_cameraYaw, _cameraYawTarget, rotationSmoothing);
        _cameraPitch = float.Lerp(_cameraPitch, _cameraPitchTarget, rotationSmoothing);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}