using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Crowbar.Engine;
using Crowbar.Editor.Tools;

namespace Crowbar.Editor;

public partial class MainWindow : Window
{
    private Point _lastMousePos;
    private bool _isOrbiting;
    private bool _isPanning;
    private readonly EditorWindowManager _editorWindows;

    public Scene Scene { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Toolbar.BindMetrics(Viewport);
        Hierarchy.Scene = Scene;
        Inspector.Scene = Scene;
        _editorWindows = new EditorWindowManager(this);
        Toolbar.ActionRequested += OnToolbarAction;
        AddToolMenuItems();
        AssetsDrawerControl.SetPlacementTarget(AssetsDrawerControl);
        AssetsDrawerControl.SetWidth(Bounds.Width);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closing += OnMainWindowClosing;
        SizeChanged += OnMainWindowSizeChanged;

        InitDefaultScene();
        Log("Engine initialized with Avalonia 12 and Silk.NET WebGPU (WGPU).");
        Log("Viewport hardware acceleration active.");
    }

    private void AddToolMenuItems()
    {
        var fakeToolItem = new MenuItem
        {
            Header = "Fake C# Tool"
        };
        fakeToolItem.Click += OnOpenFakeToolClick;
        TitleBar.AddWindowMenuItem(fakeToolItem);
    }

    private void OnTitleBarAction(object? sender, TitleBarActionEventArgs e)
    {
        switch (e.Action)
        {
            case TitleBarAction.NewScene:
                OnNewSceneClick(this, new RoutedEventArgs());
                break;
            case TitleBarAction.Exit:
                OnExitClick(this, new RoutedEventArgs());
                break;
            case TitleBarAction.DeleteObject:
                OnDeleteObjectClick(this, new RoutedEventArgs());
                break;
            case TitleBarAction.AddCube:
                OnAddCubeClick(this, new RoutedEventArgs());
                break;
            case TitleBarAction.AddPyramid:
                OnAddPyramidClick(this, new RoutedEventArgs());
                break;
            case TitleBarAction.ResetCamera:
                OnResetCameraClick(this, new RoutedEventArgs());
                break;
        }
    }

    private void OnToolbarAction(object? sender, ToolbarActionEventArgs e)
    {
        switch (e.Action)
        {
            case ToolbarAction.TogglePlay:
                OnTogglePlayClick(this, new RoutedEventArgs());
                break;
            case ToolbarAction.ResetCamera:
                OnResetCameraClick(this, new RoutedEventArgs());
                break;
            case ToolbarAction.AddCube:
                OnAddCubeClick(this, new RoutedEventArgs());
                break;
            case ToolbarAction.AddPyramid:
                OnAddPyramidClick(this, new RoutedEventArgs());
                break;
            case ToolbarAction.DeleteObject:
                OnDeleteObjectClick(this, new RoutedEventArgs());
                break;
        }
    }

    private void OnOpenFakeToolClick(object? sender, RoutedEventArgs e)
    {
        var context = new EditorContext
        {
            Scene = Scene,
            Windows = _editorWindows
        };

        _editorWindows.Open(new FakeEditorTool(context));
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _editorWindows.CloseAll();
    }

