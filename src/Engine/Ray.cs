using System.Numerics;

namespace Crowbar.Engine;

public readonly record struct Ray(Vector3 Origin, Vector3 Direction);