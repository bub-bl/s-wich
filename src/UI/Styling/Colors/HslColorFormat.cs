namespace Crowbar.UI;

/// <summary>
/// Parses <c>hsl()</c> and <c>hsla()</c> in both the legacy comma syntax
/// (<c>hsl(120, 50%, 50%)</c>) and the modern space syntax
/// (<c>hsl(120deg 50% 50% / 0.5)</c>). Hue accepts any angle unit.
/// </summary>
public sealed class HslColorFormat : ICssColorFormat
{
    public string Name => "hsl";

    public bool TryParse(string input, out UiColor color)
    {
        color = default;
        if (!CssColorFunctionHelpers.TryOpen(input, "hsl", out var body) &&
            !CssColorFunctionHelpers.TryOpen(input, "hsla", out body)) return false;
        if (!CssColorFunctionHelpers.TrySplitChannels(body, out var channels, out var alpha)) return false;
        if (!CssColorFunctionHelpers.TryParseHue(channels[0], out var hue) ||
            !CssColorFunctionHelpers.TryParsePercentOrFraction(channels[1], out var saturation) ||
            !CssColorFunctionHelpers.TryParsePercentOrFraction(channels[2], out var lightness)) return false;
        byte a = 255;
        if (alpha is not null && !CssColorFunctionHelpers.TryParseAlpha(alpha, out a)) return false;
        color = CssColorFunctionHelpers.FromHsl(hue, saturation, lightness, a);
        return true;
    }
}
