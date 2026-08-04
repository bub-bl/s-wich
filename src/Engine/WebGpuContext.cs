using Crowbar.Engine.Rendering;
using Silk.NET.WebGPU;

namespace Crowbar.Engine;

/// <summary>
/// Owns the first usable WebGPU device for the runtime.
/// Owns the window surface and keeps its configuration synchronized with the framebuffer size.
/// </summary>
public sealed unsafe class WebGpuContext : IDisposable
{
    public WebGpuRuntime Runtime { get; }
    public WebGpuAdapter Adapter { get; }
    public WebGpuDevice Device { get; }
    public WebGpuQueue Queue { get; }

    private Surface* _surface;
    private TextureFormat _surfaceFormat;
    private bool _hasPresentedFrame;
    private bool _disposed;

    public WebGpuContext(nint windowHandle, int width, int height)
    {
        Runtime = new WebGpuRuntime();
        try
        {
            if (windowHandle == 0)
                throw new ArgumentException("The window does not expose a native handle.", nameof(windowHandle));

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
            }
        };

        WebGpuRenderPassEncoder pass = Runtime.BeginRenderPass(encoder, passDescription);
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

    public void Resize(int width, int height)
    {
        if (_disposed || _surface == null || width <= 0 || height <= 0)
            return;

        ConfigureSurface(width, height);
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
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Device.Dispose();
        Adapter.Dispose();
        if (_surface != null)
            Runtime.Api.SurfaceRelease(_surface);
        Runtime.Dispose();
        _disposed = true;
    }
}
