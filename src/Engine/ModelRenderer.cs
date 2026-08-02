namespace Crowbar.Engine;

public sealed class ModelRenderer : Renderer
{
    public ModelRenderer(SceneObject? sceneObject = null)
    {
        SceneObject = sceneObject ?? new SceneObject();
        SceneObject.OwnerRenderer = this;
    }

    public override SceneObject SceneObject { get; }
    [Property] public Model? Model { get; set; }
}
