using System.Numerics;

namespace Crowbar.UI;

/// <summary>World-space UI root. Projection/compositing is intentionally renderer-owned.</summary>
public sealed class WorldPanel : Panel
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector2 Size { get; set; } = new(1, 1);
    public bool Billboard { get; set; }
    public Matrix4x4 WorldMatrix => Matrix4x4.CreateScale(new Vector3(Size, 1)) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
}
