using System.Numerics;

namespace Crowbar.Engine.Rendering;

public sealed class RenderPassDescription
{
    public required WebGpuTextureView ColorView { get; init; }
    public WebGpuTextureView? DepthView { get; init; }
    public Vector4 ClearColor { get; init; } = new(0.12f, 0.12f, 0.14f, 1.0f);
    public float DepthClearValue { get; init; } = 1.0f;
}

public enum WebGpuIndexFormat
{
    Uint16,
    Uint32
}

public sealed class CommandList : IDisposable
{
    private readonly WebGpuRuntime _runtime;
    private readonly WebGpuQueue _queue;
    private WebGpuCommandEncoder _encoder;
    private RenderPass? _activePass;
    private bool _submitted;
    private bool _disposed;

    internal CommandList(WebGpuRuntime runtime, WebGpuCommandEncoder encoder, WebGpuQueue queue)
    {
        _runtime = runtime;
        _encoder = encoder;
        _queue = queue;
    }

    public RenderPass BeginRenderPass(RenderPassDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (_disposed) throw new ObjectDisposedException(nameof(CommandList));
        if (_submitted) throw new InvalidOperationException("The command list has already been submitted.");
        if (_activePass != null) throw new InvalidOperationException("A render pass is already active.");
        if (description.ColorView.NativeHandle == 0)
            throw new ArgumentException("A color attachment is required.", nameof(description));

        var handle = _runtime.BeginRenderPass(_encoder, description);
        _activePass = new RenderPass(this, _runtime, handle);
        return _activePass;
    }

    public void Submit()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CommandList));
        if (_submitted) throw new InvalidOperationException("The command list has already been submitted.");
        if (_activePass != null) throw new InvalidOperationException("End the active render pass before submitting.");

        var commandBuffer = _runtime.FinishCommandEncoder(_encoder);
        _runtime.Submit(_queue, commandBuffer);
        _runtime.ReleaseCommandBuffer(commandBuffer);
        _runtime.ReleaseCommandEncoder(_encoder);
        _encoder = default;
        _submitted = true;
    }

    internal void EndPass(RenderPass pass)
    {
        if (!ReferenceEquals(_activePass, pass))
            throw new InvalidOperationException("The render pass does not belong to this command list.");

        _runtime.EndRenderPass(pass.Handle);
        _activePass = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_activePass != null)
            _activePass.Dispose();

        if (!_submitted && _encoder.NativeHandle != 0)
        {
            _runtime.ReleaseCommandEncoder(_encoder);
            _encoder = default;
        }
    }
}

public sealed class RenderPass : IDisposable
{
    private readonly CommandList _owner;
    private readonly WebGpuRuntime _runtime;
    private WebGpuRenderPassEncoder _handle;
    private bool _ended;

    internal RenderPass(CommandList owner, WebGpuRuntime runtime, WebGpuRenderPassEncoder handle)
    {
        _owner = owner;
        _runtime = runtime;
        _handle = handle;
    }

    internal WebGpuRenderPassEncoder Handle => _handle;

    public void SetPipeline(WebGpuRenderPipeline pipeline)
    {
        EnsureActive();
        _runtime.SetPipeline(_handle, pipeline);
    }

    public void SetBindGroup(WebGpuBindGroup bindGroup)
    {
        EnsureActive();
        _runtime.SetBindGroup(_handle, bindGroup);
    }

    public void SetVertexBuffer(WebGpuBuffer buffer, ulong size)
    {
        EnsureActive();
        _runtime.SetVertexBuffer(_handle, buffer, size);
    }

    public void SetIndexBuffer(WebGpuBuffer buffer, WebGpuIndexFormat format, ulong size) =>
        SetIndexBufferCore(buffer, format, size);

    public void Draw(uint vertexCount)
    {
        EnsureActive();
        _runtime.Draw(_handle, vertexCount);
    }

    public void DrawIndexed(uint indexCount)
    {
        EnsureActive();
        _runtime.DrawIndexed(_handle, indexCount);
    }

    private void SetIndexBufferCore(WebGpuBuffer buffer, WebGpuIndexFormat format, ulong size)
    {
        EnsureActive();
        _runtime.SetIndexBuffer(_handle, buffer, format, size);
    }

    private void EnsureActive()
    {
        if (_ended) throw new InvalidOperationException("The render pass has already ended.");
    }

    public void End()
    {
        if (_ended) return;
        _owner.EndPass(this);
        _ended = true;
        _handle = default;
    }

    public void Dispose() => End();
}
