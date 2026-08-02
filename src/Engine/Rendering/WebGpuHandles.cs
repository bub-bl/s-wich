namespace Crowbar.Engine.Rendering;

/// <summary>Opaque safe handle for a WebGPU queue.</summary>
public readonly struct WebGpuQueue
{
    internal nint NativeHandle { get; }

    internal WebGpuQueue(nint nativeHandle) => NativeHandle = Require(nativeHandle);

    internal static WebGpuQueue FromNative(nint nativeHandle) => new(nativeHandle);

    private static nint Require(nint handle) => handle == 0
        ? throw new ArgumentException("A valid WebGPU queue handle is required.", nameof(handle))
        : handle;
}

/// <summary>Opaque safe handle for a WebGPU render pass encoder.</summary>
public readonly struct WebGpuRenderPassEncoder
{
    internal nint NativeHandle { get; }

    internal WebGpuRenderPassEncoder(nint nativeHandle) => NativeHandle = Require(nativeHandle);

    internal static WebGpuRenderPassEncoder FromNative(nint nativeHandle) => new(nativeHandle);

    private static nint Require(nint handle) => handle == 0
        ? throw new ArgumentException("A valid WebGPU render pass encoder handle is required.", nameof(handle))
        : handle;
}

/// <summary>Opaque safe handle for a WebGPU buffer.</summary>
public readonly struct WebGpuBuffer
{
    internal nint NativeHandle { get; }

    internal WebGpuBuffer(nint nativeHandle) => NativeHandle = Require(nativeHandle);

    internal static WebGpuBuffer FromNative(nint nativeHandle) => new(nativeHandle);

    private static nint Require(nint handle) => handle == 0
        ? throw new ArgumentException("A valid WebGPU buffer handle is required.", nameof(handle))
        : handle;
}

/// <summary>Opaque safe handle for a WebGPU bind group.</summary>
public readonly struct WebGpuBindGroup
{
    internal nint NativeHandle { get; }

    internal WebGpuBindGroup(nint nativeHandle) => NativeHandle = Require(nativeHandle);

    internal static WebGpuBindGroup FromNative(nint nativeHandle) => new(nativeHandle);

    private static nint Require(nint handle) => handle == 0
        ? throw new ArgumentException("A valid WebGPU bind group handle is required.", nameof(handle))
        : handle;
}

/// <summary>Opaque safe handle for a WebGPU render pipeline.</summary>
public readonly struct WebGpuRenderPipeline
{
    internal nint NativeHandle { get; }

    internal WebGpuRenderPipeline(nint nativeHandle) => NativeHandle = Require(nativeHandle);

    internal static WebGpuRenderPipeline FromNative(nint nativeHandle) => new(nativeHandle);

    private static nint Require(nint handle) => handle == 0
        ? throw new ArgumentException("A valid WebGPU render pipeline handle is required.", nameof(handle))
        : handle;
}