    private void InitDefaultScene()
    {
        var cube1 = new SceneObject
        {
            Name = "Player Cube",
            PositionX = 0f,
            PositionY = 0.5f,
            PositionZ = 0f,
            ScaleX = 1f,
            ScaleY = 1f,
            ScaleZ = 1f,
            ColorR = 0.2f,
            ColorG = 0.6f,
            ColorB = 1.0f,
            MeshType = "Cube"
        };

        var cube2 = new SceneObject
        {
            Name = "Companion Cube",
            PositionX = 2.0f,
            PositionY = 0.5f,
            PositionZ = -1.0f,
            ScaleX = 0.8f,
            ScaleY = 0.8f,
            ScaleZ = 0.8f,
            ColorR = 0.9f,
            ColorG = 0.3f,
            ColorB = 0.4f,
            MeshType = "Cube"
        };

        var pyramid = new SceneObject
        {
            Name = "Pyramid Pillar",
            PositionX = -2.0f,
            PositionY = 0.75f,
            PositionZ = 1.0f,
            ScaleX = 1.2f,
            ScaleY = 1.5f,
            ScaleZ = 1.2f,
            ColorR = 0.2f,
            ColorG = 0.8f,
            ColorB = 0.4f,
            MeshType = "Pyramid"
        };

        Register(cube1);
        Register(cube2);
        Register(pyramid);

        const string modelPath = "Assets/scene.gltf";
        if (File.Exists(modelPath) || File.Exists(Path.Combine(AppContext.BaseDirectory, modelPath)))
        {
            try
            {
                Model model = Model.Load(modelPath);
                var modelObject = new SceneObject
                {
                    Name = "Industrial Work Light",
                    PositionX = 0f,
                    PositionY = 0.5f,
                    PositionZ = -2f,
                    ScaleX = 0.01f,
                    ScaleY = 0.01f,
                    ScaleZ = 0.01f,
                    ColorR = 0.85f,
                    ColorG = 0.75f,
                    ColorB = 0.35f,
                    MeshType = "Model"
                };
                Register(modelObject, model);
                Log($"Loaded model: {modelPath}");
            }
            catch (Exception exception) when (exception is IOException or FormatException or DllNotFoundException or EntryPointNotFoundException)
            {
                Log($"Could not load model '{modelPath}': {exception.Message}");
            }
        }

        Scene.SelectedObject = cube1;
    }

    private void OnAddCubeClick(object? sender, RoutedEventArgs e)
    {
        int count = Scene.GameObjects.Count + 1;
        var obj = new SceneObject
        {
            Name = $"Cube_{count}",
            PositionX = (float)(Random.Shared.NextDouble() * 4.0 - 2.0),
            PositionY = 0.5f,
            PositionZ = (float)(Random.Shared.NextDouble() * 4.0 - 2.0),
            ColorR = (float)Random.Shared.NextDouble(),
            ColorG = (float)Random.Shared.NextDouble(),
            ColorB = (float)Random.Shared.NextDouble(),
            MeshType = "Cube"
        };
        Register(obj);
        Scene.SelectedObject = obj;
        Log($"Created new Scene Object: '{obj.Name}'");
    }

    private void OnAddPyramidClick(object? sender, RoutedEventArgs e)
    {
        int count = Scene.GameObjects.Count + 1;
        var obj = new SceneObject
        {
            Name = $"Pyramid_{count}",
            PositionX = (float)(Random.Shared.NextDouble() * 4.0 - 2.0),
            PositionY = 0.5f,
            PositionZ = (float)(Random.Shared.NextDouble() * 4.0 - 2.0),
            ColorR = (float)Random.Shared.NextDouble(),
            ColorG = (float)Random.Shared.NextDouble(),
            ColorB = (float)Random.Shared.NextDouble(),
            MeshType = "Pyramid"
        };
        Register(obj);
        Scene.SelectedObject = obj;
        Log($"Created new Scene Object: '{obj.Name}'");
    }

    private void OnDeleteObjectClick(object? sender, RoutedEventArgs e)
    {
        if (Scene.SelectedObject != null)
        {
            string name = Scene.SelectedObject.Name;
            GameObject? gameObject = Scene.GameObjects.FirstOrDefault(item =>
                ReferenceEquals(item.GetComponent<ModelRenderer>()?.SceneObject, Scene.SelectedObject));
            if (gameObject != null)
                gameObject.Destroy();

            Scene.SelectedObject = Scene.GameObjects.Count > 0
                ? Scene.GameObjects[0].GetComponent<ModelRenderer>()?.SceneObject
                : null;
            Log($"Removed Scene Object: '{name}'");
        }
    }

