namespace Crowbar.Engine;

public abstract class Renderer : Component
{
    public abstract SceneObject SceneObject { get; }

    public bool Enabled { get; set; } = true;
}
