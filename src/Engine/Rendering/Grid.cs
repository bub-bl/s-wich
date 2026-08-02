using System.Numerics;
using System.Runtime.InteropServices;

namespace Crowbar.Engine.Rendering;

/// <summary>
/// Configuration and GPU data for the editor ground grid.
/// </summary>
public sealed class Grid
{
    public float Size { get; set; } = 50f;
    public float CellSize { get; set; } = 1f;
    public float FadeDistance { get; set; } = 50f;
    public Vector4 LineColor { get; set; } = new(0.35f, 0.35f, 0.38f, 1f);
    public Vector4 XAxisColor { get; set; } = new(0.2f, 0.4f, 0.9f, 1f);
    public Vector4 ZAxisColor { get; set; } = new(0.9f, 0.2f, 0.2f, 1f);
    public bool ShowAxes { get; set; } = true;

    public GridUniforms CreateUniforms(Matrix4x4 view, Matrix4x4 projection,
        Matrix4x4 viewInverse, Matrix4x4 projectionInverse) => new()
    {
        View = view,
        Proj = projection,
        ViewInv = viewInverse,
        ProjInv = projectionInverse,
        Settings = new(
            MathF.Max(Size, 0.01f),
            MathF.Max(CellSize, 0.001f),
            MathF.Max(FadeDistance, 0.01f),
            ShowAxes ? 1f : 0f),
        LineColor = LineColor,
        XAxisColor = XAxisColor,
        ZAxisColor = ZAxisColor
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct GridUniforms
{
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Matrix4x4 ViewInv;
    public Matrix4x4 ProjInv;
    public Vector4 Settings;
    public Vector4 LineColor;
    public Vector4 XAxisColor;
    public Vector4 ZAxisColor;
}
