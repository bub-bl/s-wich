using SkiaSharp;

namespace Crowbar.UI;

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
        // Children are drawn at the scrolled position (content coordinates minus
        // the scroll offset) and clipped to the padding box when the panel clips
        // its content (overflow hidden/scroll/auto/clip).
        if (panel.ClipsContent)
        {
            canvas.Save();
            var clip = new SKRect(
                rect.Left + panel.LayoutBorder.Left,
                rect.Top + panel.LayoutBorder.Top,
                rect.Right - panel.LayoutBorder.Right,
                rect.Bottom - panel.LayoutBorder.Bottom);
            canvas.ClipRect(clip);
            foreach (var child in panel.Children) DrawPanel(canvas, child, ox - panel.ScrollX, oy - panel.ScrollY, opacity);
            canvas.Restore();
        }
        else foreach (var child in panel.Children) DrawPanel(canvas, child, ox - panel.ScrollX, oy - panel.ScrollY, opacity);
        // Scrollbars overlay the content edge and stay visible regardless of the
        // scroll position, so they are drawn after restoring the clip.
        DrawScrollBars(canvas, panel, ox, oy);
    }

    private static void DrawScrollBars(SKCanvas canvas, Panel panel, float ox, float oy)
    {
        // Scrollbar look is customizable per element through CSS
        // (scrollbar-color/width/radius); the values flow from ComputedStyle.
        var style = panel.ComputedStyle;
        var radius = Math.Min(style.ScrollbarRadius, panel.ScrollbarThickness / 2f);
        var trackColor = new SKColor(style.ScrollbarTrackColor.R, style.ScrollbarTrackColor.G, style.ScrollbarTrackColor.B, style.ScrollbarTrackColor.A);
        var thumbColor = new SKColor(style.ScrollbarThumbColor.R, style.ScrollbarThumbColor.G, style.ScrollbarThumbColor.B, style.ScrollbarThumbColor.A);
        if (ScrollBars.ShouldShowVertical(panel))
        {
            var track = ScrollBars.VerticalTrack(panel);
            using var trackPaint = new SKPaint { Color = trackColor, IsAntialias = true };
            canvas.DrawRoundRect(track.X + ox, track.Y + oy, track.Width, track.Height, radius, radius, trackPaint);
            var thumb = ScrollBars.VerticalThumb(panel);
            using var thumbPaint = new SKPaint { Color = thumbColor, IsAntialias = true };
            canvas.DrawRoundRect(thumb.X + ox, thumb.Y + oy, thumb.Width, thumb.Height, radius, radius, thumbPaint);
        }
        if (ScrollBars.ShouldShowHorizontal(panel))
        {
            var track = ScrollBars.HorizontalTrack(panel);
            using var trackPaint = new SKPaint { Color = trackColor, IsAntialias = true };
            canvas.DrawRoundRect(track.X + ox, track.Y + oy, track.Width, track.Height, radius, radius, trackPaint);
            var thumb = ScrollBars.HorizontalThumb(panel);
            using var thumbPaint = new SKPaint { Color = thumbColor, IsAntialias = true };
            canvas.DrawRoundRect(thumb.X + ox, thumb.Y + oy, thumb.Width, thumb.Height, radius, radius, thumbPaint);
        }
    }

    private static void DrawText(SKCanvas canvas, Panel panel, SKRect rect, string text, byte alpha)
    {
        var style = panel.ComputedStyle;
        var padding = panel.LayoutPadding;
        var left = rect.Left + padding.Left;
        var top = rect.Top + padding.Top;
        var contentWidth = Math.Max(0, rect.Width - padding.Left - padding.Right);
        var contentHeight = Math.Max(0, rect.Height - padding.Top - padding.Bottom);
        var lineHeight = style.LineHeight > 0 ? style.LineHeight : style.FontSize * 1.25f;

        using var paint = new SKPaint { Color = new SKColor(style.Color.R, style.Color.G, style.Color.B, alpha), IsAntialias = true };
        using var font = new SKFont { Size = style.FontSize };
        var metrics = font.Metrics;
        var lines = panel is TextInput ? [text] : WrapText(text, font, contentWidth);
        var blockHeight = lines.Count * lineHeight;
        var y = style.VerticalAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? top + Math.Max(0, (contentHeight - blockHeight) / 2)
            : style.VerticalAlign.Equals("bottom", StringComparison.OrdinalIgnoreCase)
                ? top + Math.Max(0, contentHeight - blockHeight)
                : top;

        float firstLineX = left;

        foreach (var line in lines)
        {
            var measured = font.MeasureText(line);
            var x = style.TextAlign.Equals("center", StringComparison.OrdinalIgnoreCase)
                ? left + Math.Max(0, (contentWidth - measured) / 2)
                : style.TextAlign.Equals("right", StringComparison.OrdinalIgnoreCase)
                    ? left + Math.Max(0, contentWidth - measured)
                    : left;
            firstLineX = x;
            // Skia reçoit une baseline, pas le sommet du texte. La position doit
            // donc tenir compte de la hauteur de la ligne pour que le glyphe soit
            // réellement centré dans un input ou un bouton.
            var baseline = y + lineHeight / 2f - (metrics.Ascent + metrics.Descent) / 2f;
            if (panel is TextInput selectionInput && selectionInput.HasSelection && line == text)
            {
                var selectionStart = Math.Min(selectionInput.SelectionStart, selectionInput.SelectionEnd);
                var selectionEnd = Math.Max(selectionInput.SelectionStart, selectionInput.SelectionEnd);
                var selectionLeft = x + font.MeasureText(text[..selectionStart]);
                var selectionRight = x + font.MeasureText(text[..selectionEnd]);
                using var selectionPaint = new SKPaint { Color = new SKColor(50, 120, 220, alpha), IsAntialias = true };
                canvas.DrawRect(new SKRect(selectionLeft, y, selectionRight, y + lineHeight), selectionPaint);
            }
            canvas.DrawText(line, x, baseline, SKTextAlign.Left, font, paint);
            y += lineHeight;
        }

        if (panel is TextInput input && input.IsFocused && input.CaretVisible && lines.Count == 1)
        {
            var caretX = firstLineX + font.MeasureText(input.Value[..Math.Clamp(input.CaretIndex, 0, input.Value.Length)]);
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
            foreach (var word in rawLine.Split(' '))
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
