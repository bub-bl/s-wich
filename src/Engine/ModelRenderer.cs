namespace Crowbar.Engine;

public sealed class ModelRenderer : Renderer
{
    public ModelRenderer(SceneObject? sceneObject = null)
    {
        SceneObject = sceneObject ?? new SceneObject();
        SceneObject.OwnerRenderer = this;
    }

    public SceneObject SceneObject { get; }

    [Property] public Model? Model { get; set; }

    protected internal override void OnDestroy()
    {
        if (ReferenceEquals(SceneObject.OwnerRenderer, this))
            SceneObject.OwnerRenderer = null;
    }
}
