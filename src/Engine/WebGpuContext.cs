using Crowbar.Engine.Rendering;
using Silk.NET.WebGPU;
using System.Runtime.InteropServices;
using System.Numerics;

namespace Crowbar.Engine;

/// <summary>
/// Owns the first usable WebGPU device for the runtime.
/// Owns the window surface and keeps its configuration synchronized with the framebuffer size.
/// </summary>
public sealed unsafe class WebGpuContext : IDisposable
{
    private struct CameraUniforms
    {
        public Matrix4x4 View;
        public Matrix4x4 Projection;
    }

    public WebGpuRuntime Runtime { get; }
    public WebGpuAdapter Adapter { get; }
    public WebGpuDevice Device { get; }
    public WebGpuQueue Queue { get; }

    private Surface* _surface;
    private ShaderModule* _cubeShader;
    private RenderPipeline* _cubePipeline;
    private Silk.NET.WebGPU.Buffer* _cubeVertexBuffer;
    private Silk.NET.WebGPU.Buffer* _cameraUniformBuffer;
    private BindGroupLayout* _cameraBindGroupLayout;
    private BindGroup* _cameraBindGroup;
    private PipelineLayout* _cameraPipelineLayout;
    private Texture* _depthTexture;
    private TextureView* _depthTextureView;
    private const uint CubeVertexCount = 36;
    private TextureFormat _surfaceFormat;
    private readonly nint _windowHandle;
    private int _width;
    private int _height;
    private Vector3 _cameraPosition = new(4.24f, 3f, 4.24f);
    private float _cameraYaw = -MathF.PI / 4f;
    private float _cameraPitch = -0.42f;
    private bool _mouseLookActive;
    private int _mouseCenterX;
    private int _mouseCenterY;
    private bool _hasPresentedFrame;
    private bool _disposed;

    public WebGpuContext(nint windowHandle, int width, int height)
    {
        Runtime = new WebGpuRuntime();
        try
        {
            if (windowHandle == 0)
                throw new ArgumentException("The window does not expose a native handle.", nameof(windowHandle));
            _windowHandle = windowHandle;
            _width = Math.Max(1, width);
            _height = Math.Max(1, height);

            var hwndDescriptor = new SurfaceDescriptorFromWindowsHWND
            {
                Chain = new ChainedStruct { SType = SType.SurfaceDescriptorFromWindowsHwnd },
                Hwnd = (void*)windowHandle,
                Hinstance = (void*)System.Runtime.InteropServices.Marshal.GetHINSTANCE(
                    typeof(WebGpuContext).Module)
            };
            var surfaceDescriptor = new SurfaceDescriptor
            {
                NextInChain = (ChainedStruct*)&hwndDescriptor
            };
            _surface = Runtime.Api.InstanceCreateSurface(Runtime.Instance.UnsafeHandle, in surfaceDescriptor);
            if (_surface == null)
                throw new InvalidOperationException("WebGPU could not create a window surface.");

            Adapter = new WebGpuAdapter(Runtime, WebGpuSurface.FromNative((nint)_surface));
            Device = Adapter.CreateDevice();
            Queue = Device.GetQueue();
            Runtime.ConfigureDebugCallback(Device);
            _surfaceFormat = Runtime.Api.SurfaceGetPreferredFormat(_surface, Adapter.UnsafeHandle);
            if (_surfaceFormat == TextureFormat.Undefined)
                _surfaceFormat = TextureFormat.Bgra8Unorm;

            ConfigureSurface(width, height);
            CreateCameraResources();
            CreateCubeResources();
            UpdateCamera(0);
            Console.WriteLine("WebGPU device initialized.");
        }
        catch
        {
            Runtime.Dispose();
            throw;
        }
    }