    private void OnTogglePlayClick(object? sender, RoutedEventArgs e)
    {
        Scene.IsPaused = !Scene.IsPaused;
        Toolbar.SetPlaying(Scene.IsPaused);
        Log(Scene.IsPaused ? "Engine simulation paused." : "Engine simulation started.");
    }

    private void OnResetCameraClick(object? sender, RoutedEventArgs e)
    {
        Scene.ResetCamera();
        Log("Viewport camera reset.");
    }

    private void OnNewSceneClick(object? sender, RoutedEventArgs e)
    {
        Scene.ClearGameObjects();
        InitDefaultScene();
        Log("New scene loaded.");
    }

    private void Register(SceneObject sceneObject, Model? model = null)
    {
        var gameObject = new GameObject();
        gameObject.AddComponent(new ModelRenderer(sceneObject) { Model = model });
        gameObject.AddComponent(new RotateComponent());
        Scene.AddGameObject(gameObject);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            ToggleAssetsDrawer();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && AssetsDrawerControl.IsOpen)
        {
            SetAssetsDrawerOpen(false);
            e.Handled = true;
        }
    }

    private void ToggleAssetsDrawer()
    {
        AssetsDrawerControl.Toggle();
    }

    private void SetAssetsDrawerOpen(bool isOpen)
    {
        AssetsDrawerControl.SetOpen(isOpen);
    }

    private void OnMainWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        AssetsDrawerControl.SetWidth(Bounds.Width);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control ctrl)
        {
            ctrl.Focus();
            var point = e.GetCurrentPoint(ctrl);
            if (point.Properties.IsLeftButtonPressed || point.Properties.IsRightButtonPressed)
            {
                _isOrbiting = true;
                _lastMousePos = point.Position;
                e.Pointer.Capture(ctrl);
                e.Handled = true;
            }
            else if (point.Properties.IsMiddleButtonPressed)
            {
                _isPanning = true;
                _lastMousePos = point.Position;
                e.Pointer.Capture(ctrl);
                e.Handled = true;
            }
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isOrbiting || _isPanning)
        {
            if (sender is Control ctrl)
            {
                var point = e.GetCurrentPoint(ctrl);
                Point delta = point.Position - _lastMousePos;
                _lastMousePos = point.Position;

                if (_isOrbiting)
                {
                    Scene.RotateCamera((float)delta.X * Scene.CameraRotationSensitivity, (float)delta.Y * Scene.CameraRotationSensitivity);
                }
                else if (_isPanning)
                {
                    float sensitivity = Scene.CameraDistance * 0.002f;
                    float yawRad = MathF.PI / 180f * Scene.CameraYaw;
                    Vector3 right = new Vector3(MathF.Cos(yawRad), 0, -MathF.Sin(yawRad));
                    Vector3 up = Vector3.UnitY;

                    Vector3 panOffset = (right * (float)-delta.X + up * (float)delta.Y) * sensitivity;
                    MoveCamera(panOffset);
                }

                e.Handled = true;
            }
        }
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isOrbiting || _isPanning)
        {
            _isOrbiting = false;
            _isPanning = false;
            if (sender is Control ctrl)
            {
                e.Pointer.Capture(null);
            }
            e.Handled = true;
        }
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        float yawRad = MathF.PI / 180f * Scene.CameraYaw;
        float pitchRad = MathF.PI / 180f * Scene.CameraPitch;
        MoveCamera(GetCameraForward(yawRad, pitchRad) * ((float)e.Delta.Y * 0.5f));
        e.Handled = true;
    }

    private static Vector3 GetCameraForward(float yawRad, float pitchRad) =>
        Vector3.Normalize(new Vector3(
            -MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            -MathF.Sin(pitchRad),
            -MathF.Cos(pitchRad) * MathF.Cos(yawRad)));

    private void MoveCamera(Vector3 offset)
    {
        Scene.MoveCamera(offset);
    }

    public void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        AssetsDrawerControl.AppendLog(message);
    }
}
