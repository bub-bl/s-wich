using System.Numerics;
using Crowbar.Engine;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Crowbar.Editor.Rendering;

internal unsafe abstract class MeshRenderPass
{
    protected abstract bool ShouldRender(SceneObject obj, MeshRenderContext context);

    public void Execute(MeshRenderContext context, IEnumerable<SceneObject> sceneObjects)
    {
        context.Pipeline = Pipeline;
        WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(context.Pass, Pipeline);

        IEnumerable<SceneObject> objects = sceneObjects.Where(obj => ShouldRender(obj, context));
        objects = OrderObjects(objects, context);

        foreach (SceneObject obj in objects)
        {
            DrawObject(context, obj);
        }
    }

    protected abstract RenderPipeline* Pipeline { get; }

    protected virtual IEnumerable<SceneObject> OrderObjects(
        IEnumerable<SceneObject> objects,
        MeshRenderContext context) => objects;

    private static void DrawObject(MeshRenderContext context, SceneObject obj)
    {
        Matrix4x4 model = Matrix4x4.CreateScale(obj.ScaleX, obj.ScaleY, obj.ScaleZ)
                          * Matrix4x4.CreateRotationX(MathF.PI / 180f * obj.RotationX)
                          * Matrix4x4.CreateRotationY(MathF.PI / 180f * obj.RotationY)
                          * Matrix4x4.CreateRotationZ(MathF.PI / 180f * obj.RotationZ)
                          * Matrix4x4.CreateTranslation(obj.PositionX, obj.PositionY, obj.PositionZ);

        MeshUniforms uniforms = new()
        {
            Model = model,
            View = context.View,
            Proj = context.Proj,
            Color = context.GetColor(obj),
            LightDir = context.GetLightDirection(obj, context.LightDirection),
            IsSelected = context.Wireframe && obj.IsSelected ? 1u : 0u
        };

        MeshGpuResources resources = context.GetResources(obj);
        WebGpuApi.Wgpu.QueueWriteBuffer(context.Queue, resources.UniformBuffer, 0, &uniforms, (nuint)sizeof(MeshUniforms));
        WebGpuApi.Wgpu.RenderPassEncoderSetBindGroup(context.Pass, 0, resources.BindGroup, 0, null);

        if (obj.IsSelected && !context.Wireframe)
        {
            context.Selection = new SelectionRenderData
            {
                Object = obj,
                Model = model,
                Resources = resources
            };
        }

        bool pyramid = obj.MeshType == "Pyramid";
        Buffer* vertexBuffer = pyramid ? context.PyramidVertexBuffer : context.CubeVertexBuffer;
        Buffer* indexBuffer = pyramid
            ? (context.Wireframe ? context.PyramidWireframeIndexBuffer : context.PyramidIndexBuffer)
            : (context.Wireframe ? context.CubeWireframeIndexBuffer : context.CubeIndexBuffer);
        uint indexCount = pyramid
            ? (context.Wireframe ? 30u : 18u)
            : (context.Wireframe ? 48u : 36u);

        WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(context.Pass, 0, vertexBuffer, 0,
            (ulong)((pyramid ? 16 : 24) * 6 * sizeof(float)));
        WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(context.Pass, indexBuffer, IndexFormat.Uint16, 0,
            (ulong)(indexCount * sizeof(ushort)));
        WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(context.Pass, indexCount, 1, 0, 0, 0);
    }
}

internal unsafe sealed class OpaqueMeshPass(RenderPipeline* pipeline) : MeshRenderPass
{
    protected override RenderPipeline* Pipeline { get; } = pipeline;
    protected override bool ShouldRender(SceneObject obj, MeshRenderContext context) =>
        context.GetColor(obj).W >= 1.0f;
}

internal unsafe sealed class TransparentMeshPass(RenderPipeline* pipeline) : MeshRenderPass
{
    protected override RenderPipeline* Pipeline { get; } = pipeline;
    protected override bool ShouldRender(SceneObject obj, MeshRenderContext context) =>
        context.GetColor(obj).W < 1.0f;

    protected override IEnumerable<SceneObject> OrderObjects(
        IEnumerable<SceneObject> objects,
        MeshRenderContext context) => objects.OrderByDescending(obj => Vector3.DistanceSquared(
            new Vector3(obj.PositionX, obj.PositionY, obj.PositionZ), context.CameraPosition));
}

internal unsafe sealed class WireframeMeshPass(RenderPipeline* pipeline) : MeshRenderPass
{
    protected override RenderPipeline* Pipeline { get; } = pipeline;
    protected override bool ShouldRender(SceneObject obj, MeshRenderContext context) => true;
}
