using System;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace Crowbar;

internal static class WebGpuApi
{
    public static WebGPU Wgpu { get; private set; } = null!;
    public static WebGpuInstance Instance { get; private set; } = null!;

    public static void Initialize()
    {
        if (Wgpu != null) return;
        Wgpu = WebGPU.GetApi();
        Console.WriteLine("Created WebGPU API.");
        Instance = new WebGpuInstance();
    }

    public static void ConfigureDebugCallback(WebGpuDevice device)
    {
        unsafe
        {
            var callback = PfnErrorCallback.From((type, msgPtr, userDataPtr) =>
            {
                var message = Marshal.PtrToStringUTF8((IntPtr)msgPtr);
                Console.WriteLine($"WGPU Unhandled Error: {type} -> {message}");
            });

            Wgpu.DeviceSetUncapturedErrorCallback(device, callback, null);
            Console.WriteLine("Created WebGPU debug callback.");
        }
    }

    public static void Dispose()
    {
        Instance?.Dispose();
        Instance = null!;
        Wgpu?.Dispose();
        Wgpu = null!;
        Console.WriteLine("Disposed WebGPU.");
    }
}
