namespace Crowbar.UI;

/// <summary>
/// Parses <c>rgb()</c> and <c>rgba()</c> in both the legacy comma syntax
/// (<c>rgb(255, 0, 128, 0.5)</c>) and the modern space syntax
/// (<c>rgb(255 0 128 / 50%)</c>). Channels accept 0-255 integers or percentages.
/// </summary>
public sealed class RgbColorFormat : ICssColorFormat
{
    public string Name => "rgb";

    public bool TryParse(string input, out UiColor color)
    {
        color = default;
        if (!CssColorFunctionHelpers.TryOpen(input, "rgb", out var body) &&
            !CssColorFunctionHelpers.TryOpen(input, "rgba", out body)) return false;
        if (!CssColorFunctionHelpers.TrySplitChannels(body, out var channels, out var alpha)) return false;
        if (!CssColorFunctionHelpers.TryParseByteChannel(channels[0], out var r) ||
            !CssColorFunctionHelpers.TryParseByteChannel(channels[1], out var g) ||
            !CssColorFunctionHelpers.TryParseByteChannel(channels[2], out var b)) return false;
        byte a = 255;
        if (alpha is not null && !CssColorFunctionHelpers.TryParseAlpha(alpha, out a)) return false;
        color = new UiColor(r, g, b, a);
        return true;
    }
}
