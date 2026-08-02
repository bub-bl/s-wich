using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Crowbar.Engine;

public class Scene : INotifyPropertyChanged
{
    private SceneObject? _selectedObject;
    private bool _isWireframe;
    private bool _isPaused;
    private float _cameraYaw = 45f;
    private float _cameraPitch = 30f;
    private float _cameraDistance = 6.0f;
    private float _cameraTargetX = 0f;
    private float _cameraTargetY = 0f;
    private float _cameraTargetZ = 0f;
    private float _cameraPositionX = 4.242641f;
    private float _cameraPositionY = 3.0f;
    private float _cameraPositionZ = 4.242641f;

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
        set => SetField(ref _cameraYaw, value);
    }

    public float CameraPitch
    {
        get => _cameraPitch;
        set => SetField(ref _cameraPitch, value);
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
        set => SetField(ref _cameraPositionX, value);
    }

    public float CameraPositionY
    {
        get => _cameraPositionY;
        set => SetField(ref _cameraPositionY, value);
    }

    public float CameraPositionZ
    {
        get => _cameraPositionZ;
        set => SetField(ref _cameraPositionZ, value);
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
