namespace Crowbar.Engine;

public sealed class ModelRenderer : Renderer
{
    [Property] public Model Model { get; set; } = null!;

    public SceneObject? SceneObject { get; private set; }
}