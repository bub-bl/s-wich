using System.Numerics;
using System.Runtime.InteropServices;

namespace Crowbar.Engine.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal struct MeshUniforms
{
    public Matrix4x4 Model;
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Vector4 Color;
    public Vector3 LightDir;
    public uint IsSelected;
    public Vector4 MaterialParams;
    public Vector4 CameraPosition;
}

internal sealed class MeshGpuResources
{
    public WebGpuBuffer UniformBuffer;
    public WebGpuBindGroup BindGroup;
    public List<ModelGpuMesh> ModelMeshes { get; } = [];
}

internal sealed class ModelGpuMesh
{
    public WebGpuBuffer VertexBuffer;
    public ulong VertexBufferSize;
    public WebGpuBuffer IndexBuffer;
    public WebGpuBuffer WireframeIndexBuffer;
    public uint IndexCount;
    public uint WireframeIndexCount;
    public WebGpuBindGroup MaterialBindGroup;
    public nint MaterialSampler;
    public nint[] MaterialTextures { get; } = new nint[5];
    public nint[] MaterialTextureViews { get; } = new nint[5];
}

internal sealed class SelectionRenderData
{
    public SceneObject Object { get; init; } = null!;
    public Matrix4x4 Model { get; init; }
    public MeshGpuResources Resources { get; init; } = null!;
}

/// <summary>
/// Safe data passed to a mesh render pass for one render pass encoder.
/// Native WebGPU handles are intentionally represented as opaque values.
/// </summary>
public sealed class MeshRenderContext
{
    internal WebGpuRuntime Runtime { get; init; } = null!;
    internal RenderPass Pass { get; init; } = null!;
    internal WebGpuQueue Queue { get; init; }
    internal Matrix4x4 View { get; init; }
    internal Matrix4x4 Proj { get; init; }
    internal Vector3 LightDirection { get; init; }
    internal Vector3 CameraPosition { get; init; }
    internal bool Wireframe { get; init; }

    internal WebGpuBuffer CubeVertexBuffer { get; init; }
    internal WebGpuBuffer CubeIndexBuffer { get; init; }
    internal WebGpuBuffer CubeWireframeIndexBuffer { get; init; }
    internal WebGpuBuffer PyramidVertexBuffer { get; init; }
    internal WebGpuBuffer PyramidIndexBuffer { get; init; }
    internal WebGpuBuffer PyramidWireframeIndexBuffer { get; init; }

    internal Func<SceneObject, MeshGpuResources> GetResources { get; init; } = null!;
    internal Func<SceneObject, Vector4> GetColor { get; init; } = null!;
    internal Func<SceneObject, Vector3, Vector3> GetLightDirection { get; init; } = null!;

    internal SelectionRenderData? Selection { get; set; }
}
