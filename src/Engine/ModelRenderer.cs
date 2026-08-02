namespace Crowbar.Engine;

public sealed class ModelRenderer : Renderer
{
    [Property] public Model? Model { get; set; }

    public SceneObject SceneObject { get; }

    public ModelRenderer(SceneObject? sceneObject = null)
    {
        SceneObject = sceneObject ?? new SceneObject();
        SceneObject.OwnerRenderer = this;
    }

    protected internal override void OnDestroy()
    {
        if (ReferenceEquals(SceneObject.OwnerRenderer, this))
            SceneObject.OwnerRenderer = null;
    }
}
