namespace Crowbar.UI;

/// <summary>
/// A single CSS color syntax the engine can parse (hex, named colors,
/// <c>rgb()</c>/<c>rgba()</c>, <c>hsl()</c>/<c>hsla()</c>, <c>hwb()</c>, ...).
/// Register a new implementation in <see cref="CssColors"/> to extend the
/// parser with additional color spaces.
/// </summary>
public interface ICssColorFormat
{
    /// <summary>Stable name of the format, used for diagnostics and deduplication.</summary>
    string Name { get; }

    /// <summary>Attempts to parse a trimmed color value. Returns false when the value is not this format.</summary>
    bool TryParse(string input, out UiColor color);
}
