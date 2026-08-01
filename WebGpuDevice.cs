using System;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace MyApp;

public sealed unsafe class WebGpuDevice : IDisposable
{
    private Device* _device;

    public WebGpuDevice(WebGpuAdapter adapter)
    {
        var deviceOptions = new DeviceDescriptor();

        var callback = PfnRequestDeviceCallback.From((status, device, msgPtr, _) =>
        {
            if (status is RequestDeviceStatus.Success)
            {
                _device = device;
                Console.WriteLine("Retrieved WebGPU device.");
            }
            else
            {
                var message = Marshal.PtrToStringUTF8((IntPtr)msgPtr);
                Console.WriteLine($"Failed to create WebGPU device: {message}");
            }
        });

        WebGpuApi.Wgpu.AdapterRequestDevice(adapter, in deviceOptions, callback, null);
    }

    public Queue* GetQueue()
    {
        return WebGpuApi.Wgpu.DeviceGetQueue(_device);
    }

    public static implicit operator Device*(WebGpuDevice device)
    {
        return device._device;
    }

    public void Dispose()
    {
        if (_device != null)
        {
            WebGpuApi.Wgpu.DeviceRelease(_device);
        }
    }
}
