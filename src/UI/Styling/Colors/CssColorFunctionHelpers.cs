using System.Globalization;

namespace Crowbar.UI;

/// <summary>
/// Shared parsing helpers for function-style color formats
/// (<c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c>...): they handle both the legacy
/// comma syntax and the modern space-separated syntax with an optional slash
/// alpha component.
/// </summary>
internal static class CssColorFunctionHelpers
{
    /// <summary>Checks that <paramref name="input"/> is exactly <c>functionName(...)</c> and returns the body.</summary>
    public static bool TryOpen(string input, string functionName, out string body)
    {
        body = string.Empty;
        var open = input.IndexOf('(');
        if (open <= 0 || !input.EndsWith(')')) return false;
        if (!input[..open].Trim().Equals(functionName, StringComparison.OrdinalIgnoreCase)) return false;
        body = input[(open + 1)..^1].Trim();
        return body.Length > 0;
    }

    /// <summary>
    /// Splits a function body into its 3 color channels plus an optional alpha.
    /// Supports both <c>r, g, b, a</c> (legacy) and <c>r g b / a</c> (modern).
    /// </summary>
    public static bool TrySplitChannels(string body, out string[] channels, out string? alpha)
    {
        channels = [];
        alpha = null;
        if (body.Contains(','))
        {
            var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is < 3 or > 4) return false;
            channels = [parts[0], parts[1], parts[2]];
            if (parts.Length == 4) alpha = parts[3];
            return true;
        }

        var slash = body.IndexOf('/');
        var colorPart = (slash >= 0 ? body[..slash] : body).Trim();
        channels = colorPart.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (channels.Length != 3) return false;
        if (slash >= 0)
        {
            alpha = body[(slash + 1)..].Trim();
            if (alpha.Length == 0) return false;
        }

        return true;
    }

    /// <summary>Parses an 8-bit channel given as a 0-255 number or a percentage.</summary>
    public static bool TryParseByteChannel(string token, out byte value)
    {
        value = 0;
        token = token.Trim();
        var isPercent = token.EndsWith('%');
        if (isPercent) token = token[..^1];
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        value = isPercent
            ? (byte)Math.Clamp(Math.Round(number * 2.55f), 0, 255)
            : (byte)Math.Clamp(Math.Round(number), 0, 255);
        return true;
    }

    /// <summary>Parses an alpha channel given as a 0-1 number or a percentage.</summary>
    public static bool TryParseAlpha(string token, out byte alpha)
    {
        alpha = 0;
        token = token.Trim();
        var isPercent = token.EndsWith('%');
        if (isPercent) token = token[..^1];
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        alpha = (byte)Math.Clamp(Math.Round(number * (isPercent ? 2.55f : 255f)), 0, 255);
        return true;
    }

    /// <summary>Parses a saturation/lightness-like value: percentage or a 0-1 fraction.</summary>
    public static bool TryParsePercentOrFraction(string token, out float value)
    {
        value = 0;
        token = token.Trim();
        var isPercent = token.EndsWith('%');
        if (isPercent) token = token[..^1];
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        value = isPercent ? number / 100f : number <= 1 ? number : number / 100f;
        value = Math.Clamp(value, 0, 1);
        return true;
    }

    /// <summary>Parses a hue: an angle with an optional <c>deg</c>/<c>grad</c>/<c>rad</c>/<c>turn</c> unit.</summary>
    public static bool TryParseHue(string token, out float degrees)
    {
        degrees = 0;
        token = token.Trim();
        float multiplier = 1;
        if (token.EndsWith("turn", StringComparison.OrdinalIgnoreCase)) { multiplier = 360f; token = token[..^4]; }
        else if (token.EndsWith("grad", StringComparison.OrdinalIgnoreCase)) { multiplier = 0.9f; token = token[..^4]; }
        else if (token.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) { multiplier = 180f / MathF.PI; token = token[..^3]; }
        else if (token.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) token = token[..^3];
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        degrees = ((number * multiplier) % 360f + 360f) % 360f;
        return true;
    }

    /// <summary>Converts HSL (hue in degrees, s/l as 0-1) to sRGB.</summary>
    public static UiColor FromHsl(float hue, float saturation, float lightness, byte alpha)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);
        if (saturation == 0)
        {
            var gray = (byte)Math.Round(lightness * 255);
            return new UiColor(gray, gray, gray, alpha);
        }

        var q = lightness < 0.5f ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        byte Channel(float t)
        {
            t = (t % 1 + 1) % 1;
            if (t < 1f / 6f) return (byte)Math.Round((p + (q - p) * 6 * t) * 255);
            if (t < 0.5f) return (byte)Math.Round(q * 255);
            if (t < 2f / 3f) return (byte)Math.Round((p + (q - p) * (2f / 3f - t) * 6) * 255);
            return (byte)Math.Round(p * 255);
        }

        var h = hue / 360f;
        return new UiColor(Channel(h + 1f / 3f), Channel(h), Channel(h - 1f / 3f), alpha);
    }

    /// <summary>Converts HWB (hue in degrees, w/b as 0-1) to sRGB.</summary>
    public static UiColor FromHwb(float hue, float whiteness, float blackness, byte alpha)
    {
        whiteness = Math.Clamp(whiteness, 0, 1);
        blackness = Math.Clamp(blackness, 0, 1);
        if (whiteness + blackness >= 1)
        {
            var gray = (byte)Math.Round(whiteness / (whiteness + blackness) * 255);
            return new UiColor(gray, gray, gray, alpha);
        }

        var saturation = 1 - whiteness - blackness;
        var lightness = whiteness + saturation / 2f;
        return FromHsl(hue, saturation, lightness, alpha);
    }
}
