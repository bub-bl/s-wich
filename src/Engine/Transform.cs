using System.Globalization;
using System.Numerics;

namespace Crowbar.Engine;

public record struct Transform
{
    public Vector3 Position { get; set; }
    public Rotation Rotation { get; set; }
    public Vector3 Scale { get; set; }

    public static Transform Zero => new(Vector3.Zero, Rotation.Identity, Vector3.One);

    public Transform()
    {
        Position = Vector3.Zero;
        Rotation = Rotation.Identity;
        Scale = Vector3.One;
    }

    public Transform(Vector3 position) : this(position, Rotation.Identity, Vector3.One)
    {
    }

    public Transform(Vector3 position, Rotation rotation, Vector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Vector3 Forward => Rotation.Forward;
    public Vector3 Backward => Rotation.Backward;
    public Vector3 Up => Rotation.Up;
    public Vector3 Down => Rotation.Down;
    public Vector3 Right => Rotation.Right;
    public Vector3 Left => Rotation.Left;
    public Ray ForwardRay => new(Position, Forward);
    public float UniformScale => Scale.X;

    // public bool IsValid => IsFinite(Position) && IsFinite(Scale) &&
    //                        IsFinite(Rotation.X) && IsFinite(Rotation.Y) &&
    //                        IsFinite(Rotation.Z) && IsFinite(Rotation.W) &&
    //                        Rotation.Quaternion.LengthSquared() > float.Epsilon;

    public Vector3 PointToWorld(Vector3 localPoint) => Position + Rotation * (localPoint * Scale);

    public Vector3 PointToLocal(Vector3 worldPoint)
    {
        var local = Rotation.Inverse * (worldPoint - Position);

        return new Vector3(
            DivideOrZero(local.X, Scale.X),
            DivideOrZero(local.Y, Scale.Y),
            DivideOrZero(local.Z, Scale.Z));
    }

    public Vector3 NormalToWorld(Vector3 localNormal) => Rotation * localNormal;
    public Vector3 NormalToLocal(Vector3 worldNormal) => Rotation.Inverse * worldNormal;
    public Rotation RotationToWorld(Rotation localRotation) => Rotation * localRotation;
    public Rotation RotationToLocal(Rotation worldRotation) => Rotation.Inverse * worldRotation;

    public Transform ToWorld(Transform child) => Concat(this, child);

    public Transform ToLocal(Transform child) => new(
        PointToLocal(child.Position),
        RotationToLocal(child.Rotation),
        new Vector3(DivideOrZero(child.Scale.X, Scale.X), DivideOrZero(child.Scale.Y, Scale.Y),
            DivideOrZero(child.Scale.Z, Scale.Z)));

    public Transform Add(Vector3 position, bool worldSpace = false) => worldSpace
        ? this with { Position = Position + position }
        : this with { Position = Position + Rotation * (position * Scale) };

    public Transform RotateAround(Vector3 center, Rotation rotation) => this with
    {
        Position = center + rotation * (Position - center),
        Rotation = rotation * Rotation
    };

    public Transform WithPosition(Vector3 position) => this with { Position = position };
    public Transform WithRotation(Rotation rotation) => this with { Rotation = rotation };
    public Transform WithScale(Vector3 scale) => this with { Scale = scale };

    public bool AlmostEqual(Transform other, float delta = 0.001f) =>
        Vector3.DistanceSquared(Position, other.Position) <= delta * delta &&
        Vector3.DistanceSquared(Scale, other.Scale) <= delta * delta &&
        Rotation.AlmostEqual(other.Rotation, delta);

    public Transform LerpTo(Transform target, float fraction, bool clamp = true) => Lerp(this, target, fraction, clamp);

    public static Transform Concat(Transform parent, Transform local) => new(
        parent.PointToWorld(local.Position),
        parent.Rotation * local.Rotation,
        parent.Scale * local.Scale);

    public static Transform Lerp(Transform a, Transform b, float fraction, bool clamp = true)
    {
        if (clamp) fraction = Math.Clamp(fraction, 0f, 1f);
        return new Transform(
            Vector3.Lerp(a.Position, b.Position, fraction),
            Rotation.Slerp(a.Rotation, b.Rotation, fraction, false),
            Vector3.Lerp(a.Scale, b.Scale, fraction));
    }

    public static Transform Parse(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 10 || !parts.All(part =>
                float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            throw new FormatException("Transform must use the format 'px,py,pz,rx,ry,rz,rw,sx,sy,sz'.");

        var values = parts.Select(part => float.Parse(part, CultureInfo.InvariantCulture)).ToArray();

        return new Transform(
            new Vector3(values[0], values[1], values[2]),
            new Rotation(values[3], values[4], values[5], values[6]),
            new Vector3(values[7], values[8], values[9]));
    }

    public override string ToString() => string.Join(",",
        Position.X.ToString(CultureInfo.InvariantCulture), Position.Y.ToString(CultureInfo.InvariantCulture),
        Position.Z.ToString(CultureInfo.InvariantCulture),
        Rotation.X.ToString(CultureInfo.InvariantCulture), Rotation.Y.ToString(CultureInfo.InvariantCulture),
        Rotation.Z.ToString(CultureInfo.InvariantCulture), Rotation.W.ToString(CultureInfo.InvariantCulture),
        Scale.X.ToString(CultureInfo.InvariantCulture), Scale.Y.ToString(CultureInfo.InvariantCulture),
        Scale.Z.ToString(CultureInfo.InvariantCulture));

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(float value) => float.IsFinite(value);

    private static float DivideOrZero(float value, float divisor) =>
        MathF.Abs(divisor) < float.Epsilon ? 0f : value / divisor;
}