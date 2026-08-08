namespace Crowbar.UI;

/// <summary>
/// Registry of CSS color formats. <see cref="TryParse"/> runs every registered
/// format in order until one accepts the value. Formats are checked
/// case-insensitively for function names and named colors.
/// </summary>
public static class CssColors
{
    private static readonly List<ICssColorFormat> Formats = [];

    /// <summary>Every registered color format, in registration order.</summary>
    public static IReadOnlyList<ICssColorFormat> All => Formats;

    /// <summary>Registers a color format. Throws when the name is already taken.</summary>
    public static void Register(ICssColorFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (Formats.Any(existing => existing.Name.Equals(format.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A CSS color format named '{format.Name}' is already registered.");
        Formats.Add(format);
    }

    /// <summary>Parses any CSS color supported by the registered formats.</summary>
    public static bool TryParse(string value, out UiColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        foreach (var format in Formats)
            if (format.TryParse(value, out color)) return true;
        return false;
    }

    static CssColors()
    {
        Register(new HexColorFormat());
        Register(new NamedColorFormat());
        Register(new RgbColorFormat());
        Register(new HslColorFormat());
        Register(new HwbColorFormat());
    }
}
