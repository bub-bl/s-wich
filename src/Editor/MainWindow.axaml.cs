using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Crowbar.Engine;

namespace Crowbar.Editor;

public partial class MainWindow : Window
{
    private const double AssetsDrawerOuterMargin = 16;
    private Point _lastMousePos;
    private bool _isOrbiting;
    private bool _isPanning;
    private bool _isAssetsDrawerOpen;
    private DispatcherTimer? _assetsDrawerAnimationTimer;
    private Stopwatch? _assetsDrawerAnimationClock;
    private double _assetsDrawerAnimationStart;
    private double _assetsDrawerAnimationTarget;

    public Scene Scene { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        AssetsDrawerPopup.PlacementTarget = AssetsDrawerAnchor;
        UpdateAssetsDrawerWidth();
        AssetsDrawer.RenderTransform = new TranslateTransform(0, 360);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += OnMainWindowSizeChanged;

        InitDefaultScene();
        Log("Engine initialized with Avalonia 12 and Silk.NET WebGPU (WGPU).");
        Log("Viewport hardware acceleration active.");
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

        Scene.Objects.Add(cube1);
        Scene.Objects.Add(cube2);
        Scene.Objects.Add(pyramid);

        const string modelPath = "Assets/model.obj";
        if (File.Exists(modelPath) || File.Exists(Path.Combine(AppContext.BaseDirectory, modelPath)))
        {
            try
            {
                var modelObject = new SceneObject
                {
                    Name = "Imported Model",
                    Model = Model.Load(modelPath),
                    PositionX = 0f,
                    PositionY = 0.5f,
                    PositionZ = -2f,
                    ScaleX = 1f,
                    ScaleY = 1f,
                    ScaleZ = 1f,
                    ColorR = 0.85f,
                    ColorG = 0.75f,
                    ColorB = 0.35f,
                    MeshType = "Model"
                };
                Scene.Objects.Add(modelObject);
                Log($"Loaded model: {modelPath}");
            }
            catch (Exception exception) when (exception is IOException or FormatException)
            {
                Log($"Could not load model '{modelPath}': {exception.Message}");
            }
        }

        Scene.SelectedObject = cube1;
    }

    private void OnAddCubeClick(object? sender, RoutedEventArgs e)
    {
        int count = Scene.Objects.Count + 1;
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
        Scene.Objects.Add(obj);
        Scene.SelectedObject = obj;
        Log($"Created new Scene Object: '{obj.Name}'");
    }

    private void OnAddPyramidClick(object? sender, RoutedEventArgs e)
    {
        int count = Scene.Objects.Count + 1;
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
        Scene.Objects.Add(obj);
        Scene.SelectedObject = obj;
        Log($"Created new Scene Object: '{obj.Name}'");
    }

    private void OnDeleteObjectClick(object? sender, RoutedEventArgs e)
    {
        if (Scene.SelectedObject != null)
        {
            string name = Scene.SelectedObject.Name;
            Scene.Objects.Remove(Scene.SelectedObject);
            Scene.SelectedObject = Scene.Objects.Count > 0 ? Scene.Objects[0] : null;
            Log($"Removed Scene Object: '{name}'");
        }
    }

    private void OnTogglePlayClick(object? sender, RoutedEventArgs e)
    {
        Scene.IsPaused = !Scene.IsPaused;
        if (sender is Button btn)
        {
            btn.Content = Scene.IsPaused ? "▶ Play" : "⏸ Pause";
            btn.Background = Avalonia.Media.Brush.Parse(Scene.IsPaused ? "#2563EB" : "#D97706");
        }
        Log(Scene.IsPaused ? "Engine simulation paused." : "Engine simulation started.");
    }

    private void OnResetCameraClick(object? sender, RoutedEventArgs e)
    {
        Scene.ResetCamera();
        Log("Viewport camera reset.");
    }

    private void OnNewSceneClick(object? sender, RoutedEventArgs e)
    {
        Scene.Objects.Clear();
        InitDefaultScene();
        Log("New scene loaded.");
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
        else if (e.Key == Key.Escape && _isAssetsDrawerOpen)
        {
            SetAssetsDrawerOpen(false);
            e.Handled = true;
        }
    }

