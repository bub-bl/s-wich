using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace Crowbar.Engine;

public sealed unsafe class WebGpuAdapter : IDisposable
{
    private Adapter* _adapter;

    public WebGpuAdapter(WebGpuInstance instance, Surface* surface)
    {
        var adapterOptions = new RequestAdapterOptions
        {
            CompatibleSurface = surface,
            BackendType = BackendType.Undefined, // Autodetect best backend (Vulkan / D3D12 / Metal)
            PowerPreference = PowerPreference.HighPerformance
        };

        var callback = PfnRequestAdapterCallback.From((status, adapter, msgPtr, userDataPtr) =>
        {
            if (status is RequestAdapterStatus.Success)
            {
                _adapter = adapter;
                Console.WriteLine("Retrieved WebGPU adapter.");
                return;
            }

            var message = Marshal.PtrToStringUTF8((IntPtr)msgPtr);
            Console.WriteLine($"Failed to create WebGPU adapter: {message}");
        });

        WebGpuApi.Wgpu.InstanceRequestAdapter(instance, in adapterOptions, callback, null);
        Console.WriteLine("Created WebGPU adapter.");
    }

    public WebGpuDevice CreateDevice()
    {
        return new WebGpuDevice(this);
    }

    public static implicit operator Adapter*(WebGpuAdapter adapter)
    {
        return adapter._adapter;
    }

    public void Dispose()
    {
        if (_adapter != null)
        {
            WebGpuApi.Wgpu.AdapterRelease(_adapter);
        }
    }
}
