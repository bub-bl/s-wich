using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;
using Color = Silk.NET.WebGPU.Color;

namespace Crowbar;

public unsafe class SilkViewportControl : NativeControlHost
{
    private nint _hwnd;
    private nint _previousWndProc;
    private NativeWndProc? _nativeWndProc;
    private Surface* _surface;
    private WebGpuAdapter? _adapter;
    private WebGpuDevice? _device;
    private Queue* _queue;

    private TextureFormat _swapChainFormat = TextureFormat.Bgra8Unorm;
    private Texture* _depthTexture;
    private TextureView* _depthTextureView;

    private ShaderModule* _meshShaderModule;
    private ShaderModule* _gridShaderModule;

    private RenderPipeline* _meshPipeline;
    private RenderPipeline* _wireframePipeline;
    private RenderPipeline* _selectionDepthPipeline;
    private RenderPipeline* _outlinePipeline;
    private RenderPipeline* _gridPipeline;

    private Buffer* _cubeVbo;
    private Buffer* _cubeEbo;
    private Buffer* _cubeWireframeEbo;
    private Buffer* _pyramidVbo;
    private Buffer* _pyramidEbo;
    private Buffer* _pyramidWireframeEbo;
    private Buffer* _gridVbo;

    private Buffer* _gridUniformBuffer;

    private BindGroup* _gridBindGroup;

    private BindGroupLayout* _meshBindGroupLayout;
    private BindGroupLayout* _gridBindGroupLayout;

    // QueueWriteBuffer is ordered on the queue. Reusing one uniform buffer for
    // several draws therefore makes every draw observe the last object's data.
    // Keep one buffer/bind group per object so each draw has stable uniforms.
    private readonly Dictionary<SceneObject, MeshGpuResources> _meshResources = new();

    private int _width = 800;
    private int _height = 600;

    private Point _lastMousePos;
    private bool _isOrbiting;
    private bool _isPanning;
    private bool _moveForward;
    private bool _moveBackward;
    private bool _moveLeft;
    private bool _moveRight;

    private const float CameraMoveSpeed = 5.0f;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastFrameTime;
    private int _frameCount;
    private double _fpsTimer;
    private DispatcherTimer? _timer;

    public static readonly StyledProperty<Scene?> SceneProperty =
        AvaloniaProperty.Register<SilkViewportControl, Scene?>(nameof(Scene));

    public static readonly StyledProperty<int> FpsProperty =
        AvaloniaProperty.Register<SilkViewportControl, int>(nameof(Fps));

    public static readonly StyledProperty<float> FrameTimeMsProperty =
        AvaloniaProperty.Register<SilkViewportControl, float>(nameof(FrameTimeMs));

    public static readonly StyledProperty<string> GpuVendorProperty =
        AvaloniaProperty.Register<SilkViewportControl, string>(nameof(GpuVendor), "Initializing...");

    public static readonly StyledProperty<string> GpuRendererProperty =
        AvaloniaProperty.Register<SilkViewportControl, string>(nameof(GpuRenderer), "Initializing...");

