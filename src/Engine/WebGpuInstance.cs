using Silk.NET.WebGPU;

namespace Crowbar.Engine;

public sealed unsafe class WebGpuInstance : IDisposable
{
    private readonly Instance* _instance;

    public WebGpuInstance()
    {
        var instanceDescriptor = new InstanceDescriptor();
        _instance = WebGpuApi.Wgpu.CreateInstance(in instanceDescriptor);

        Console.WriteLine("Created WebGPU instance.");
    }

    public static implicit operator Instance*(WebGpuInstance instance)
    {
        return instance._instance;
    }

    public void Dispose()
    {
        if (_instance != null)
        {
            WebGpuApi.Wgpu.InstanceRelease(_instance);
        }
    }
}
