using Silk.NET.WebGPU;
using Crowbar.Engine.Rendering;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Crowbar.Engine;

/// <summary>
/// Internal unsafe boundary for calls whose Silk.NET signatures contain pointers.
/// </summary>
internal static unsafe class WebGpuNative
{
    internal static void ReleaseInstance(WebGPU api, nint handle) =>
        api.InstanceRelease((Instance*)handle);

    internal static void ReleaseAdapter(WebGPU api, nint handle) =>
        api.AdapterRelease((Adapter*)handle);

    internal static void ReleaseDevice(WebGPU api, nint handle) =>
        api.DeviceRelease((Device*)handle);

    internal static void SetPipeline(WebGPU api, WebGpuRenderPassEncoder pass, WebGpuRenderPipeline pipeline) =>
        api.RenderPassEncoderSetPipeline((RenderPassEncoder*)pass.NativeHandle, (RenderPipeline*)pipeline.NativeHandle);

    internal static unsafe void WriteBuffer(
        WebGPU api,
        WebGpuQueue queue,
        WebGpuBuffer buffer,
        in MeshUniforms data)
    {
        MeshUniforms copy = data;
        api.QueueWriteBuffer((Queue*)queue.NativeHandle, (Buffer*)buffer.NativeHandle, 0, &copy,
            (nuint)sizeof(MeshUniforms));
    }

    internal static void SetBindGroup(WebGPU api, WebGpuRenderPassEncoder pass, WebGpuBindGroup bindGroup) =>
        api.RenderPassEncoderSetBindGroup((RenderPassEncoder*)pass.NativeHandle, 0,
            (BindGroup*)bindGroup.NativeHandle, 0, null);

    internal static void SetVertexBuffer(WebGPU api, WebGpuRenderPassEncoder pass, WebGpuBuffer buffer, ulong size) =>
        api.RenderPassEncoderSetVertexBuffer((RenderPassEncoder*)pass.NativeHandle, 0,
            (Buffer*)buffer.NativeHandle, 0, size);

    internal static void SetIndexBuffer(
        WebGPU api,
        WebGpuRenderPassEncoder pass,
        WebGpuBuffer buffer,
        IndexFormat format,
        ulong size) =>
        api.RenderPassEncoderSetIndexBuffer((RenderPassEncoder*)pass.NativeHandle,
            (Buffer*)buffer.NativeHandle, format, 0, size);

    internal static void DrawIndexed(WebGPU api, WebGpuRenderPassEncoder pass, uint indexCount) =>
        api.RenderPassEncoderDrawIndexed((RenderPassEncoder*)pass.NativeHandle, indexCount, 1, 0, 0, 0);
}
