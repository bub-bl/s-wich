using System;
using Silk.NET.WebGPU;

namespace MyApp;

public sealed class WebGpuInstance : IDisposable
{
    private readonly unsafe Instance* _instance;

    public WebGpuInstance()
    {
        unsafe
        {
            var instanceDescriptor = new InstanceDescriptor();
            _instance = WebGpuApi.Wgpu.CreateInstance(in instanceDescriptor);
        }

        Console.WriteLine("Created WebGPU instance.");
    }

    public static unsafe implicit operator Instance*(WebGpuInstance instance)
    {
        return instance._instance;
    }

    public void Dispose()
    {
        unsafe
        {
            if (_instance != null)
            {
                WebGpuApi.Wgpu.InstanceRelease(_instance);
            }
        }
    }
}
