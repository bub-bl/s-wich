namespace Crowbar.UI;

/// <summary>
/// Parses the CSS Color 4 <c>hwb()</c> function
/// (<c>hwb(120 20% 40% / 0.5)</c>), an intuitive alternative to HSL based on
/// whiteness and blackness.
/// </summary>
public sealed class HwbColorFormat : ICssColorFormat
{
    public string Name => "hwb";

    public bool TryParse(string input, out UiColor color)
    {
        color = default;
        if (!CssColorFunctionHelpers.TryOpen(input, "hwb", out var body)) return false;
        if (!CssColorFunctionHelpers.TrySplitChannels(body, out var channels, out var alpha)) return false;
        if (!CssColorFunctionHelpers.TryParseHue(channels[0], out var hue) ||
            !CssColorFunctionHelpers.TryParsePercentOrFraction(channels[1], out var whiteness) ||
            !CssColorFunctionHelpers.TryParsePercentOrFraction(channels[2], out var blackness)) return false;
        byte a = 255;
        if (alpha is not null && !CssColorFunctionHelpers.TryParseAlpha(alpha, out a)) return false;
        color = CssColorFunctionHelpers.FromHwb(hue, whiteness, blackness, a);
        return true;
    }
}
