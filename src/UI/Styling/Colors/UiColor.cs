namespace Crowbar.UI;

/// <summary>An 8-bit sRGB color with alpha, the color type used across the UI engine.</summary>
public readonly record struct UiColor(byte R, byte G, byte B, byte A)
{
    public static UiColor Transparent => new(0, 0, 0, 0);
    public static UiColor White => new(255, 255, 255, 255);
    public static UiColor Black => new(0, 0, 0, 255);

    /// <summary>
    /// Parses a CSS color. Delegates to the <see cref="CssColors"/> registry,
    /// so every registered color format (hex, named, rgb, hsl, hwb, ...) is
    /// available here.
    /// </summary>
    public static bool TryParse(string value, out UiColor color) => CssColors.TryParse(value, out color);

    /// <summary>Linear interpolation in sRGB space, used by color transitions.</summary>
    public static UiColor Lerp(UiColor from, UiColor to, float t) => new(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t),
        (byte)Math.Round(from.A + (to.A - from.A) * t));
}
