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
        var text = panel.TagName == "text" ? panel.Text : panel is TextInput input ? input.Value : string.Empty;
        // An empty focused input still needs a text pass so its caret can be
        // drawn at the beginning of the field.
        if (!string.IsNullOrEmpty(text) || panel is TextInput { IsFocused: true }) DrawText(canvas, panel, rect, text, alpha);
        if (panel.ComputedStyle.Overflow.Equals("hidden", StringComparison.OrdinalIgnoreCase))
        {
            canvas.Save();
            canvas.ClipRect(rect);
            foreach (var child in panel.Children) DrawPanel(canvas, child, ox, oy, opacity);
            canvas.Restore();
        }
        else foreach (var child in panel.Children) DrawPanel(canvas, child, ox, oy, opacity);
    }

    private static void DrawText(SKCanvas canvas, Panel panel, SKRect rect, string text, byte alpha)
    {
        var style = panel.ComputedStyle;
        var left = rect.Left + style.PaddingLeft;
        var top = rect.Top + style.PaddingTop;
        var contentWidth = Math.Max(0, rect.Width - style.PaddingLeft - style.PaddingRight);
        var contentHeight = Math.Max(0, rect.Height - style.PaddingTop - style.PaddingBottom);
        var lineHeight = style.LineHeight > 0 ? style.LineHeight : style.FontSize * 1.25f;

        using var paint = new SKPaint { Color = new SKColor(style.Color.R, style.Color.G, style.Color.B, alpha), IsAntialias = true };
        using var font = new SKFont { Size = style.FontSize };
        var metrics = font.Metrics;
        var lines = WrapText(text, font, contentWidth);
        var blockHeight = lines.Count * lineHeight;
        var y = style.VerticalAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? top + Math.Max(0, (contentHeight - blockHeight) / 2)
            : style.VerticalAlign.Equals("bottom", StringComparison.OrdinalIgnoreCase)
                ? top + Math.Max(0, contentHeight - blockHeight)
                : top;

        foreach (var line in lines)
        {
            var measured = font.MeasureText(line);
            var x = style.TextAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
                ? left + Math.Max(0, (contentWidth - measured) / 2)
                : style.TextAlign.Equals("right", StringComparison.OrdinalIgnoreCase)
                    ? left + Math.Max(0, contentWidth - measured)
                    : left;
            // Skia reçoit une baseline, pas le sommet du texte. La position doit
            // donc tenir compte de la hauteur de la ligne pour que le glyphe soit
            // réellement centré dans un input ou un bouton.
            var baseline = y + lineHeight / 2f - (metrics.Ascent + metrics.Descent) / 2f;
            canvas.DrawText(line, x, baseline, SKTextAlign.Left, font, paint);
            y += lineHeight;
        }

        if (panel is TextInput input && input.IsFocused && input.CaretVisible && lines.Count == 1)
        {
            var caretX = left + font.MeasureText(input.Value[..Math.Clamp(input.CaretIndex, 0, input.Value.Length)]);
            using var caretPaint = new SKPaint { Color = paint.Color, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawLine(caretX, top + 3, caretX, top + Math.Max(font.Size + 3, contentHeight - 3), caretPaint);
        }
    }

    private static List<string> WrapText(string text, SKFont font, float width)
    {
        if (width <= 0 || font.MeasureText(text) <= width) return text.Split('\n').ToList();
        var lines = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
                if (!string.IsNullOrEmpty(current) && font.MeasureText(candidate) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = candidate;
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
        }
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    public void MarkDirty() => _dirty = true;
    public void Dispose() => _bitmap?.Dispose();
}
