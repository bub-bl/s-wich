namespace Crowbar.Engine;

public sealed class RotateComponent : Component
{
    [Property] public float Speed { get; set; } = 45f;

    protected internal override void OnUpdate(float deltaTime)
    {
        WorldRotation *= Rotation.FromYaw(Speed * deltaTime % 360f);
    }
}
