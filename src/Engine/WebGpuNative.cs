using Silk.NET.WebGPU;

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
}