    public SilkViewportControl()
    {
        Focusable = true;

        PointerPressedEvent.AddClassHandler<SilkViewportControl>((ctrl, e) => ctrl.HandlePointerPressed(e), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PointerMovedEvent.AddClassHandler<SilkViewportControl>((ctrl, e) => ctrl.HandlePointerMoved(e), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PointerReleasedEvent.AddClassHandler<SilkViewportControl>((ctrl, e) => ctrl.HandlePointerReleased(e), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PointerWheelChangedEvent.AddClassHandler<SilkViewportControl>((ctrl, e) => ctrl.HandlePointerWheelChanged(e), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public int Fps
    {
        get => GetValue(FpsProperty);
        private set => SetValue(FpsProperty, value);
    }

    public float FrameTimeMs
    {
        get => GetValue(FrameTimeMsProperty);
        private set => SetValue(FrameTimeMsProperty, value);
    }

    public string GpuVendor
    {
        get => GetValue(GpuVendorProperty);
        private set => SetValue(GpuVendorProperty, value);
    }

    public string GpuRenderer
    {
        get => GetValue(GpuRendererProperty);
        private set => SetValue(GpuRendererProperty, value);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsWindows())
        {
            const int WS_CHILD = 0x40000000;
            const int WS_VISIBLE = 0x10000000;
            const int WS_CLIPCHILDREN = 0x02000000;
            const int WS_CLIPSIBLINGS = 0x04000000;
            const int WS_TABSTOP = 0x00010000;
            const int SS_NOTIFY = 0x00000100;

            _width = Math.Max(1, (int)Bounds.Width);
            _height = Math.Max(1, (int)Bounds.Height);

            _hwnd = CreateWindowExW(
                0, "static", "SilkWebGpuHost",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | WS_TABSTOP | SS_NOTIFY,
                0, 0, _width, _height,
                parent.Handle, nint.Zero, nint.Zero, nint.Zero);

            // NativeControlHost embeds a real HWND, so pointer events do not
            // bubble through Avalonia. Subclass the child window to handle
            // navigation input at the point where it is actually received.
            _nativeWndProc = NativeWindowProc;
            _previousWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_nativeWndProc));

            InitWebGpu();
            StartRenderingLoop();

            return new PlatformHandle(_hwnd, "HWND");
        }

        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopRenderingLoop();
        CleanupWebGpu();

        if (OperatingSystem.IsWindows() && _hwnd != nint.Zero)
        {
            if (_previousWndProc != nint.Zero)
            {
                SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _previousWndProc);
                _previousWndProc = nint.Zero;
            }

            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        _nativeWndProc = null;

        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _width = Math.Max(1, (int)e.NewSize.Width);
        _height = Math.Max(1, (int)e.NewSize.Height);

        if (_hwnd != nint.Zero && OperatingSystem.IsWindows())
        {
            SetWindowPos(_hwnd, nint.Zero, 0, 0, _width, _height, 0x0004 /* SWP_NOZORDER */ | 0x0002 /* SWP_NOMOVE */);
        }

        if (_surface != null && _device != null)
        {
            ReconfigureSurface();
        }
    }

    private void InitWebGpu()
    {
        WebGpuApi.Initialize();

        var hwndDesc = new SurfaceDescriptorFromWindowsHWND
        {
            Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromWindowsHwnd },
            Hwnd = (void*)_hwnd,
            Hinstance = (void*)Marshal.GetHINSTANCE(typeof(SilkViewportControl).Module)
        };

        var surfaceDesc = new SurfaceDescriptor
        {
            NextInChain = (ChainedStruct*)&hwndDesc
        };

        _surface = WebGpuApi.Wgpu.InstanceCreateSurface(WebGpuApi.Instance, in surfaceDesc);
        _adapter = new WebGpuAdapter(WebGpuApi.Instance, _surface);
        _device = _adapter.CreateDevice();
        _queue = _device.GetQueue();

        WebGpuApi.ConfigureDebugCallback(_device);

        GpuVendor = "WebGPU (WGPU / Native)";
        GpuRenderer = "Hardware Accelerated (Vulkan / D3D12)";

        _swapChainFormat = WebGpuApi.Wgpu.SurfaceGetPreferredFormat(_surface, _adapter);
        if (_swapChainFormat == TextureFormat.Undefined)
        {
            _swapChainFormat = TextureFormat.Bgra8Unorm;
        }

        ReconfigureSurface();
        InitBuffers();
        InitPipelines();
    }

    private void ReconfigureSurface()
    {
        if (_surface == null || _device == null) return;

        var surfaceConfig = new SurfaceConfiguration
        {
            Device = _device,
            Width = (uint)_width,
            Height = (uint)_height,
            Format = _swapChainFormat,
            PresentMode = PresentMode.Immediate, // Uncapped FPS
            Usage = TextureUsage.RenderAttachment,
            AlphaMode = CompositeAlphaMode.Auto
        };

        WebGpuApi.Wgpu.SurfaceConfigure(_surface, in surfaceConfig);

        // Recreate Depth Texture
        if (_depthTextureView != null) WebGpuApi.Wgpu.TextureViewRelease(_depthTextureView);
        if (_depthTexture != null)
        {
            WebGpuApi.Wgpu.TextureDestroy(_depthTexture);
            WebGpuApi.Wgpu.TextureRelease(_depthTexture);
        }

        var depthTextureDesc = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = (uint)_width, Height = (uint)_height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Depth24Plus,
            MipLevelCount = 1,
            SampleCount = 1
        };

        _depthTexture = WebGpuApi.Wgpu.DeviceCreateTexture(_device, in depthTextureDesc);
        _depthTextureView = WebGpuApi.Wgpu.TextureCreateView(_depthTexture, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshUniforms
    {
        public Matrix4x4 Model;
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        public Vector4 Color;
        public Vector3 LightDir;
        public uint IsSelected;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GridUniforms
    {
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        public Matrix4x4 ViewInv;
        public Matrix4x4 ProjInv;
    }

    private sealed class MeshGpuResources
    {
        public Buffer* UniformBuffer;
        public BindGroup* BindGroup;
    }

    private void InitBuffers()
    {
        if (_device == null) return;

        // 1. Cube Vertices (Pos 3 + Normal 3)
        float[] cubeVertices = new float[]
        {
            // Front
            -0.5f, -0.5f,  0.5f,  0f, 0f, 1f,   0.5f, -0.5f,  0.5f,  0f, 0f, 1f,   0.5f,  0.5f,  0.5f,  0f, 0f, 1f,  -0.5f,  0.5f,  0.5f,  0f, 0f, 1f,
            // Back
            -0.5f, -0.5f, -0.5f,  0f, 0f,-1f,  -0.5f,  0.5f, -0.5f,  0f, 0f,-1f,   0.5f,  0.5f, -0.5f,  0f, 0f,-1f,   0.5f, -0.5f, -0.5f,  0f, 0f,-1f,
            // Top
            -0.5f,  0.5f, -0.5f,  0f, 1f, 0f,  -0.5f,  0.5f,  0.5f,  0f, 1f, 0f,   0.5f,  0.5f,  0.5f,  0f, 1f, 0f,   0.5f,  0.5f, -0.5f,  0f, 1f, 0f,
            // Bottom
            -0.5f, -0.5f, -0.5f,  0f,-1f, 0f,   0.5f, -0.5f, -0.5f,  0f,-1f, 0f,   0.5f, -0.5f,  0.5f,  0f,-1f, 0f,  -0.5f, -0.5f,  0.5f,  0f,-1f, 0f,
            // Right
             0.5f, -0.5f, -0.5f,  1f, 0f, 0f,   0.5f,  0.5f, -0.5f,  1f, 0f, 0f,   0.5f,  0.5f,  0.5f,  1f, 0f, 0f,   0.5f, -0.5f,  0.5f,  1f, 0f, 0f,
            // Left
            -0.5f, -0.5f, -0.5f, -1f, 0f, 0f,  -0.5f, -0.5f,  0.5f, -1f, 0f, 0f,  -0.5f,  0.5f,  0.5f, -1f, 0f, 0f,  -0.5f,  0.5f, -0.5f, -1f, 0f, 0f,
        };

        ushort[] cubeIndices = new ushort[]
        {
             0,  1,  2,  2,  3,  0,
             4,  5,  6,  6,  7,  4,
             8,  9, 10, 10, 11,  8,
            12, 13, 14, 14, 15, 12,
            16, 17, 18, 18, 19, 16,
            20, 21, 22, 22, 23, 20
        };

        _cubeVbo = CreateGpuBuffer(cubeVertices, BufferUsage.Vertex);
        _cubeEbo = CreateGpuBuffer(cubeIndices, BufferUsage.Index);
        ushort[] cubeWireframeIndices = new ushort[]
        {
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            8, 9, 9, 10, 10, 11, 11, 8,
            12, 13, 13, 14, 14, 15, 15, 12,
            16, 17, 17, 18, 18, 19, 19, 16,
            20, 21, 21, 22, 22, 23, 23, 20
        };
        _cubeWireframeEbo = CreateGpuBuffer(cubeWireframeIndices, BufferUsage.Index);

        // 2. Pyramid Vertices
        float[] pyramidVertices = new float[]
        {
            // Front face
             0.0f,  0.75f, 0.0f,  0.0f, 0.447f, 0.894f,
            -0.5f, -0.5f,  0.5f,  0.0f, 0.447f, 0.894f,
             0.5f, -0.5f,  0.5f,  0.0f, 0.447f, 0.894f,
            // Right face
             0.0f,  0.75f, 0.0f,  0.894f, 0.447f, 0.0f,
             0.5f, -0.5f,  0.5f,  0.894f, 0.447f, 0.0f,
             0.5f, -0.5f, -0.5f,  0.894f, 0.447f, 0.0f,
            // Back face
             0.0f,  0.75f, 0.0f,  0.0f, 0.447f, -0.894f,
             0.5f, -0.5f, -0.5f,  0.0f, 0.447f, -0.894f,
            -0.5f, -0.5f, -0.5f,  0.0f, 0.447f, -0.894f,
            // Left face
             0.0f,  0.75f, 0.0f, -0.894f, 0.447f, 0.0f,
            -0.5f, -0.5f, -0.5f, -0.894f, 0.447f, 0.0f,
            -0.5f, -0.5f,  0.5f, -0.894f, 0.447f, 0.0f,
            // Bottom face
            -0.5f, -0.5f, -0.5f,  0.0f, -1.0f, 0.0f,
             0.5f, -0.5f, -0.5f,  0.0f, -1.0f, 0.0f,
             0.5f, -0.5f,  0.5f,  0.0f, -1.0f, 0.0f,
            -0.5f, -0.5f,  0.5f,  0.0f, -1.0f, 0.0f
        };

        ushort[] pyramidIndices = new ushort[]
        {
            0, 1, 2,
            3, 4, 5,
            6, 7, 8,
            9, 10, 11,
            12, 13, 14, 14, 15, 12
        };

        _pyramidVbo = CreateGpuBuffer(pyramidVertices, BufferUsage.Vertex);
        _pyramidEbo = CreateGpuBuffer(pyramidIndices, BufferUsage.Index);
        ushort[] pyramidWireframeIndices = new ushort[]
        {
            0, 1, 1, 2, 2, 0,
            3, 4, 4, 5, 5, 3,
            6, 7, 7, 8, 8, 6,
            9, 10, 10, 11, 11, 9,
            12, 13, 13, 14, 14, 15, 15, 12
        };
        _pyramidWireframeEbo = CreateGpuBuffer(pyramidWireframeIndices, BufferUsage.Index);

        // 3. Grid Quad Vertices
        float[] gridVertices = new float[]
        {
             1.0f,  1.0f, 0.0f,
            -1.0f, -1.0f, 0.0f,
            -1.0f,  1.0f, 0.0f,
             1.0f,  1.0f, 0.0f,
             1.0f, -1.0f, 0.0f,
            -1.0f, -1.0f, 0.0f
        };

        _gridVbo = CreateGpuBuffer(gridVertices, BufferUsage.Vertex);

        // 4. Uniform Buffers
        _gridUniformBuffer = CreateEmptyBuffer((ulong)sizeof(GridUniforms), BufferUsage.Uniform | BufferUsage.CopyDst);
    }

    private Buffer* CreateGpuBuffer<T>(T[] data, BufferUsage usage) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            var size = (ulong)(data.Length * sizeof(T));
            var desc = new BufferDescriptor
            {
                Usage = usage | BufferUsage.CopyDst,
                Size = size,
                MappedAtCreation = false
            };
            var buffer = WebGpuApi.Wgpu.DeviceCreateBuffer(_device!, in desc);
            WebGpuApi.Wgpu.QueueWriteBuffer(_queue, buffer, 0, ptr, (nuint)size);
            return buffer;
        }
    }

    private Buffer* CreateEmptyBuffer(ulong size, BufferUsage usage)
    {
        var desc = new BufferDescriptor
        {
            Usage = usage,
            Size = size,
            MappedAtCreation = false
        };
        return WebGpuApi.Wgpu.DeviceCreateBuffer(_device!, in desc);
    }

    private MeshGpuResources GetMeshResources(SceneObject obj)
    {
        if (_meshResources.TryGetValue(obj, out MeshGpuResources? resources))
        {
            return resources;
        }

        Buffer* uniformBuffer = CreateEmptyBuffer((ulong)sizeof(MeshUniforms), BufferUsage.Uniform | BufferUsage.CopyDst);
        var bindGroupEntry = new BindGroupEntry
        {
            Binding = 0,
            Buffer = uniformBuffer,
            Size = (ulong)sizeof(MeshUniforms)
        };
        var bindGroupDesc = new BindGroupDescriptor
        {
            Layout = _meshBindGroupLayout,
            EntryCount = 1,
            Entries = &bindGroupEntry
        };

        resources = new MeshGpuResources
        {
            UniformBuffer = uniformBuffer,
            BindGroup = WebGpuApi.Wgpu.DeviceCreateBindGroup(_device!, in bindGroupDesc)
        };
        _meshResources.Add(obj, resources);
        return resources;
    }

    private void CleanupMeshResources()
    {
        if (Scene == null) return;

        List<SceneObject>? removed = null;
        foreach (SceneObject obj in _meshResources.Keys)
        {
            if (!Scene.Objects.Contains(obj))
            {
                (removed ??= new List<SceneObject>()).Add(obj);
            }
        }

        if (removed == null) return;

        foreach (SceneObject obj in removed)
        {
            ReleaseMeshResources(_meshResources[obj]);
            _meshResources.Remove(obj);
        }
    }

    private void ReleaseMeshResources(MeshGpuResources resources)
    {
        if (resources.BindGroup != null)
        {
            WebGpuApi.Wgpu.BindGroupRelease(resources.BindGroup);
        }

        if (resources.UniformBuffer != null)
        {
            WebGpuApi.Wgpu.BufferDestroy(resources.UniformBuffer);
            WebGpuApi.Wgpu.BufferRelease(resources.UniformBuffer);
        }
    }

    private void InitPipelines()
    {
        if (_device == null) return;

        // 1. Mesh WGSL Shader Code
        const string meshWgsl = @"
struct MeshUniforms {
    model: mat4x4<f32>,
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    color: vec4<f32>,
    lightDir: vec3<f32>,
    isSelected: u32,
};

@group(0) @binding(0) var<uniform> u: MeshUniforms;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) frag_pos: vec3<f32>,
    @location(1) normal: vec3<f32>,
};

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    let world_pos = u.model * vec4<f32>(in.position, 1.0);
    out.frag_pos = world_pos.xyz;

