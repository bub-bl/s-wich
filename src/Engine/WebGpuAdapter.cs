using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace Crowbar.Engine;

public sealed class WebGpuAdapter : IDisposable
{
    private readonly WebGpuRuntime _runtime;
    private nint _nativeHandle;

    public WebGpuAdapter(WebGpuRuntime runtime, nint surfaceHandle)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (surfaceHandle == 0) throw new ArgumentException("A surface handle is required.", nameof(surfaceHandle));

        var adapterOptions = new RequestAdapterOptions
        {
            BackendType = BackendType.Undefined, // Autodetect best backend (Vulkan / D3D12 / Metal)
            PowerPreference = PowerPreference.HighPerformance
        };

        unsafe
        {
            adapterOptions.CompatibleSurface = (Surface*)surfaceHandle;
        }

        var callback = PfnRequestAdapterCallback.From((status, adapter, msgPtr, userDataPtr) =>
        {
            if (status is RequestAdapterStatus.Success)
            {
                _nativeHandle = (nint)adapter;
                Console.WriteLine("Retrieved WebGPU adapter.");
                return;
            }

            var message = Marshal.PtrToStringUTF8((IntPtr)msgPtr);
            Console.WriteLine($"Failed to create WebGPU adapter: {message}");
        });

        unsafe
        {
            _runtime.Api.InstanceRequestAdapter(_runtime.Instance.UnsafeHandle, in adapterOptions, callback, null);
        }
        Console.WriteLine("Created WebGPU adapter.");
    }

    public WebGpuDevice CreateDevice()
    {
        return new WebGpuDevice(_runtime, this);
    }

    internal nint NativeHandle => _nativeHandle;
    internal unsafe Adapter* UnsafeHandle => (Adapter*)_nativeHandle;

    public void Dispose()
    {
        if (_nativeHandle != 0)
        {
            WebGpuNative.ReleaseAdapter(_runtime.Api, _nativeHandle);
            _nativeHandle = 0;
        }
    }
}
