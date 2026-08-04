using SkiaSharp;

namespace Crowbar.Engine.UI;

public interface ICanvas
{
    void Clear(UiColor color);
    void FillRoundedRect(UiRect rect, float radius, UiColor color);
    void DrawText(string text, float x, float y, float size, UiColor color);
}

public interface IUiRenderer
{
    UiSize Size { get; }
    ReadOnlyMemory<byte> Render(ScreenPanel root);
    bool IsDirty { get; }
}

public sealed class SkiaUiRenderer : IUiRenderer, IDisposable
{
    private readonly YogaLayoutEngine _layout = new();
    private SKBitmap? _bitmap;
    private byte[] _pixels = [];
    private bool _dirty = true;
    public StyleSheet? StyleSheet { get; set; }
    public UiSize Size { get; private set; }
    public bool IsDirty => _dirty;
    public int LayoutPasses => _layout.LayoutPasses;

    public void Resize(int width, int height) { Size = new(width, height); _bitmap?.Dispose(); _bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul); _pixels = new byte[width * height * 4]; _dirty = true; }
    public ReadOnlyMemory<byte> Render(ScreenPanel root)
    {
        if (!_dirty && _pixels.Length != 0) return _pixels;
        if (_bitmap is null || _bitmap.Width != Math.Max(1, (int)Size.Width) || _bitmap.Height != Math.Max(1, (int)Size.Height)) Resize(Math.Max(1, (int)Size.Width), Math.Max(1, (int)Size.Height));
        root.SetViewport(Size.Width, Size.Height);
        _layout.Layout(root, Size.Width / Math.Max(0.01f, root.Scale), Size.Height / Math.Max(0.01f, root.Scale), StyleSheet);
        using var canvas = new SKCanvas(_bitmap!);
        canvas.Clear(SKColors.Transparent);
        DrawPanel(canvas, root, 0, 0, root.Opacity);
        _bitmap!.PeekPixels().GetPixelSpan().CopyTo(_pixels);
        _dirty = false;
        return _pixels;
    }

    private static void DrawPanel(SKCanvas canvas, Panel panel, float ox, float oy, float opacity)
    {
        var rect = new SKRect(panel.Layout.X + ox, panel.Layout.Y + oy, panel.Layout.Right + ox, panel.Layout.Bottom + oy);
        var alpha = (byte)Math.Clamp(panel.ComputedStyle.Opacity * opacity * 255, 0, 255);
        var background = panel.ComputedStyle.BackgroundColor;
        if (background.A > 0)
        {
            using var paint = new SKPaint { Color = new SKColor(background.R, background.G, background.B, (byte)(background.A * alpha / 255)), IsAntialias = true };
            canvas.DrawRoundRect(rect, panel.ComputedStyle.BorderRadius, panel.ComputedStyle.BorderRadius, paint);
        }
        if (panel.TagName == "text" && !string.IsNullOrEmpty(panel.Text))
        {
            using var paint = new SKPaint { Color = new SKColor(panel.ComputedStyle.Color.R, panel.ComputedStyle.Color.G, panel.ComputedStyle.Color.B, alpha), IsAntialias = true };
            using var font = new SKFont { Size = 16 };
            canvas.DrawText(panel.Text, rect.Left, rect.Top + 16, SKTextAlign.Left, font, paint);
        }
        foreach (var child in panel.Children) DrawPanel(canvas, child, ox, oy, opacity);
    }

    public void MarkDirty() => _dirty = true;
    public void Dispose() => _bitmap?.Dispose();
}
