namespace Crowbar.Engine;

public sealed class RotateComponent : Component
{
    [Property] public float Speed { get; set; } = 45f;

    protected internal override void OnUpdate(float deltaTime)
    {
        if (GameObject?.ModelRenderer?.SceneObject is not { } sceneObject)
            return;

        sceneObject.RotationY = (sceneObject.RotationY + Speed * deltaTime) % 360f;
    }
}