    public void Render(double _)
    {
        if (_disposed || _surface == null)
            return;

        SurfaceTexture surfaceTexture = default;
        Runtime.Api.SurfaceGetCurrentTexture(_surface, ref surfaceTexture);
        if (surfaceTexture.Texture == null)
            return;

        TextureView* view = Runtime.Api.TextureCreateView(surfaceTexture.Texture, null);
        if (view == null)
            return;

        WebGpuCommandEncoder encoder = Runtime.CreateCommandEncoder(Device);
        var passDescription = new RenderPassDescription
        {
            Color = new ColorAttachment
            {
                View = WebGpuTextureView.FromNative((nint)view),
                LoadOp = RenderAttachmentLoadOp.Clear,
                StoreOp = RenderAttachmentStoreOp.Store,
                ClearColor = new System.Numerics.Vector4(0.06f, 0.09f, 0.16f, 1f)
            },
            Depth = new DepthAttachment
            {
                View = WebGpuTextureView.FromNative((nint)_depthTextureView),
                LoadOp = RenderAttachmentLoadOp.Clear,
                StoreOp = RenderAttachmentStoreOp.Store,
                ClearValue = 1f
            }
        };

        WebGpuRenderPassEncoder pass = Runtime.BeginRenderPass(encoder, passDescription);
        Runtime.SetPipeline(pass, WebGpuRenderPipeline.FromNative((nint)_cubePipeline));
        Runtime.SetBindGroup(pass, WebGpuBindGroup.FromNative((nint)_cameraBindGroup), 0);
        Runtime.SetVertexBuffer(pass, WebGpuBuffer.FromNative((nint)_cubeVertexBuffer),
            (ulong)(CubeVertexCount * 6 * sizeof(float)));
        Runtime.Draw(pass, CubeVertexCount);
        Runtime.EndRenderPass(pass);
        WebGpuCommandBuffer commandBuffer = Runtime.FinishCommandEncoder(encoder);
        Runtime.Submit(Queue, commandBuffer);
        Runtime.ReleaseCommandBuffer(commandBuffer);
        Runtime.ReleaseCommandEncoder(encoder);
        Runtime.Api.TextureViewRelease(view);
        Runtime.Api.SurfacePresent(_surface);

        if (!_hasPresentedFrame)
        {
            _hasPresentedFrame = true;
            Console.WriteLine("WebGPU first frame presented.");
        }
    }

