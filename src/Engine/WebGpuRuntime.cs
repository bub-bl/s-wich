using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace Crowbar.Engine;

/// <summary>
/// Owns one WebGPU API instance and its native instance handle.
/// </summary>
public sealed class WebGpuRuntime : IDisposable
{
    internal WebGPU Api { get; }
    internal WebGPU Wgpu => Api;
    public WebGpuInstance Instance { get; }

    public WebGpuRuntime()
    {
        Api = WebGPU.GetApi();
        Instance = new WebGpuInstance(this);
        Console.WriteLine("Created WebGPU runtime.");
    }

    public void ConfigureDebugCallback(WebGpuDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        unsafe
        {
            var callback = PfnErrorCallback.From((type, msgPtr, _) =>
            {
                var message = Marshal.PtrToStringUTF8((IntPtr)msgPtr);
                Console.WriteLine($"WGPU Unhandled Error: {type} -> {message}");
            });

            Api.DeviceSetUncapturedErrorCallback(device.UnsafeHandle, callback, null);
        }
    }

    public void Dispose()
    {
        Instance.Dispose();
        Api.Dispose();
        Console.WriteLine("Disposed WebGPU runtime.");
    }
}
