using System.Numerics;
using Crowbar.Engine;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace Crowbar.Editor.Rendering;

internal unsafe abstract class UnsafeMeshRenderPass
{
    protected abstract RenderPipeline* Pipeline { get; }

    public void Execute(UnsafeMeshRenderContext context, IEnumerable<SceneObject> sceneObjects)
    {
        context.Pipeline = Pipeline;
        WebGpuApi.Wgpu.RenderPassEncoderSetPipeline(context.Pass, Pipeline);

        IEnumerable<SceneObject> objects = sceneObjects.Where(obj => ShouldRender(obj, context));
        objects = OrderObjects(objects, context);

        foreach (SceneObject obj in objects)
            DrawObject(context, obj);
    }

    protected abstract bool ShouldRender(SceneObject obj, UnsafeMeshRenderContext context);

    protected virtual IEnumerable<SceneObject> OrderObjects(
        IEnumerable<SceneObject> objects,
        UnsafeMeshRenderContext context) => objects;

    private static void DrawObject(UnsafeMeshRenderContext context, SceneObject obj)
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
        WebGpuApi.Wgpu.QueueWriteBuffer(context.Queue, resources.UniformBuffer, 0, &uniforms,
            (nuint)sizeof(MeshUniforms));
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

        if (obj.Model != null && resources.ModelMeshes.Count > 0)
        {
            DrawModel(context.Pass, resources, context.Wireframe);
            return;
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

    internal static void DrawModel(RenderPassEncoder* pass, MeshGpuResources resources, bool wireframe)
    {
        foreach (ModelGpuMesh mesh in resources.ModelMeshes)
        {
            Buffer* indexBuffer = wireframe ? mesh.WireframeIndexBuffer : mesh.IndexBuffer;
            uint indexCount = wireframe ? mesh.WireframeIndexCount : mesh.IndexCount;
            WebGpuApi.Wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, mesh.VertexBuffer, 0,
                mesh.VertexBufferSize);
            WebGpuApi.Wgpu.RenderPassEncoderSetIndexBuffer(pass, indexBuffer, IndexFormat.Uint32, 0,
                (ulong)(indexCount * sizeof(uint)));
            WebGpuApi.Wgpu.RenderPassEncoderDrawIndexed(pass, indexCount, 1, 0, 0, 0);
        }
    }

    internal static UnsafeMeshRenderPass CreateOpaque(MeshRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return new UnsafeOpaqueMeshRenderPass(pipeline);
    }

    internal static UnsafeMeshRenderPass CreateTransparent(MeshRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return new UnsafeTransparentMeshRenderPass(pipeline);
    }

    internal static UnsafeMeshRenderPass CreateWireframe(MeshRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return new UnsafeWireframeMeshRenderPass(pipeline);
    }

    internal static MeshRenderPipeline CreatePipeline(RenderPipeline* pipeline) =>
        new((nint)pipeline);

    private sealed class UnsafeOpaqueMeshRenderPass(MeshRenderPipeline pipeline) : UnsafeMeshRenderPass
    {
        protected override RenderPipeline* Pipeline => (RenderPipeline*)pipeline.NativeHandle;

        protected override bool ShouldRender(SceneObject obj, UnsafeMeshRenderContext context) =>
            context.GetColor(obj).W >= 1.0f;
    }

    private sealed class UnsafeTransparentMeshRenderPass(MeshRenderPipeline pipeline) : UnsafeMeshRenderPass
    {
        protected override RenderPipeline* Pipeline => (RenderPipeline*)pipeline.NativeHandle;

        protected override bool ShouldRender(SceneObject obj, UnsafeMeshRenderContext context) =>
            context.GetColor(obj).W < 1.0f;

        protected override IEnumerable<SceneObject> OrderObjects(
            IEnumerable<SceneObject> objects,
            UnsafeMeshRenderContext context) => objects.OrderByDescending(obj => Vector3.DistanceSquared(
                new Vector3(obj.PositionX, obj.PositionY, obj.PositionZ), context.CameraPosition));
    }

    private sealed class UnsafeWireframeMeshRenderPass(MeshRenderPipeline pipeline) : UnsafeMeshRenderPass
    {
        protected override RenderPipeline* Pipeline => (RenderPipeline*)pipeline.NativeHandle;

        protected override bool ShouldRender(SceneObject obj, UnsafeMeshRenderContext context) => true;
    }
}
