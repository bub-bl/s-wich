using System.Runtime.InteropServices;
using System.Numerics;
using Crowbar.Engine.Rendering;
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

    internal void SetPipeline(WebGpuRenderPassEncoder pass, WebGpuRenderPipeline pipeline) =>
        WebGpuNative.SetPipeline(Api, pass, pipeline);

    internal void WriteBuffer(WebGpuQueue queue, WebGpuBuffer buffer, in MeshUniforms data) =>
        WebGpuNative.WriteBuffer(Api, queue, buffer, in data);

    internal void SetBindGroup(WebGpuRenderPassEncoder pass, WebGpuBindGroup bindGroup) =>
        WebGpuNative.SetBindGroup(Api, pass, bindGroup);

    internal void SetVertexBuffer(WebGpuRenderPassEncoder pass, WebGpuBuffer buffer, ulong size) =>
        WebGpuNative.SetVertexBuffer(Api, pass, buffer, size);

    internal void SetIndexBuffer(WebGpuRenderPassEncoder pass, WebGpuBuffer buffer, IndexFormat format, ulong size) =>
        WebGpuNative.SetIndexBuffer(Api, pass, buffer, format, size);

    internal void DrawIndexed(WebGpuRenderPassEncoder pass, uint indexCount) =>
        WebGpuNative.DrawIndexed(Api, pass, indexCount);
}
