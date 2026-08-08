namespace Crowbar.UI;

/// <summary>
/// Registry of every CSS property the styling engine understands. New
/// properties are added by calling <see cref="Register"/>; once registered they
/// flow through <see cref="StyleSheet"/> cascading, change detection and (when
/// animatable) transition interpolation without any further wiring.
/// </summary>
public static class CssProperties
{
    private static readonly Dictionary<string, CssProperty> Registry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every registered property, in registration order.</summary>
    public static IReadOnlyCollection<CssProperty> All => Registry.Values;

    public static bool TryGet(string name, out CssProperty property) => Registry.TryGetValue(name, out property!);

    /// <summary>Registers a property. Throws when the name is already taken.</summary>
    public static void Register(CssProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!Registry.TryAdd(property.Name, property))
            throw new InvalidOperationException($"A CSS property named '{property.Name}' is already registered.");
    }

    /// <summary>Applies a raw CSS declaration to the style. Unknown or invalid values are ignored.</summary>
    public static bool TryApply(ComputedStyle style, string name, string value) =>
        Registry.TryGetValue(name, out var property) && property.TryApply(style, value);

    /// <summary>Restores every property of the style to its default value.</summary>
    public static void Reset(ComputedStyle style)
    {
        foreach (var property in Registry.Values) property.SetValue(style, property.DefaultValue);
    }

    /// <summary>Creates a keyword/string property (display, overflow, ...).</summary>
    public static CssProperty<string> Text(string name, Func<ComputedStyle, string> getter,
        Action<ComputedStyle, string> setter, string defaultValue, bool inherited = false) =>
        new(name, getter, setter, static (string value, out string result) =>
        {
            result = value;
            return true;
        }, defaultValue, inherited, animatable: false, lerper: null);

    /// <summary>Creates a non-nullable numeric property (opacity, font-size, ...).</summary>
    public static CssProperty<float> Number(string name, Func<ComputedStyle, float> getter,
        Action<ComputedStyle, float> setter, float defaultValue, bool inherited = false,
        bool animatable = false, TryParseHandler<float>? parser = null) =>
        new(name, getter, setter, parser ?? CssValueParsers.TryParseNumber, defaultValue, inherited, animatable,
            animatable ? LerpFloat : null);

    /// <summary>Creates a nullable dimension property (width, height, ...).</summary>
    public static CssProperty<float?> Dimension(string name, Func<ComputedStyle, float?> getter,
        Action<ComputedStyle, float?> setter, TryParseHandler<float?>? parser = null) =>
        new(name, getter, setter, parser ?? CssValueParsers.TryParseDimension, null, inherited: false, animatable: false,
            lerper: null);

    /// <summary>Creates a color property (background-color, color, ...).</summary>
    public static CssProperty<UiColor> Color(string name, Func<ComputedStyle, UiColor> getter,
        Action<ComputedStyle, UiColor> setter, UiColor defaultValue, bool inherited = false,
        bool animatable = false) =>
        new(name, getter, setter, static (string value, out UiColor result) => UiColor.TryParse(value, out result),
            defaultValue, inherited, animatable, animatable ? UiColor.Lerp : null);

    private static float LerpFloat(float from, float to, float t) => from + (to - from) * t;

    static CssProperties() => RegisterBuiltIns();

    private static void RegisterBuiltIns()
    {
        // Layout keywords.
        Register(Text("display", s => s.Display, (s, v) => s.Display = v, "flex"));
        Register(Text("flex-direction", s => s.FlexDirection, (s, v) => s.FlexDirection = v, "column"));
        Register(Text("align-items", s => s.AlignItems, (s, v) => s.AlignItems = v, "stretch"));
        Register(Text("justify-content", s => s.JustifyContent, (s, v) => s.JustifyContent = v, "flex-start"));
        Register(Text("overflow", s => s.Overflow, (s, v) => s.Overflow = v, "visible"));
        Register(Text("text-align", s => s.TextAlign, (s, v) => s.TextAlign = v, "left", inherited: true));
        Register(Text("vertical-align", s => s.VerticalAlign, (s, v) => s.VerticalAlign = v, "top", inherited: true));
        Register(Text("box-sizing", s => s.BoxSizing, (s, v) => s.BoxSizing = v, "border-box"));
        Register(Text("transition-property", s => s.TransitionProperty, (s, v) => s.TransitionProperty = v, "none"));
        Register(Text("transition-timing-function", s => s.TransitionTimingFunction, (s, v) =>
            s.TransitionTimingFunction = v, "ease"));

        // Dimensions.
        Register(Dimension("width", s => s.Width, (s, v) => s.Width = v));
        Register(Dimension("height", s => s.Height, (s, v) => s.Height = v));
        Register(Dimension("min-width", s => s.MinWidth, (s, v) => s.MinWidth = v));
        Register(Dimension("max-width", s => s.MaxWidth, (s, v) => s.MaxWidth = v));
        Register(Dimension("min-height", s => s.MinHeight, (s, v) => s.MinHeight = v));
        Register(Dimension("max-height", s => s.MaxHeight, (s, v) => s.MaxHeight = v));

        // Numbers.
        Register(Number("flex-grow", s => s.FlexGrow, (s, v) => s.FlexGrow = v, 0));
        Register(Number("opacity", s => s.Opacity, (s, v) => s.Opacity = v, 1, animatable: true));
        Register(Number("border-radius", s => s.BorderRadius, (s, v) => s.BorderRadius = v, 0, animatable: true));
        Register(Number("font-size", s => s.FontSize, (s, v) => s.FontSize = v, 16, inherited: true,
            parser: CssValueParsers.TryParseLength));
        Register(Number("line-height", s => s.LineHeight, (s, v) => s.LineHeight = v, 0, inherited: true,
            parser: CssValueParsers.TryParseLength));
        Register(Number("transition-duration", s => s.TransitionDuration, (s, v) => s.TransitionDuration = v, 0,
            parser: CssValueParsers.TryParseTime));

        // Box model: shorthand + individual sides.
        Register(new MarginCssProperty());
        Register(Number("margin-top", s => s.MarginTop, (s, v) => s.MarginTop = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("margin-right", s => s.MarginRight, (s, v) => s.MarginRight = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("margin-bottom", s => s.MarginBottom, (s, v) => s.MarginBottom = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("margin-left", s => s.MarginLeft, (s, v) => s.MarginLeft = v, 0, parser: CssValueParsers.TryParseLength));
        Register(new PaddingCssProperty());
        Register(Number("padding-top", s => s.PaddingTop, (s, v) => s.PaddingTop = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("padding-right", s => s.PaddingRight, (s, v) => s.PaddingRight = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("padding-bottom", s => s.PaddingBottom, (s, v) => s.PaddingBottom = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("padding-left", s => s.PaddingLeft, (s, v) => s.PaddingLeft = v, 0, parser: CssValueParsers.TryParseLength));

        // Gap.
        Register(new GapCssProperty());
        Register(Number("row-gap", s => s.RowGap, (s, v) => s.RowGap = v, 0, parser: CssValueParsers.TryParseLength));
        Register(Number("column-gap", s => s.ColumnGap, (s, v) => s.ColumnGap = v, 0, parser: CssValueParsers.TryParseLength));

        // Transitions.
        Register(new TransitionCssProperty());

        // Colors.
        Register(Color("background-color", s => s.BackgroundColor, (s, v) => s.BackgroundColor = v,
            UiColor.Transparent, animatable: true));
        Register(Color("color", s => s.Color, (s, v) => s.Color = v, UiColor.White, inherited: true, animatable: true));
    }

    private sealed class MarginCssProperty : CompoundCssProperty
    {
        public MarginCssProperty() : base("margin")
        {
        }

        public override bool TryApply(ComputedStyle style, string rawValue)
        {
            if (!CssValueParsers.TryParseBox(rawValue, out var box)) return false;
            style.Margin = box.Top;
            style.MarginTop = box.Top;
            style.MarginRight = box.Right;
            style.MarginBottom = box.Bottom;
            style.MarginLeft = box.Left;
            return true;
        }
    }

    private sealed class PaddingCssProperty : CompoundCssProperty
    {
        public PaddingCssProperty() : base("padding")
        {
        }

        public override bool TryApply(ComputedStyle style, string rawValue)
        {
            if (!CssValueParsers.TryParseBox(rawValue, out var box)) return false;
            style.Padding = box.Top;
            style.PaddingTop = box.Top;
            style.PaddingRight = box.Right;
            style.PaddingBottom = box.Bottom;
            style.PaddingLeft = box.Left;
            return true;
        }
    }

    private sealed class GapCssProperty : CompoundCssProperty
    {
        public GapCssProperty() : base("gap")
        {
        }

        public override bool TryApply(ComputedStyle style, string rawValue)
        {
            var parts = rawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is < 1 or > 2) return false;
            if (!CssValueParsers.TryParseLength(parts[0], out var row)) return false;
            var column = parts.Length > 1 && CssValueParsers.TryParseLength(parts[1], out var parsedColumn)
                ? parsedColumn
                : row;
            style.Gap = row;
            style.RowGap = row;
            style.ColumnGap = column;
            return true;
        }
    }

    private sealed class TransitionCssProperty : CompoundCssProperty
    {
        public TransitionCssProperty() : base("transition")
        {
        }

        public override bool TryApply(ComputedStyle style, string rawValue)
        {
            var parts = rawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;
            style.TransitionProperty = parts[0];
            if (parts.Length > 1 && CssValueParsers.TryParseTime(parts[1], out var duration))
                style.TransitionDuration = duration;
            if (parts.Length > 2) style.TransitionTimingFunction = parts[2];
            return true;
        }
    }
}