    let norm_mat = mat3x3<f32>(u.model[0].xyz, u.model[1].xyz, u.model[2].xyz);
    out.normal = norm_mat * in.normal;
    out.clip_position = u.proj * u.view * world_pos;
    return out;
}

@vertex
fn vs_outline(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    let world_pos = u.model * vec4<f32>(in.position * 1.06, 1.0);
    out.frag_pos = world_pos.xyz;

    let norm_mat = mat3x3<f32>(u.model[0].xyz, u.model[1].xyz, u.model[2].xyz);
    out.normal = norm_mat * in.normal;
    out.clip_position = u.proj * u.view * world_pos;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let norm = normalize(in.normal);
    let light = normalize(u.lightDir);
    let diff = max(dot(norm, light), 0.25);
    var base_color = u.color.rgb * diff;
    if (u.isSelected != 0u) {
        return vec4<f32>(1.0, 0.72, 0.08, 1.0);
    }
    return vec4<f32>(base_color, u.color.a);
}

@fragment
fn fs_outline(in: VertexOutput) -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.72, 0.08, 1.0);
}
";

        // 2. Grid WGSL Shader Code
        const string gridWgsl = @"
struct GridUniforms {
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    viewInv: mat4x4<f32>,
    projInv: mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> gu: GridUniforms;

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) near_point: vec3<f32>,
    @location(1) far_point: vec3<f32>,
};

