using System.Globalization;

namespace Crowbar.UI;

/// <summary>Parses hexadecimal colors: <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> and <c>#RRGGBBAA</c>.</summary>
public sealed class HexColorFormat : ICssColorFormat
{
    public string Name => "hex";

    public bool TryParse(string input, out UiColor color)
    {
        color = default;
        if (!input.StartsWith('#')) return false;
        var hex = input[1..];
        // #RGB / #RGBA shorthand: each digit is doubled (#333 -> #333333).
        if (hex.Length is 3 or 4) hex = string.Concat(hex.SelectMany(c => new[] { c, c }));
        if (hex.Length is not (6 or 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return false;
        color = hex.Length == 6
            ? new UiColor((byte)(n >> 16), (byte)(n >> 8), (byte)n, 255)
            : new UiColor((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
        return true;
    }
}
