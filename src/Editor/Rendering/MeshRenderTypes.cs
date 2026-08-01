using System.Numerics;
using System.Runtime.InteropServices;
using Crowbar.Engine;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Crowbar.Editor.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal struct MeshUniforms
{
    public Matrix4x4 Model;
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Vector4 Color;
    public Vector3 LightDir;
    public uint IsSelected;
}

internal unsafe sealed class MeshGpuResources
{
    public Buffer* UniformBuffer;
    public BindGroup* BindGroup;
}

internal sealed class SelectionRenderData
{
    public SceneObject Object { get; init; } = null!;
    public Matrix4x4 Model { get; init; }
    public MeshGpuResources Resources { get; init; } = null!;
}

internal unsafe sealed class MeshRenderContext
{
    public RenderPassEncoder* Pass;
    public Queue* Queue;
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Vector3 LightDirection;
    public Vector3 CameraPosition;
    public RenderPipeline* Pipeline;
    public bool Wireframe;

    public Buffer* CubeVertexBuffer;
    public Buffer* CubeIndexBuffer;
    public Buffer* CubeWireframeIndexBuffer;
    public Buffer* PyramidVertexBuffer;
    public Buffer* PyramidIndexBuffer;
    public Buffer* PyramidWireframeIndexBuffer;

    public required Func<SceneObject, MeshGpuResources> GetResources;
    public required Func<SceneObject, Vector4> GetColor;
    public required Func<SceneObject, Vector3, Vector3> GetLightDirection;

    public SelectionRenderData? Selection { get; set; }
}