fn unprojectPoint(x: f32, y: f32, z: f32) -> vec3<f32> {
    let p = gu.viewInv * gu.projInv * vec4<f32>(x, y, z, 1.0);
    return p.xyz / p.w;
}

@vertex
fn vs_main(@location(0) position: vec3<f32>) -> VertexOutput {
    var out: VertexOutput;
    out.clip_position = vec4<f32>(position.xy, 0.0, 1.0);
    out.near_point = unprojectPoint(position.x, position.y, 0.0);
    out.far_point = unprojectPoint(position.x, position.y, 1.0);
    return out;
}

struct FragmentOutput {
    @location(0) color: vec4<f32>,
    @builtin(frag_depth) depth: f32,
};

@fragment
fn fs_main(in: VertexOutput) -> FragmentOutput {
    let t = -in.near_point.y / (in.far_point.y - in.near_point.y);
    if (t < 0.0 || t > 1.0) {
        discard;
    }

    let fragPos3D = in.near_point + t * (in.far_point - in.near_point);
    let clipSpacePos = gu.proj * gu.view * vec4<f32>(fragPos3D, 1.0);
    let realDepth = clipSpacePos.z / clipSpacePos.w;

    let coord = fragPos3D.xz;
    let derivative = fwidth(coord);
    let grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    let line = min(grid.x, grid.y);
    // The grid is transparent between its lines.  Discarding those pixels is
    // important because the grid is rendered after the meshes and must not
    // cover objects through an otherwise invisible fragment.
    if (line > 1.0) {
        discard;
    }
    let minimumz = min(derivative.y, 1.0);
    let minimumx = min(derivative.x, 1.0);

    var gridColor = vec4<f32>(0.35, 0.35, 0.38, 1.0 - min(line, 1.0));

    if (fragPos3D.x > -0.02 * minimumx && fragPos3D.x < 0.02 * minimumx) {
        gridColor = vec4<f32>(0.2, 0.4, 0.9, 1.0);
    }
    if (fragPos3D.z > -0.02 * minimumz && fragPos3D.z < 0.02 * minimumz) {
        gridColor = vec4<f32>(0.9, 0.2, 0.2, 1.0);
    }

    let fading = max(0.0, 1.0 - length(fragPos3D.xz) / 50.0);

    var out: FragmentOutput;
    out.color = gridColor * fading;
    out.depth = realDepth;
    return out;
}
";

        _meshShaderModule = CreateShaderModule(meshWgsl);
        _gridShaderModule = CreateShaderModule(gridWgsl);

        // BindGroupLayout for Mesh
        var meshLayoutEntry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform }
        };
        var meshLayoutDesc = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &meshLayoutEntry };
        _meshBindGroupLayout = WebGpuApi.Wgpu.DeviceCreateBindGroupLayout(_device, in meshLayoutDesc);

        // PipelineLayout for Mesh
        var meshLayoutLocal = _meshBindGroupLayout;
        var meshPipelineLayoutDesc = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &meshLayoutLocal };
        var meshPipelineLayout = WebGpuApi.Wgpu.DeviceCreatePipelineLayout(_device, in meshPipelineLayoutDesc);

        // Vertex Attributes for Mesh (Pos 3 + Normal 3)
        var vertexAttribs = stackalloc VertexAttribute[2];
        vertexAttribs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        vertexAttribs[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 3 * sizeof(float), ShaderLocation = 1 };

        var vertexBufferLayout = new VertexBufferLayout
        {
            ArrayStride = 6 * sizeof(float),
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 2,
            Attributes = vertexAttribs
        };

        var colorTarget = new ColorTargetState
        {
            Format = _swapChainFormat,
            WriteMask = ColorWriteMask.All
        };

        var fsEntryPoint = Marshal.StringToHGlobalAnsi("fs_main");
        var vsEntryPoint = Marshal.StringToHGlobalAnsi("vs_main");

        var fragmentState = new FragmentState
        {
            Module = _meshShaderModule,
            EntryPoint = (byte*)fsEntryPoint.ToPointer(),
            TargetCount = 1,
            Targets = &colorTarget
        };

        var depthStencilState = new DepthStencilState
        {
            Format = TextureFormat.Depth24Plus,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.Less,
            StencilFront = new StencilFaceState { Compare = CompareFunction.Always },
            StencilBack = new StencilFaceState { Compare = CompareFunction.Always }
        };

        var meshPipelineDesc = new RenderPipelineDescriptor
        {
            Layout = meshPipelineLayout,
            Vertex = new VertexState
            {
                Module = _meshShaderModule,
                EntryPoint = (byte*)vsEntryPoint.ToPointer(),
                BufferCount = 1,
                Buffers = &vertexBufferLayout
            },
            Primitive = new PrimitiveState
            {
                Topology = PrimitiveTopology.TriangleList,
                FrontFace = FrontFace.Ccw,
                CullMode = CullMode.None
            },
            DepthStencil = &depthStencilState,
            Multisample = new MultisampleState { Count = 1, Mask = 0xFFFFFFFF },
            Fragment = &fragmentState
        };

        _meshPipeline = WebGpuApi.Wgpu.DeviceCreateRenderPipeline(_device, in meshPipelineDesc);

        // This pass writes only the selected mesh's depth.  It is used just
        // before the outline so the expanded back-face shell is hidden inside
        // the selected object, while still being able to overlay other scene
        // objects.
        var selectionDepthStencilState = depthStencilState;
        selectionDepthStencilState.DepthCompare = CompareFunction.Always;
        var selectionDepthColorTarget = colorTarget;
        selectionDepthColorTarget.WriteMask = ColorWriteMask.None;
        var selectionDepthFragmentState = fragmentState;
        selectionDepthFragmentState.Targets = &selectionDepthColorTarget;
        var selectionDepthPipelineDesc = meshPipelineDesc;
        selectionDepthPipelineDesc.DepthStencil = &selectionDepthStencilState;
        selectionDepthPipelineDesc.Fragment = &selectionDepthFragmentState;
        _selectionDepthPipeline = WebGpuApi.Wgpu.DeviceCreateRenderPipeline(_device, in selectionDepthPipelineDesc);

        // WebGPU has no polygon-mode switch. Wireframe rendering therefore
        // uses the same shader and vertex layout with a line-list pipeline;
        // the dedicated index buffers contain the edges of each mesh face.
        var wireframePipelineDesc = meshPipelineDesc;
        wireframePipelineDesc.Primitive = new PrimitiveState
        {
            Topology = PrimitiveTopology.LineList,
            FrontFace = FrontFace.Ccw,
            CullMode = CullMode.None
        };
        var outlineDepthStencilState = depthStencilState;
        outlineDepthStencilState.DepthWriteEnabled = false;
        // The selected object's depth mask is written immediately before this
        // pass, so LessEqual keeps the outline on the silhouette instead of
        // filling the object's faces with the outline color.
        outlineDepthStencilState.DepthCompare = CompareFunction.LessEqual;
        wireframePipelineDesc.DepthStencil = &outlineDepthStencilState;
        _wireframePipeline = WebGpuApi.Wgpu.DeviceCreateRenderPipeline(_device, in wireframePipelineDesc);

        var outlineFragmentState = fragmentState;
        var outlineFsEntryPoint = Marshal.StringToHGlobalAnsi("fs_outline");
        outlineFragmentState.EntryPoint = (byte*)outlineFsEntryPoint.ToPointer();
        var outlineVsEntryPoint = Marshal.StringToHGlobalAnsi("vs_outline");
        var outlineVertexState = new VertexState
        {
            Module = _meshShaderModule,
            EntryPoint = (byte*)outlineVsEntryPoint.ToPointer(),
            BufferCount = 1,
            Buffers = &vertexBufferLayout
        };
        var outlinePipelineDesc = meshPipelineDesc;
        outlinePipelineDesc.Vertex = outlineVertexState;
        outlinePipelineDesc.Primitive = new PrimitiveState
        {
            Topology = PrimitiveTopology.TriangleList,
            FrontFace = FrontFace.Ccw,
            CullMode = CullMode.Front
        };
        outlinePipelineDesc.DepthStencil = &outlineDepthStencilState;
        outlinePipelineDesc.Fragment = &outlineFragmentState;
        _outlinePipeline = WebGpuApi.Wgpu.DeviceCreateRenderPipeline(_device, in outlinePipelineDesc);


        // BindGroupLayout for Grid
        var gridLayoutEntry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform }
        };
        var gridLayoutDesc = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &gridLayoutEntry };
        _gridBindGroupLayout = WebGpuApi.Wgpu.DeviceCreateBindGroupLayout(_device, in gridLayoutDesc);

        var gridBindGroupEntry = new BindGroupEntry
        {
            Binding = 0,
            Buffer = _gridUniformBuffer,
            Size = (ulong)sizeof(GridUniforms)
        };
        var gridBindGroupDesc = new BindGroupDescriptor
        {
            Layout = _gridBindGroupLayout,
            EntryCount = 1,
            Entries = &gridBindGroupEntry
        };
        _gridBindGroup = WebGpuApi.Wgpu.DeviceCreateBindGroup(_device, in gridBindGroupDesc);

        var gridLayoutLocal = _gridBindGroupLayout;
        var gridPipelineLayoutDesc = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &gridLayoutLocal };
        var gridPipelineLayout = WebGpuApi.Wgpu.DeviceCreatePipelineLayout(_device, in gridPipelineLayoutDesc);

        var gridAttrib = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        var gridBufferLayout = new VertexBufferLayout
        {
            ArrayStride = 3 * sizeof(float),
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 1,
            Attributes = &gridAttrib
        };

        var blendState = new BlendState
        {
            Color = new BlendComponent { SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add }
        };

        var gridColorTarget = new ColorTargetState
        {
            Format = _swapChainFormat,
            Blend = &blendState,
            WriteMask = ColorWriteMask.All
        };

        var gridDepthStencilState = new DepthStencilState
        {
            Format = TextureFormat.Depth24Plus,
            // The grid is rendered after the meshes. It can test mesh depth,
            // but must not replace it or write depth between grid lines.
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.LessEqual,
            StencilFront = new StencilFaceState { Compare = CompareFunction.Always },
            StencilBack = new StencilFaceState { Compare = CompareFunction.Always }
        };

        var gridFragmentState = new FragmentState
        {
            Module = _gridShaderModule,
            EntryPoint = (byte*)fsEntryPoint.ToPointer(),
            TargetCount = 1,
            Targets = &gridColorTarget
        };

        var gridPipelineDesc = new RenderPipelineDescriptor
        {
            Layout = gridPipelineLayout,
            Vertex = new VertexState
            {
                Module = _gridShaderModule,
                EntryPoint = (byte*)vsEntryPoint.ToPointer(),
                BufferCount = 1,
                Buffers = &gridBufferLayout
            },
            Primitive = new PrimitiveState
            {
                Topology = PrimitiveTopology.TriangleList,
                FrontFace = FrontFace.Ccw,
                CullMode = CullMode.None
            },
            DepthStencil = &gridDepthStencilState,
            Multisample = new MultisampleState { Count = 1, Mask = 0xFFFFFFFF },
            Fragment = &gridFragmentState
        };

        _gridPipeline = WebGpuApi.Wgpu.DeviceCreateRenderPipeline(_device, in gridPipelineDesc);
    }

    private ShaderModule* CreateShaderModule(string wgslCode)
    {
        var codePtr = Marshal.StringToHGlobalAnsi(wgslCode);
        var wgslDescriptor = new ShaderModuleWGSLDescriptor
        {
            Code = (byte*)codePtr.ToPointer()
        };
        wgslDescriptor.Chain.SType = SType.ShaderModuleWgslDescriptor;

        var descriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgslDescriptor };
        var module = WebGpuApi.Wgpu.DeviceCreateShaderModule(_device!, in descriptor);
        Marshal.FreeHGlobal(codePtr);
        return module;
    }

    private void StartRenderingLoop()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1), DispatcherPriority.Input, OnTimerTick);
        _timer.Start();
    }

    private void StopRenderingLoop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_surface == null || _device == null) return;
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_surface == null || _device == null || _depthTextureView == null) return;

        // 1. Timing & FPS
        double currentTime = _stopwatch.Elapsed.TotalSeconds;
        double deltaTime = currentTime - _lastFrameTime;
        _lastFrameTime = currentTime;

        UpdateKeyboardMovement((float)Math.Clamp(deltaTime, 0.0, 0.1));

        _frameCount++;
        _fpsTimer += deltaTime;
        if (_fpsTimer >= 0.5)
        {
            Fps = (int)(_frameCount / _fpsTimer);
            FrameTimeMs = (float)((_fpsTimer / _frameCount) * 1000.0);
            _frameCount = 0;
            _fpsTimer = 0;
        }

        // 2. Matrices & Camera Setup
        float yawRad = MathF.PI / 180f * (Scene?.CameraYaw ?? 45f);
        float pitchRad = MathF.PI / 180f * (Scene?.CameraPitch ?? 30f);
        float dist = Scene?.CameraDistance ?? 6.0f;
        Vector3 target = new Vector3(Scene?.CameraTargetX ?? 0f, Scene?.CameraTargetY ?? 0f, Scene?.CameraTargetZ ?? 0f);

        Vector3 eye = target + new Vector3(
            dist * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            dist * MathF.Sin(pitchRad),
            dist * MathF.Cos(pitchRad) * MathF.Cos(yawRad)
        );

        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)_width / _height, 0.1f, 100f);

        Matrix4x4.Invert(view, out Matrix4x4 viewInv);
        Matrix4x4.Invert(proj, out Matrix4x4 projInv);

        // 3. Acquire Surface Texture
        SurfaceTexture surfaceTexture;
        WebGpuApi.Wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success || surfaceTexture.Texture == null) return;

        TextureView* targetView = WebGpuApi.Wgpu.TextureCreateView(surfaceTexture.Texture, null);
        CommandEncoder* encoder = WebGpuApi.Wgpu.DeviceCreateCommandEncoder(_device, null);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = targetView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.12, G = 0.12, B = 0.14, A = 1.0 }
        };

        var depthAttachment = new RenderPassDepthStencilAttachment
        {
            View = _depthTextureView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f
        };

        var renderPassDesc = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
            DepthStencilAttachment = &depthAttachment
        };

        RenderPassEncoder* pass = WebGpuApi.Wgpu.CommandEncoderBeginRenderPass(encoder, in renderPassDesc);

        // Prepare the grid uniforms. The grid itself is drawn after the scene
        // so mesh depth is already available to its depth test.
        GridUniforms gridUniforms = new GridUniforms
        {
            // System.Numerics uses row vectors. WGSL interprets the same
            // memory as column-major matrices, which is exactly the
            // transpose needed for `matrix * vector` in the shader.
            View = view,
            Proj = proj,
            ViewInv = viewInv,
            ProjInv = projInv
        };
        WebGpuApi.Wgpu.QueueWriteBuffer(_queue, _gridUniformBuffer, 0, &gridUniforms, (nuint)sizeof(GridUniforms));

        // A. Draw Scene Objects
        bool wireframe = Scene?.IsWireframe == true;
        WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(pass, wireframe ? _wireframePipeline : _meshPipeline);

        Vector3 lightDir = Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.7f));

        if (Scene != null)
        {
            CleanupMeshResources();

            // Keep the selected object's transform and GPU resources for a
            // second pass.  Drawing the outline inside this loop lets a later
            // object overwrite it (notably the green pyramid in the editor).
            SceneObject? outlineObject = null;
            Matrix4x4 outlineModel = default;
            MeshGpuResources? outlineResources = null;

            foreach (var obj in Scene.Objects)
            {
                if (!obj.IsVisible) continue;

                if (!Scene.IsPaused)
                {
                    obj.RotationY = (obj.RotationY + 45f * (float)deltaTime) % 360f;
                }

                Matrix4x4 scale = Matrix4x4.CreateScale(obj.ScaleX, obj.ScaleY, obj.ScaleZ);
                Matrix4x4 rotX = Matrix4x4.CreateRotationX(MathF.PI / 180f * obj.RotationX);
                Matrix4x4 rotY = Matrix4x4.CreateRotationY(MathF.PI / 180f * obj.RotationY);
                Matrix4x4 rotZ = Matrix4x4.CreateRotationZ(MathF.PI / 180f * obj.RotationZ);
                Matrix4x4 trans = Matrix4x4.CreateTranslation(obj.PositionX, obj.PositionY, obj.PositionZ);

                Matrix4x4 model = scale * rotX * rotY * rotZ * trans;

                MeshUniforms uniforms = new MeshUniforms
                {
                    Model = model,
                    View = view,
                    Proj = proj,
                    Color = new Vector4(obj.ColorR, obj.ColorG, obj.ColorB, obj.ColorA),
                    LightDir = lightDir,
                    IsSelected = wireframe && obj.IsSelected ? 1u : 0u
                };

                MeshGpuResources meshResources = GetMeshResources(obj);
                WebGpuApi.Wgpu.QueueWriteBuffer(_queue, meshResources.UniformBuffer, 0, &uniforms, (nuint)sizeof(MeshUniforms));
                WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(pass, 0, meshResources.BindGroup, 0, null);

                if (obj.IsSelected && !wireframe)
                {
                    outlineObject = obj;
                    outlineModel = model;
                    outlineResources = meshResources;
                }

                if (obj.MeshType == "Pyramid")
                {
                    WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _pyramidVbo, 0, (ulong)(16 * 6 * sizeof(float)));
                    Buffer* indexBuffer = wireframe ? _pyramidWireframeEbo : _pyramidEbo;
                    uint indexCount = wireframe ? 30u : 18u;
                    WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, indexBuffer, IndexFormat.Uint16, 0, (ulong)(indexCount * sizeof(ushort)));
                    WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, indexCount, 1, 0, 0, 0);
                }
                else
                {
                    WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVbo, 0, (ulong)(24 * 6 * sizeof(float)));
                    Buffer* indexBuffer = wireframe ? _cubeWireframeEbo : _cubeEbo;
                    uint indexCount = wireframe ? 48u : 36u;
                    WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, indexBuffer, IndexFormat.Uint16, 0, (ulong)(indexCount * sizeof(ushort)));
                    WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, indexCount, 1, 0, 0, 0);
                }

            }

            // Draw selection after every scene object so later meshes cannot
            // cover the highlight.  The outline pipeline also uses an Always
            // depth comparison because the highlight is intentionally an
            // overlay rather than another occludable mesh.
            if (outlineObject != null && outlineResources != null)
            {
                MeshUniforms outlineUniforms = new MeshUniforms
                {
                    Model = outlineModel,
                    View = view,
                    Proj = proj,
                    Color = new Vector4(outlineObject.ColorR, outlineObject.ColorG, outlineObject.ColorB, outlineObject.ColorA),
                    LightDir = lightDir,
                    IsSelected = 0u
                };
                WebGpuApi.Wgpu.QueueWriteBuffer(_queue, outlineResources.UniformBuffer, 0, &outlineUniforms, (nuint)sizeof(MeshUniforms));

                WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(pass, _selectionDepthPipeline);
                WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(pass, 0, outlineResources.BindGroup, 0, null);
                if (outlineObject.MeshType == "Pyramid")
                {
                    WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _pyramidVbo, 0, (ulong)(16 * 6 * sizeof(float)));
                    WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, _pyramidEbo, IndexFormat.Uint16, 0, (ulong)(18 * sizeof(ushort)));
                    WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, 18, 1, 0, 0, 0);
                }
                else
                {
                    WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVbo, 0, (ulong)(24 * 6 * sizeof(float)));
                    WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeEbo, IndexFormat.Uint16, 0, (ulong)(36 * sizeof(ushort)));
                    WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, 36, 1, 0, 0, 0);
                }

                WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(pass, _outlinePipeline);
                WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(pass, 0, outlineResources.BindGroup, 0, null);

                if (outlineObject.MeshType == "Pyramid")
                {
                    WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _pyramidVbo, 0, (ulong)(16 * 6 * sizeof(float)));
                    WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, _pyramidEbo, IndexFormat.Uint16, 0, (ulong)(18 * sizeof(ushort)));
                    WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, 18, 1, 0, 0, 0);
                }
                else
                {
                    WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _cubeVbo, 0, (ulong)(24 * 6 * sizeof(float)));
                    WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, _cubeEbo, IndexFormat.Uint16, 0, (ulong)(36 * sizeof(ushort)));
                    WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, 36, 1, 0, 0, 0);
                }
            }
        }

        // B. Draw Infinite Grid. It uses depth testing without writing depth,
        // and the shader discards the space between lines.
        WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(pass, _gridPipeline);
        WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(pass, 0, _gridBindGroup, 0, null);
        WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _gridVbo, 0, (ulong)(6 * 3 * sizeof(float)));
        WebGpuApi.Wgpu.RenderPassEncoderDraw(pass, 6, 1, 0, 0);

        WebGpuApi.Wgpu.RenderPassEncoderEnd(pass);

        CommandBuffer* cmdBuffer = WebGpuApi.Wgpu.CommandEncoderFinish(encoder, null);
        WebGpuApi.Wgpu.QueueSubmit(_queue, 1, &cmdBuffer);
        WebGpuApi.Wgpu.SurfacePresent(_surface);

        WebGpuApi.Wgpu.TextureViewRelease(targetView);
        WebGpuApi.Wgpu.CommandBufferRelease(cmdBuffer);
        WebGpuApi.Wgpu.CommandEncoderRelease(encoder);
    }

    private void HandlePointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed || point.Properties.IsLeftButtonPressed)
        {
            _isOrbiting = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void HandlePointerMoved(PointerEventArgs e)
    {
        if (_isOrbiting || _isPanning)
        {
            var point = e.GetCurrentPoint(this);
            Point delta = point.Position - _lastMousePos;
            _lastMousePos = point.Position;

            if (Scene != null)
            {
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
            }

            e.Handled = true;
        }
    }

    private void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        if (_isOrbiting || _isPanning)
        {
            _isOrbiting = false;
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void HandlePointerWheelChanged(PointerWheelEventArgs e)
    {
        if (Scene == null) return;
        float zoomDelta = (float)e.Delta.Y * 0.5f;
        Scene.CameraDistance = Math.Clamp(Scene.CameraDistance - zoomDelta, 1.0f, 50.0f);
        e.Handled = true;
    }

    private void UpdateKeyboardMovement(float deltaTime)
    {
        if (Scene == null || deltaTime <= 0f) return;

        // The WebGPU viewport is hosted in a native child HWND. Polling the
        // keys while that HWND owns focus avoids depending on Avalonia's
        // routed keyboard events crossing the native-control boundary.
        if (_hwnd != nint.Zero && GetFocus() == _hwnd)
        {
            SetMovementKey(VK_W, IsKeyDown(VK_W));
            SetMovementKey(VK_A, IsKeyDown(VK_A));
            SetMovementKey(VK_S, IsKeyDown(VK_S));
            SetMovementKey(VK_D, IsKeyDown(VK_D));
        }
        else
        {
            ClearMovementKeys();
        }

        float forwardInput = (_moveForward ? 1f : 0f) - (_moveBackward ? 1f : 0f);
        float strafeInput = (_moveRight ? 1f : 0f) - (_moveLeft ? 1f : 0f);
        if (forwardInput == 0f && strafeInput == 0f) return;

        float yawRad = MathF.PI / 180f * Scene.CameraYaw;
        float pitchRad = MathF.PI / 180f * Scene.CameraPitch;
        float distance = MathF.Max(Scene.CameraDistance, 0.001f);
        Vector3 cameraOffset = new(
            distance * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
            distance * MathF.Sin(pitchRad),
            distance * MathF.Cos(pitchRad) * MathF.Cos(yawRad));

        // The camera looks from eye (target + offset) toward target. Move
        // along that exact view vector, including its vertical component.
        Vector3 forward = Vector3.Normalize(-cameraOffset);
        Vector3 right = new(MathF.Cos(yawRad), 0f, -MathF.Sin(yawRad));
        Vector3 direction = forward * forwardInput + right * strafeInput;

        // Keep diagonal movement at the same speed as axial movement.
        if (direction.LengthSquared() > 1f)
        {
            direction = Vector3.Normalize(direction);
        }

        Vector3 offset = direction * CameraMoveSpeed * deltaTime;
        Scene.CameraTargetX += offset.X;
        Scene.CameraTargetY += offset.Y;
        Scene.CameraTargetZ += offset.Z;
    }

    private nint NativeWindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_KEYDOWN:
                SetMovementKey((int)wParam, true);
                if (IsMovementKey((int)wParam)) return nint.Zero;
                break;

            case WM_KEYUP:
                SetMovementKey((int)wParam, false);
                if (IsMovementKey((int)wParam)) return nint.Zero;
                break;

            case WM_KILLFOCUS:
                ClearMovementKeys();
                break;

            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
                BeginNativeInteraction(orbit: true, GetMouseX(lParam), GetMouseY(lParam));
                return nint.Zero;

            case WM_MBUTTONDOWN:
                BeginNativeInteraction(orbit: false, GetMouseX(lParam), GetMouseY(lParam));
                return nint.Zero;

            case WM_MOUSEMOVE:
                if (_isOrbiting || _isPanning)
                {
                    UpdateCamera(GetMouseX(lParam), GetMouseY(lParam));
                    return nint.Zero;
                }
                break;

            case WM_LBUTTONUP:
            case WM_RBUTTONUP:
            case WM_MBUTTONUP:
                if (_isOrbiting || _isPanning)
                {
                    EndNativeInteraction();
                    return nint.Zero;
                }
                break;

            case WM_MOUSEWHEEL:
                ApplyZoom((short)((long)wParam >> 16));
                return nint.Zero;
        }

        return CallWindowProc(_previousWndProc, hWnd, message, wParam, lParam);
    }

    private void SetMovementKey(int key, bool isDown)
    {
        switch (key)
        {
            case VK_W: _moveForward = isDown; break;
            case VK_S: _moveBackward = isDown; break;
            case VK_A: _moveLeft = isDown; break;
            case VK_D: _moveRight = isDown; break;
        }
    }

    private static bool IsMovementKey(int key) =>
        key is VK_W or VK_S or VK_A or VK_D;

    private void ClearMovementKeys()
    {
        _moveForward = false;
        _moveBackward = false;
        _moveLeft = false;
        _moveRight = false;
    }

    private void BeginNativeInteraction(bool orbit, int x, int y)
    {
        Focus();
        SetFocus(_hwnd);
        _isOrbiting = orbit;
        _isPanning = !orbit;
        _lastMousePos = new Point(x, y);
        SetCapture(_hwnd);
    }

    private void UpdateCamera(int x, int y)
    {
        Point current = new(x, y);
        Point delta = current - _lastMousePos;
        _lastMousePos = current;

        if (Scene == null) return;

        if (_isOrbiting)
        {
            Scene.CameraYaw -= (float)delta.X * 0.4f;
            Scene.CameraPitch = Math.Clamp(Scene.CameraPitch + (float)delta.Y * 0.4f, -89f, 89f);
        }
        else if (_isPanning)
        {
            float sensitivity = Scene.CameraDistance * 0.002f;
            float yawRad = MathF.PI / 180f * Scene.CameraYaw;
            Vector3 right = new(MathF.Cos(yawRad), 0, -MathF.Sin(yawRad));
            Vector3 panOffset = (right * (float)-delta.X + Vector3.UnitY * (float)delta.Y) * sensitivity;
            Scene.CameraTargetX += panOffset.X;
            Scene.CameraTargetY += panOffset.Y;
            Scene.CameraTargetZ += panOffset.Z;
        }
    }

    private void EndNativeInteraction()
    {
        _isOrbiting = false;
        _isPanning = false;
        ReleaseCapture();
    }

    private void ApplyZoom(float wheelDelta)
    {
        if (Scene != null)
        {
            Scene.CameraDistance = Math.Clamp(Scene.CameraDistance - wheelDelta / 120f * 0.5f, 1.0f, 50.0f);
        }
    }

    private static int GetMouseX(nint lParam) => (short)((long)lParam & 0xFFFF);
    private static int GetMouseY(nint lParam) => (short)(((long)lParam >> 16) & 0xFFFF);
    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void CleanupWebGpu()
    {
        if (_depthTextureView != null) WebGpuApi.Wgpu.TextureViewRelease(_depthTextureView);
        if (_depthTexture != null) { WebGpuApi.Wgpu.TextureDestroy(_depthTexture); WebGpuApi.Wgpu.TextureRelease(_depthTexture); }

        if (_meshPipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_meshPipeline);
        if (_wireframePipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_wireframePipeline);
        if (_selectionDepthPipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_selectionDepthPipeline);
        if (_outlinePipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_outlinePipeline);
        if (_gridPipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_gridPipeline);

        if (_meshShaderModule != null) WebGpuApi.Wgpu.ShaderModuleRelease(_meshShaderModule);
        if (_gridShaderModule != null) WebGpuApi.Wgpu.ShaderModuleRelease(_gridShaderModule);

        if (_gridBindGroup != null) WebGpuApi.Wgpu.BindGroupRelease(_gridBindGroup);

        if (_meshBindGroupLayout != null) WebGpuApi.Wgpu.BindGroupLayoutRelease(_meshBindGroupLayout);
        if (_gridBindGroupLayout != null) WebGpuApi.Wgpu.BindGroupLayoutRelease(_gridBindGroupLayout);

        foreach (MeshGpuResources resources in _meshResources.Values)
        {
            ReleaseMeshResources(resources);
        }
        _meshResources.Clear();
        if (_gridUniformBuffer != null) { WebGpuApi.Wgpu.BufferDestroy(_gridUniformBuffer); WebGpuApi.Wgpu.BufferRelease(_gridUniformBuffer); }

        if (_cubeVbo != null) { WebGpuApi.Wgpu.BufferDestroy(_cubeVbo); WebGpuApi.Wgpu.BufferRelease(_cubeVbo); }
        if (_cubeEbo != null) { WebGpuApi.Wgpu.BufferDestroy(_cubeEbo); WebGpuApi.Wgpu.BufferRelease(_cubeEbo); }
        if (_cubeWireframeEbo != null) { WebGpuApi.Wgpu.BufferDestroy(_cubeWireframeEbo); WebGpuApi.Wgpu.BufferRelease(_cubeWireframeEbo); }
        if (_pyramidVbo != null) { WebGpuApi.Wgpu.BufferDestroy(_pyramidVbo); WebGpuApi.Wgpu.BufferRelease(_pyramidVbo); }
        if (_pyramidEbo != null) { WebGpuApi.Wgpu.BufferDestroy(_pyramidEbo); WebGpuApi.Wgpu.BufferRelease(_pyramidEbo); }
        if (_pyramidWireframeEbo != null) { WebGpuApi.Wgpu.BufferDestroy(_pyramidWireframeEbo); WebGpuApi.Wgpu.BufferRelease(_pyramidWireframeEbo); }
        if (_gridVbo != null) { WebGpuApi.Wgpu.BufferDestroy(_gridVbo); WebGpuApi.Wgpu.BufferRelease(_gridVbo); }

        if (_surface != null) WebGpuApi.Wgpu.SurfaceRelease(_surface);

        _device?.Dispose();
        _adapter?.Dispose();
        WebGpuApi.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private delegate nint NativeWndProc(nint hWnd, uint message, nint wParam, nint lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_KILLFOCUS = 0x0008;
    private const int VK_W = 0x57;
    private const int VK_A = 0x41;
    private const int VK_S = 0x53;
    private const int VK_D = 0x44;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newValue);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallWindowProc(nint previousWndProc, nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
