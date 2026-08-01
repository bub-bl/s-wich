using System;
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

namespace MyApp;

public unsafe class SilkViewportControl : NativeControlHost
{
    private nint _hwnd;
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
    private RenderPipeline* _gridPipeline;

    private Buffer* _cubeVbo;
    private Buffer* _cubeEbo;
    private Buffer* _pyramidVbo;
    private Buffer* _pyramidEbo;
    private Buffer* _gridVbo;

    private Buffer* _meshUniformBuffer;
    private Buffer* _gridUniformBuffer;

    private BindGroup* _meshBindGroup;
    private BindGroup* _gridBindGroup;

    private BindGroupLayout* _meshBindGroupLayout;
    private BindGroupLayout* _gridBindGroupLayout;

    private int _width = 800;
    private int _height = 600;

    private Point _lastMousePos;
    private bool _isOrbiting;
    private bool _isPanning;

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

            _width = Math.Max(1, (int)Bounds.Width);
            _height = Math.Max(1, (int)Bounds.Height);

            _hwnd = CreateWindowExW(
                0, "static", "SilkWebGpuHost",
                WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, _width, _height,
                parent.Handle, nint.Zero, nint.Zero, nint.Zero);

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
            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

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
        _meshUniformBuffer = CreateEmptyBuffer((ulong)sizeof(MeshUniforms), BufferUsage.Uniform | BufferUsage.CopyDst);
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

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let norm = normalize(in.normal);
    let light = normalize(u.lightDir);
    let diff = max(dot(norm, light), 0.25);
    var base_color = u.color.rgb * diff;
    if (u.isSelected != 0u) {
        base_color += vec3<f32>(0.3, 0.3, 0.0);
    }
    return vec4<f32>(base_color, u.color.a);
}
";

        // 2. Grid WGSL Shader Code
        const string gridWgsl = @"
struct GridUniforms {
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> gu: GridUniforms;

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) near_point: vec3<f32>,
    @location(1) far_point: vec3<f32>,
};

@vertex
fn vs_main(@location(0) position: vec3<f32>) -> VertexOutput {
    var out: VertexOutput;
    out.clip_position = vec4<f32>(position, 1.0);
    out.near_point = vec3<f32>(position.x * 20.0, 0.0, position.y * 20.0);
    out.far_point = vec3<f32>(position.x * 20.0, 0.0, position.y * 20.0);
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let coord = in.near_point.xz;
    let grid = abs(fract(coord - 0.5) - 0.5);
    let line = min(grid.x, grid.y);
    var color = vec4<f32>(0.35, 0.35, 0.38, 1.0 - min(line * 10.0, 1.0));
    
    if (abs(coord.x) < 0.1) {
        color = vec4<f32>(0.2, 0.4, 0.9, 0.9); // Z axis
    }
    if (abs(coord.y) < 0.1) {
        color = vec4<f32>(0.9, 0.2, 0.2, 0.9); // X axis
    }

    let dist = length(coord);
    let fading = max(0.0, 1.0 - dist / 30.0);
    return color * fading;
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

        var meshBindGroupEntry = new BindGroupEntry
        {
            Binding = 0,
            Buffer = _meshUniformBuffer,
            Size = (ulong)sizeof(MeshUniforms)
        };
        var meshBindGroupDesc = new BindGroupDescriptor
        {
            Layout = _meshBindGroupLayout,
            EntryCount = 1,
            Entries = &meshBindGroupEntry
        };
        _meshBindGroup = WebGpuApi.Wgpu.DeviceCreateBindGroup(_device, in meshBindGroupDesc);

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
            DepthStencil = &depthStencilState,
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

        // A. Draw Infinite Grid
        GridUniforms gridUniforms = new GridUniforms { View = view, Proj = proj };
        WebGpuApi.Wgpu.QueueWriteBuffer(_queue, _gridUniformBuffer, 0, &gridUniforms, (nuint)sizeof(GridUniforms));

        WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(pass, _gridPipeline);
        WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(pass, 0, _gridBindGroup, 0, null);
        WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _gridVbo, 0, (ulong)(6 * 3 * sizeof(float)));
        WebGpuApi.Wgpu.RenderPassEncoderDraw(pass, 6, 1, 0, 0);

        // B. Draw Scene Objects
        WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(pass, _meshPipeline);

        Vector3 lightDir = Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.7f));

        if (Scene != null)
        {
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
                    IsSelected = obj.IsSelected ? 1u : 0u
                };

                WebGpuApi.Wgpu.QueueWriteBuffer(_queue, _meshUniformBuffer, 0, &uniforms, (nuint)sizeof(MeshUniforms));
                WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(pass, 0, _meshBindGroup, 0, null);

                if (obj.MeshType == "Pyramid")
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
                    Scene.CameraYaw += (float)delta.X * 0.4f;
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

    private void CleanupWebGpu()
    {
        if (_depthTextureView != null) WebGpuApi.Wgpu.TextureViewRelease(_depthTextureView);
        if (_depthTexture != null) { WebGpuApi.Wgpu.TextureDestroy(_depthTexture); WebGpuApi.Wgpu.TextureRelease(_depthTexture); }

        if (_meshPipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_meshPipeline);
        if (_gridPipeline != null) WebGpuApi.Wgpu.RenderPipelineRelease(_gridPipeline);

        if (_meshShaderModule != null) WebGpuApi.Wgpu.ShaderModuleRelease(_meshShaderModule);
        if (_gridShaderModule != null) WebGpuApi.Wgpu.ShaderModuleRelease(_gridShaderModule);

        if (_meshBindGroup != null) WebGpuApi.Wgpu.BindGroupRelease(_meshBindGroup);
        if (_gridBindGroup != null) WebGpuApi.Wgpu.BindGroupRelease(_gridBindGroup);

        if (_meshBindGroupLayout != null) WebGpuApi.Wgpu.BindGroupLayoutRelease(_meshBindGroupLayout);
        if (_gridBindGroupLayout != null) WebGpuApi.Wgpu.BindGroupLayoutRelease(_gridBindGroupLayout);

        if (_meshUniformBuffer != null) { WebGpuApi.Wgpu.BufferDestroy(_meshUniformBuffer); WebGpuApi.Wgpu.BufferRelease(_meshUniformBuffer); }
        if (_gridUniformBuffer != null) { WebGpuApi.Wgpu.BufferDestroy(_gridUniformBuffer); WebGpuApi.Wgpu.BufferRelease(_gridUniformBuffer); }

        if (_cubeVbo != null) { WebGpuApi.Wgpu.BufferDestroy(_cubeVbo); WebGpuApi.Wgpu.BufferRelease(_cubeVbo); }
        if (_cubeEbo != null) { WebGpuApi.Wgpu.BufferDestroy(_cubeEbo); WebGpuApi.Wgpu.BufferRelease(_cubeEbo); }
        if (_pyramidVbo != null) { WebGpuApi.Wgpu.BufferDestroy(_pyramidVbo); WebGpuApi.Wgpu.BufferRelease(_pyramidVbo); }
        if (_pyramidEbo != null) { WebGpuApi.Wgpu.BufferDestroy(_pyramidEbo); WebGpuApi.Wgpu.BufferRelease(_pyramidEbo); }
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
}