    private void OnCloseAssetsDrawerClick(object? sender, RoutedEventArgs e)
    {
        SetAssetsDrawerOpen(false);
    }

    private void OnConsoleTabClick(object? sender, RoutedEventArgs e)
    {
        SetDrawerTab(showAssets: false);
    }

    private void OnAssetsTabClick(object? sender, RoutedEventArgs e)
    {
        SetDrawerTab(showAssets: true);
    }

    private void SetDrawerTab(bool showAssets)
    {
        BtnConsoleTab.IsChecked = !showAssets;
        BtnAssetsTab.IsChecked = showAssets;
        ConsoleTabContent.IsVisible = !showAssets;
        AssetsTabContent.IsVisible = showAssets;
    }

    private void ToggleAssetsDrawer()
    {
        SetAssetsDrawerOpen(!_isAssetsDrawerOpen);
    }

    private void SetAssetsDrawerOpen(bool isOpen)
    {
        _isAssetsDrawerOpen = isOpen;
        // Do not keep a closed Popup alive: its native/layout surface can still
        // intercept pointer input even when its content is translated off-screen.
        AssetsDrawerPopup.IsOpen = isOpen;
        AnimateAssetsDrawer(isOpen ? 0 : AssetsDrawer.Height);
    }

    private void OnMainWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateAssetsDrawerWidth();
    }

    private void UpdateAssetsDrawerWidth()
    {
        AssetsDrawer.Width = Math.Max(0, Bounds.Width - AssetsDrawerOuterMargin);
    }

    private void AnimateAssetsDrawer(double target)
    {
        _assetsDrawerAnimationTimer?.Stop();
        _assetsDrawerAnimationStart = (AssetsDrawer.RenderTransform as TranslateTransform)?.Y ?? AssetsDrawer.Height;
        _assetsDrawerAnimationTarget = target;
        _assetsDrawerAnimationClock = Stopwatch.StartNew();
        _assetsDrawerAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _assetsDrawerAnimationTimer.Tick += OnAssetsDrawerAnimationTick;
        _assetsDrawerAnimationTimer.Start();
    }

    private void OnAssetsDrawerAnimationTick(object? sender, EventArgs e)
    {
        const double durationMs = 220;
        double progress = Math.Clamp((_assetsDrawerAnimationClock?.Elapsed.TotalMilliseconds ?? durationMs) / durationMs, 0, 1);
        double easedProgress = 1 - Math.Pow(1 - progress, 3);
        double y = _assetsDrawerAnimationStart + (_assetsDrawerAnimationTarget - _assetsDrawerAnimationStart) * easedProgress;
        AssetsDrawer.RenderTransform = new TranslateTransform(0, y);

        if (progress >= 1)
        {
            _assetsDrawerAnimationTimer?.Stop();
            _assetsDrawerAnimationTimer = null;
            _assetsDrawerAnimationClock = null;
        }
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
                    Scene.CameraYaw -= (float)delta.X * 0.4f;
                    Scene.CameraPitch = Math.Clamp(Scene.CameraPitch + (float)delta.Y * 0.4f, -89f, 89f);
                }
                else if (_isPanning)
                {
                    float sensitivity = Scene.CameraDistance * 0.002f;
                    float yawRad = MathF.PI / 180f * Scene.CameraYaw;
                    Vector3 right = new Vector3(MathF.Cos(yawRad), 0, -MathF.Sin(yawRad));
                    Vector3 up = Vector3.UnitY;

                    Vector3 panOffset = (right * (float)-delta.X + up * (float)delta.Y) * sensitivity;
                    Scene.CameraTargetX += panOffset.X;
                    Scene.CameraTargetY += panOffset.Y;
                    Scene.CameraTargetZ += panOffset.Z;
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
        float zoomDelta = (float)e.Delta.Y * 0.5f;
        Scene.CameraDistance = Math.Clamp(Scene.CameraDistance - zoomDelta, 1.0f, 50.0f);
        e.Handled = true;
    }

    public void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        TxtConsole.Text += $"[{timestamp}] {message}\n";
        TxtConsole.CaretIndex = TxtConsole.Text?.Length ?? 0;
    }
}
