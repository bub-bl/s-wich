using Crowbar.Engine;

namespace Crowbar.Engine.Rendering;

/// <summary>
/// Safe public API for rendering mesh scene objects.
/// </summary>
public abstract class MeshRenderPass
{
    private readonly UnsafeMeshRenderPass _implementation;

    internal MeshRenderPass(UnsafeMeshRenderPass implementation)
    {
        _implementation = implementation;
    }

    public void Execute(MeshRenderContext context, IEnumerable<SceneObject> sceneObjects)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sceneObjects);
        _implementation.Execute(context.UnsafeContext, sceneObjects);
    }
}

/// <summary>
/// Safe, opaque handle to a native render pipeline.
/// </summary>
public sealed class MeshRenderPipeline
{
    internal nint NativeHandle { get; }

    internal MeshRenderPipeline(nint nativeHandle)
    {
        if (nativeHandle == 0)
            throw new ArgumentException("A render pipeline handle is required.", nameof(nativeHandle));

        NativeHandle = nativeHandle;
    }
}

public sealed class OpaqueMeshRenderPass : MeshRenderPass
{
    public OpaqueMeshRenderPass(MeshRenderPipeline pipeline)
        : base(UnsafeMeshRenderPass.CreateOpaque(pipeline))
    {
    }
}

public sealed class TransparentMeshRenderPass : MeshRenderPass
{
    public TransparentMeshRenderPass(MeshRenderPipeline pipeline)
        : base(UnsafeMeshRenderPass.CreateTransparent(pipeline))
    {
    }
}

public sealed class WireframeMeshRenderPass : MeshRenderPass
{
    public WireframeMeshRenderPass(MeshRenderPipeline pipeline)
        : base(UnsafeMeshRenderPass.CreateWireframe(pipeline))
    {
    }
}
