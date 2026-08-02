using System.Numerics;

namespace Crowbar.Engine.Rendering;

public enum MeshRenderPassMode
{
    Opaque,
    Transparent,
    Wireframe
}

/// <summary>
/// Safe mesh render pass. GPU calls are routed through the runtime supplied by
/// <see cref="MeshRenderContext"/>; this class never handles native pointers.
/// </summary>
public sealed class MeshRenderPass
{
    private readonly WebGpuRenderPipeline _pipeline;
    private readonly WebGpuRenderPipeline? _modelPipeline;
    private readonly MeshRenderPassMode _mode;

    public MeshRenderPass(WebGpuRenderPipeline pipeline, MeshRenderPassMode mode, WebGpuRenderPipeline? modelPipeline = null)
    {
        _pipeline = pipeline;
        _mode = mode;
        _modelPipeline = modelPipeline;
    }

    public void Execute(MeshRenderContext context, IEnumerable<SceneObject> sceneObjects)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sceneObjects);

        context.Pass.SetPipeline(_pipeline);

        IEnumerable<SceneObject> objects = sceneObjects.Where(obj => ShouldRender(obj, context));

        if (_mode == MeshRenderPassMode.Transparent)
        {
            objects = objects.OrderByDescending(obj => Vector3.DistanceSquared(
                new Vector3(obj.PositionX, obj.PositionY, obj.PositionZ), context.CameraPosition));
        }

        foreach (SceneObject obj in objects)
            DrawObject(context, obj);
    }

    internal static WebGpuRenderPipeline CreatePipeline(nint nativeHandle) =>
        WebGpuRenderPipeline.FromNative(nativeHandle);

    internal static void DrawModel(
        RenderPass pass,
        MeshGpuResources resources,
        bool wireframe)
    {
        foreach (ModelGpuMesh mesh in resources.ModelMeshes)
        {
            if (!wireframe && mesh.MaterialBindGroup.NativeHandle != nint.Zero)
                pass.SetBindGroup(mesh.MaterialBindGroup, 1);
            WebGpuBuffer indexBuffer = wireframe ? mesh.WireframeIndexBuffer : mesh.IndexBuffer;
            uint indexCount = wireframe ? mesh.WireframeIndexCount : mesh.IndexCount;
            pass.SetVertexBuffer(mesh.VertexBuffer, mesh.VertexBufferSize);
            pass.SetIndexBuffer(indexBuffer, WebGpuIndexFormat.Uint32, (ulong)(indexCount * sizeof(uint)));
            pass.DrawIndexed(indexCount);
        }
    }

    private bool ShouldRender(SceneObject obj, MeshRenderContext context) => _mode switch
    {
        MeshRenderPassMode.Opaque => context.GetColor(obj).W >= 1.0f,
        MeshRenderPassMode.Transparent => context.GetColor(obj).W < 1.0f,
        MeshRenderPassMode.Wireframe => true,
        _ => throw new ArgumentOutOfRangeException()
    };

    private void DrawObject(MeshRenderContext context, SceneObject obj)
    {
        Matrix4x4 model = Matrix4x4.CreateScale(obj.ScaleX, obj.ScaleY, obj.ScaleZ)
                          * Matrix4x4.CreateRotationX(MathF.PI / 180f * obj.RotationX)
                          * Matrix4x4.CreateRotationY(MathF.PI / 180f * obj.RotationY)
                          * Matrix4x4.CreateRotationZ(MathF.PI / 180f * obj.RotationZ)
                          * Matrix4x4.CreateTranslation(obj.PositionX, obj.PositionY, obj.PositionZ);

        Vector4 color = context.GetColor(obj);
        if (obj.Model?.Materials.FirstOrDefault() is { } modelMaterial)
        {
            Vector3 tinted = new Vector3(color.X, color.Y, color.Z)
                * new Vector3(modelMaterial.BaseColorFactor.X, modelMaterial.BaseColorFactor.Y, modelMaterial.BaseColorFactor.Z);
            color = new Vector4(tinted, color.W);
        }

        MeshUniforms uniforms = new()
        {
            Model = model,
            View = context.View,
            Proj = context.Proj,
            Color = color,
            LightDir = context.GetLightDirection(obj, context.LightDirection),
            IsSelected = context.Wireframe && obj.IsSelected ? 1u : 0u,
            MaterialParams = obj.Model?.Materials.FirstOrDefault() is { } material
                ? new Vector4(material.MetallicFactor, material.RoughnessFactor, 1.0f, 0.0f)
                : new Vector4(1.0f, 1.0f, 1.0f, 0.0f),
            CameraPosition = new Vector4(context.CameraPosition, 1.0f)
        };

        MeshGpuResources resources = context.GetResources(obj);
        context.Runtime.WriteBuffer(context.Queue, resources.UniformBuffer, in uniforms);
        context.Pass.SetBindGroup(resources.BindGroup);

        if (obj.IsSelected && !context.Wireframe)
        {
            context.Selection = new SelectionRenderData
            {
                Object = obj,
                Model = model,
                Resources = resources
            };
        }

        if (obj.Model != null && resources.ModelMeshes.Count > 0)
        {
            if (!context.Wireframe && _modelPipeline != null)
                context.Pass.SetPipeline(_modelPipeline.Value);
            DrawModel(context.Pass, resources, context.Wireframe);
            return;
        }

        context.Pass.SetPipeline(_pipeline);

        bool pyramid = obj.MeshType == "Pyramid";
        WebGpuBuffer vertexBuffer = pyramid ? context.PyramidVertexBuffer : context.CubeVertexBuffer;
        WebGpuBuffer indexBuffer = pyramid
            ? (context.Wireframe ? context.PyramidWireframeIndexBuffer : context.PyramidIndexBuffer)
            : (context.Wireframe ? context.CubeWireframeIndexBuffer : context.CubeIndexBuffer);
        uint indexCount = pyramid
            ? (context.Wireframe ? 30u : 18u)
            : (context.Wireframe ? 48u : 36u);

        context.Pass.SetVertexBuffer(vertexBuffer,
            (ulong)((pyramid ? 16 : 24) * 6 * sizeof(float)));
        context.Pass.SetIndexBuffer(indexBuffer, WebGpuIndexFormat.Uint16,
            (ulong)(indexCount * sizeof(ushort)));
        context.Pass.DrawIndexed(indexCount);
    }
}