    public void Update(double deltaTime)
    {
        if (_disposed)
            return;

        // GetAsyncKeyState is process-independent, so explicitly reject input
        // while another window is in the foreground.
        if (!IsWindowFocused())
        {
            _mouseLookActive = false;
            return;
        }

        float delta = Math.Clamp((float)deltaTime, 0f, 0.1f);
        UpdateMouseLook();

        Vector3 forward = new(
            MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch),
            MathF.Sin(_cameraPitch),
            -MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch));
        Vector3 right = new(MathF.Cos(_cameraYaw), 0f, MathF.Sin(_cameraYaw));
        Vector3 movement = Vector3.Zero;
        if (IsKeyDown(0x5A)) movement += forward; // Z
        if (IsKeyDown(0x53)) movement -= forward; // S
        if (IsKeyDown(0x44)) movement += right;   // D
        if (IsKeyDown(0x51)) movement -= right;   // Q
        if (IsKeyDown(0x20)) movement += Vector3.UnitY; // Espace
        if (IsKeyDown(0x11)) movement -= Vector3.UnitY; // Ctrl

        if (movement.LengthSquared() > 0f)
            _cameraPosition += Vector3.Normalize(movement) * (2.5f * delta);

        UpdateCamera(delta);
    }

    public void Resize(int width, int height)
    {
        if (_disposed || _surface == null || width <= 0 || height <= 0)
            return;

        _width = width;
        _height = height;
        ConfigureSurface(width, height);
        UpdateCamera(0);
    }

    private void ConfigureSurface(int width, int height)
    {
        var configuration = new SurfaceConfiguration
        {
            Device = Device.UnsafeHandle,
            Width = (uint)Math.Max(1, width),
            Height = (uint)Math.Max(1, height),
            Format = _surfaceFormat,
            Usage = TextureUsage.RenderAttachment,
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto
        };
        Runtime.Api.SurfaceConfigure(_surface, in configuration);
        RecreateDepthTexture(width, height);
    }

    private void RecreateDepthTexture(int width, int height)
    {
        if (_depthTextureView != null)
            Runtime.Api.TextureViewRelease(_depthTextureView);
        if (_depthTexture != null)
        {
            Runtime.Api.TextureDestroy(_depthTexture);
            Runtime.Api.TextureRelease(_depthTexture);
        }

        var descriptor = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D
            {
                Width = (uint)Math.Max(1, width),
                Height = (uint)Math.Max(1, height),
                DepthOrArrayLayers = 1
            },
            Format = TextureFormat.Depth24Plus,
            MipLevelCount = 1,
            SampleCount = 1
        };
        _depthTexture = Runtime.Api.DeviceCreateTexture(Device.UnsafeHandle, in descriptor);
        if (_depthTexture == null)
            throw new InvalidOperationException("WebGPU could not create the depth texture.");

        _depthTextureView = Runtime.Api.TextureCreateView(_depthTexture, null);
        if (_depthTextureView == null)
            throw new InvalidOperationException("WebGPU could not create the depth texture view.");
    }

    private void UpdateCamera(double _)
    {
        float aspect = Math.Max(1, _width) / (float)Math.Max(1, _height);
        Vector3 target = _cameraPosition + new Vector3(
            MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch),
            MathF.Sin(_cameraPitch),
            -MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch));
        Matrix4x4 view = Matrix4x4.CreateLookAt(_cameraPosition, target, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 3f, aspect, 0.1f, 100f);
        CameraUniforms uniforms = new()
        {
            View = view,
            Projection = projection
        };
        Runtime.Api.QueueWriteBuffer(
            (Queue*)Queue.NativeHandle,
            _cameraUniformBuffer,
            0,
            in uniforms,
            (nuint)sizeof(CameraUniforms));
    }

    private void UpdateMouseLook()
    {
        if (!IsKeyDown(0x02)) // VK_RBUTTON
        {
            _mouseLookActive = false;
            return;
        }

        if (!_mouseLookActive)
        {
            CenterCursor();
            _mouseLookActive = true;
            return;
        }

        if (!GetCursorPos(out Point cursor))
            return;

        float deltaX = cursor.X - _mouseCenterX;
        float deltaY = cursor.Y - _mouseCenterY;
        CenterCursor();
        _cameraYaw += deltaX * 0.003f;
        _cameraPitch = Math.Clamp(_cameraPitch - deltaY * 0.003f, -1.45f, 1.45f);
    }

    private void CenterCursor()
    {
        if (!GetClientRect(_windowHandle, out Rect client))
            return;

        Point center = new((client.Left + client.Right) / 2, (client.Top + client.Bottom) / 2);
        if (!ClientToScreen(_windowHandle, ref center))
            return;

        _mouseCenterX = center.X;
        _mouseCenterY = center.Y;
        SetCursorPos(_mouseCenterX, _mouseCenterY);
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private bool IsWindowFocused() => GetForegroundWindow() == _windowHandle;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint window, ref Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; public Point(int x, int y) => (X, Y) = (x, y); }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private void CreateCameraResources()
    {
        var bufferDescriptor = new BufferDescriptor
        {
            Size = (ulong)sizeof(CameraUniforms),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            MappedAtCreation = false
        };
        _cameraUniformBuffer = Runtime.Api.DeviceCreateBuffer(Device.UnsafeHandle, in bufferDescriptor);

        var layoutEntry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform }
        };
        var layoutDescriptor = new BindGroupLayoutDescriptor
        {
            EntryCount = 1,
            Entries = &layoutEntry
        };
        _cameraBindGroupLayout = Runtime.Api.DeviceCreateBindGroupLayout(Device.UnsafeHandle, in layoutDescriptor);

        BindGroupLayout* cameraLayout = _cameraBindGroupLayout;
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = &cameraLayout
        };
        _cameraPipelineLayout = Runtime.Api.DeviceCreatePipelineLayout(
            Device.UnsafeHandle, in pipelineLayoutDescriptor);

        var bindGroupEntry = new BindGroupEntry
        {
            Binding = 0,
            Buffer = _cameraUniformBuffer,
            Size = (ulong)sizeof(CameraUniforms)
        };
        var bindGroupDescriptor = new BindGroupDescriptor
        {
            Layout = _cameraBindGroupLayout,
            EntryCount = 1,
            Entries = &bindGroupEntry
        };
        _cameraBindGroup = Runtime.Api.DeviceCreateBindGroup(Device.UnsafeHandle, in bindGroupDescriptor);
    }

    private void CreateCubeResources()
    {
        string shaderSource = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Shaders", "Cube.wgsl"));

        float[] vertices =
        [
            // Back
            -0.5f, -0.5f, -0.35f, 0.8f, 0.2f, 0.2f,  0.5f, -0.5f, -0.35f, 0.8f, 0.2f, 0.2f,  0.5f, 0.5f, -0.35f, 0.8f, 0.2f, 0.2f,
             0.5f, 0.5f, -0.35f, 0.8f, 0.2f, 0.2f, -0.5f, 0.5f, -0.35f, 0.8f, 0.2f, 0.2f, -0.5f, -0.5f, -0.35f, 0.8f, 0.2f, 0.2f,
            // Front
            -0.5f, -0.5f,  0.35f, 0.2f, 0.8f, 1.0f,  0.5f, 0.5f,  0.35f, 0.2f, 0.8f, 1.0f,  0.5f, -0.5f,  0.35f, 0.2f, 0.8f, 1.0f,
            -0.5f, -0.5f,  0.35f, 0.2f, 0.8f, 1.0f, -0.5f, 0.5f,  0.35f, 0.2f, 0.8f, 1.0f,  0.5f, 0.5f,  0.35f, 0.2f, 0.8f, 1.0f,
            // Left
            -0.5f, -0.5f, -0.35f, 0.2f, 0.4f, 1.0f, -0.5f, 0.5f,  0.35f, 0.2f, 0.4f, 1.0f, -0.5f, -0.5f,  0.35f, 0.2f, 0.4f, 1.0f,
            -0.5f, -0.5f, -0.35f, 0.2f, 0.4f, 1.0f, -0.5f, 0.5f, -0.35f, 0.2f, 0.4f, 1.0f, -0.5f, 0.5f,  0.35f, 0.2f, 0.4f, 1.0f,
            // Right
             0.5f, -0.5f, -0.35f, 1.0f, 0.5f, 0.2f,  0.5f, -0.5f,  0.35f, 1.0f, 0.5f, 0.2f,  0.5f, 0.5f,  0.35f, 1.0f, 0.5f, 0.2f,
             0.5f, -0.5f, -0.35f, 1.0f, 0.5f, 0.2f,  0.5f, 0.5f,  0.35f, 1.0f, 0.5f, 0.2f,  0.5f, 0.5f, -0.35f, 1.0f, 0.5f, 0.2f,
            // Top
            -0.5f,  0.5f, -0.35f, 0.9f, 0.8f, 0.2f,  0.5f, 0.5f, -0.35f, 0.9f, 0.8f, 0.2f,  0.5f, 0.5f,  0.35f, 0.9f, 0.8f, 0.2f,
            -0.5f,  0.5f, -0.35f, 0.9f, 0.8f, 0.2f,  0.5f, 0.5f,  0.35f, 0.9f, 0.8f, 0.2f, -0.5f, 0.5f,  0.35f, 0.9f, 0.8f, 0.2f,
            // Bottom
            -0.5f, -0.5f, -0.35f, 0.2f, 0.9f, 0.4f, -0.5f, -0.5f,  0.35f, 0.2f, 0.9f, 0.4f,  0.5f, -0.5f,  0.35f, 0.2f, 0.9f, 0.4f,
            -0.5f, -0.5f, -0.35f, 0.2f, 0.9f, 0.4f,  0.5f, -0.5f,  0.35f, 0.2f, 0.9f, 0.4f,  0.5f, -0.5f, -0.35f, 0.2f, 0.9f, 0.4f
        ];

        nint shaderCode = Marshal.StringToHGlobalAnsi(shaderSource);
        nint vertexEntry = Marshal.StringToHGlobalAnsi("vs_main");
        nint fragmentEntry = Marshal.StringToHGlobalAnsi("fs_main");
        try
        {
            var wgslDescriptor = new ShaderModuleWGSLDescriptor
            {
                Code = (byte*)shaderCode
            };
            wgslDescriptor.Chain.SType = SType.ShaderModuleWgslDescriptor;

            var shaderDescriptor = new ShaderModuleDescriptor
            {
                NextInChain = (ChainedStruct*)&wgslDescriptor
            };
            _cubeShader = Runtime.Api.DeviceCreateShaderModule(Device.UnsafeHandle, in shaderDescriptor);
            if (_cubeShader == null)
                throw new InvalidOperationException("WebGPU could not create the cube shader.");

            fixed (float* data = vertices)
            {
                var bufferDescriptor = new BufferDescriptor
                {
                    Size = (ulong)(vertices.Length * sizeof(float)),
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                    MappedAtCreation = false
                };
                _cubeVertexBuffer = Runtime.Api.DeviceCreateBuffer(Device.UnsafeHandle, in bufferDescriptor);
                if (_cubeVertexBuffer == null)
                    throw new InvalidOperationException("WebGPU could not create the cube vertex buffer.");

                Runtime.Api.QueueWriteBuffer((Queue*)Queue.NativeHandle, _cubeVertexBuffer, 0, data,
                    (nuint)(vertices.Length * sizeof(float)));
            }

            var colorTarget = new ColorTargetState
            {
                Format = _surfaceFormat,
                WriteMask = ColorWriteMask.All
            };
            var fragment = new FragmentState
            {
                Module = _cubeShader,
                EntryPoint = (byte*)fragmentEntry,
                TargetCount = 1,
                Targets = &colorTarget
            };
            VertexAttribute* vertexAttributes = stackalloc VertexAttribute[2];
            vertexAttributes[0] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 0,
                ShaderLocation = 0
            };
            vertexAttributes[1] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 3 * sizeof(float),
                ShaderLocation = 1
            };
            var vertexBufferLayout = new VertexBufferLayout
            {
                ArrayStride = 6 * sizeof(float),
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 2,
                Attributes = vertexAttributes
            };
            var vertex = new VertexState
            {
                Module = _cubeShader,
                EntryPoint = (byte*)vertexEntry,
                BufferCount = 1,
                Buffers = &vertexBufferLayout
            };
            var primitive = new PrimitiveState
            {
                Topology = PrimitiveTopology.TriangleList,
                FrontFace = FrontFace.Ccw,
                CullMode = CullMode.None
            };
            var depthStencil = new DepthStencilState
            {
                Format = TextureFormat.Depth24Plus,
                DepthWriteEnabled = true,
                DepthCompare = CompareFunction.Less,
                StencilFront = new StencilFaceState { Compare = CompareFunction.Always },
                StencilBack = new StencilFaceState { Compare = CompareFunction.Always }
            };
            var pipelineDescriptor = new RenderPipelineDescriptor
            {
                Layout = _cameraPipelineLayout,
                Vertex = vertex,
                Primitive = primitive,
                DepthStencil = &depthStencil,
                Multisample = new MultisampleState { Count = 1, Mask = 0xFFFFFFFF },
                Fragment = &fragment
            };

            _cubePipeline = Runtime.Api.DeviceCreateRenderPipeline(Device.UnsafeHandle, in pipelineDescriptor);
            if (_cubePipeline == null)
                throw new InvalidOperationException("WebGPU could not create the cube pipeline.");
        }
        finally
        {
            Marshal.FreeHGlobal(shaderCode);
            Marshal.FreeHGlobal(vertexEntry);
            Marshal.FreeHGlobal(fragmentEntry);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_cubePipeline != null)
            Runtime.Api.RenderPipelineRelease(_cubePipeline);
        if (_cubeShader != null)
            Runtime.Api.ShaderModuleRelease(_cubeShader);
        if (_cubeVertexBuffer != null)
        {
            Runtime.Api.BufferDestroy(_cubeVertexBuffer);
            Runtime.Api.BufferRelease(_cubeVertexBuffer);
        }
        if (_cameraBindGroup != null)
            Runtime.Api.BindGroupRelease(_cameraBindGroup);
        if (_cameraPipelineLayout != null)
            Runtime.Api.PipelineLayoutRelease(_cameraPipelineLayout);
        if (_cameraBindGroupLayout != null)
            Runtime.Api.BindGroupLayoutRelease(_cameraBindGroupLayout);
        if (_cameraUniformBuffer != null)
        {
            Runtime.Api.BufferDestroy(_cameraUniformBuffer);
            Runtime.Api.BufferRelease(_cameraUniformBuffer);
        }
        if (_depthTextureView != null)
            Runtime.Api.TextureViewRelease(_depthTextureView);
        if (_depthTexture != null)
        {
            Runtime.Api.TextureDestroy(_depthTexture);
            Runtime.Api.TextureRelease(_depthTexture);
        }
        Device.Dispose();
        Adapter.Dispose();
        if (_surface != null)
            Runtime.Api.SurfaceRelease(_surface);
        Runtime.Dispose();
        _disposed = true;
    }
}
