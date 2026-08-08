using System.Globalization;
using System.Text.RegularExpressions;

namespace Crowbar.Engine.UI;

public sealed class ComputedStyle
{
    public string Display { get; set; } = "flex";
    public string FlexDirection { get; set; } = "column";
    public string AlignItems { get; set; } = "stretch";
    public string JustifyContent { get; set; } = "flex-start";
    public string Overflow { get; set; } = "visible";
    public string TextAlign { get; set; } = "left";
    public string VerticalAlign { get; set; } = "top";
    public string BoxSizing { get; set; } = "border-box";
    public float? Width { get; set; }
    public float? Height { get; set; }
    public float? MinWidth { get; set; }
    public float? MaxWidth { get; set; }
    public float? MinHeight { get; set; }
    public float? MaxHeight { get; set; }
    public float FlexGrow { get; set; }
    public float Gap { get; set; }
    public float RowGap { get; set; }
    public float ColumnGap { get; set; }
    public float Margin { get; set; }
    public float Padding { get; set; }
    public float MarginTop { get; set; }
    public float MarginRight { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }
    public float PaddingTop { get; set; }
    public float PaddingRight { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }
    public float Opacity { get; set; } = 1;
    public float BorderRadius { get; set; }
    public float FontSize { get; set; } = 16;
    public float LineHeight { get; set; }
    public string TransitionProperty { get; set; } = "none";
    public float TransitionDuration { get; set; }
    public string TransitionTimingFunction { get; set; } = "ease";
    public UiColor BackgroundColor { get; set; } = UiColor.Transparent;
    public UiColor Color { get; set; } = UiColor.White;

    public ComputedStyle Clone() => (ComputedStyle)MemberwiseClone();
}

public readonly record struct UiColor(byte R, byte G, byte B, byte A)
{
    public static UiColor Transparent => new(0, 0, 0, 0);
    public static UiColor White => new(255, 255, 255, 255);
    public static UiColor Black => new(0, 0, 0, 255);

    public static bool TryParse(string value, out UiColor color)
    {
        value = value.Trim();
        if (NamedColors.TryGetValue(value, out color)) return true;
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            // #RGB / #RGBA shorthand: each digit is doubled (#333 -> #333333).
            if (hex.Length is 3 or 4)
            {
                hex = string.Concat(hex.SelectMany(c => new[] { c, c }));
            }
            if (hex.Length is 6 or 8 &&
                uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            {
                if (hex.Length == 6) color = new((byte)(n >> 16), (byte)(n >> 8), (byte)n, 255);
                else color = new((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
                return true;
            }
        }
        var match = Regex.Match(value, "^rgba?\\(([^)]+)\\)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var parts = match.Groups[1].Value.Split(',');
            if (parts.Length is 3 or 4 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
            {
                byte a = 255;
                if (parts.Length == 4 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha)) a = (byte)Math.Clamp(alpha * 255, 0, 255);
                color = new(r, g, b, a); return true;
            }
        }
        color = default; return false;
    }

    private static readonly Dictionary<string, UiColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = Transparent,
        ["white"] = White,
        ["black"] = Black,
        ["red"] = new(255, 0, 0, 255),
        ["green"] = new(0, 128, 0, 255),
        ["lime"] = new(0, 255, 0, 255),
        ["blue"] = new(0, 0, 255, 255),
        ["navy"] = new(0, 0, 128, 255),
        ["yellow"] = new(255, 255, 0, 255),
        ["orange"] = new(255, 165, 0, 255),
        ["purple"] = new(128, 0, 128, 255),
        ["magenta"] = new(255, 0, 255, 255),
        ["cyan"] = new(0, 255, 255, 255),
        ["teal"] = new(0, 128, 128, 255),
        ["gray"] = new(128, 128, 128, 255),
        ["grey"] = new(128, 128, 128, 255),
        ["silver"] = new(192, 192, 192, 255),
        ["maroon"] = new(128, 0, 0, 255),
        ["olive"] = new(128, 128, 0, 255),
        ["pink"] = new(255, 192, 203, 255),
        ["brown"] = new(165, 42, 42, 255),
        ["gold"] = new(255, 215, 0, 255)
    };
}

public sealed record StyleRule(string Selector, IReadOnlyDictionary<string, string> Properties, int Order)
{
    public bool Matches(Panel panel)
    {
        var parts = Selector.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !MatchesSimple(parts[^1], panel)) return false;
        var ancestor = panel.Parent;
        for (var i = parts.Length - 2; i >= 0; i--)
        {
            if (parts[i] == ">")
            {
                i--;
                if (i < 0 || ancestor is null || !MatchesSimple(parts[i], ancestor)) return false;
                ancestor = ancestor.Parent;
            }
            else
            {
                while (ancestor is not null && !MatchesSimple(parts[i], ancestor)) ancestor = ancestor.Parent;
                if (ancestor is null) return false;
                ancestor = ancestor.Parent;
            }
        }
        return true;
    }

    private static bool MatchesSimple(string selector, Panel panel)
    {
        var pseudoIndex = selector.IndexOf(':');
        var simple = pseudoIndex >= 0 ? selector[..pseudoIndex] : selector;
        var pseudo = pseudoIndex >= 0 ? selector[(pseudoIndex + 1)..] : string.Empty;
        if (pseudo.Length > 0 && !pseudo.Equals("hover", StringComparison.OrdinalIgnoreCase) && !pseudo.Equals("active", StringComparison.OrdinalIgnoreCase) && !pseudo.Equals("focus", StringComparison.OrdinalIgnoreCase) && !pseudo.Equals("disabled", StringComparison.OrdinalIgnoreCase) && !pseudo.Equals("checked", StringComparison.OrdinalIgnoreCase)) return false;
        if (pseudo.Equals("hover", StringComparison.OrdinalIgnoreCase) && !panel.IsHovered) return false;
        if (pseudo.Equals("active", StringComparison.OrdinalIgnoreCase) && !panel.IsPressed) return false;
        if (pseudo.Equals("focus", StringComparison.OrdinalIgnoreCase) && !panel.IsFocused) return false;
        if (pseudo.Equals("disabled", StringComparison.OrdinalIgnoreCase) && panel.IsEnabled) return false;
        if (pseudo.Equals("checked", StringComparison.OrdinalIgnoreCase) && !panel.IsChecked) return false;
        if (simple == "*") return true;
        var type = simple.StartsWith('*') ? string.Empty : Regex.Match(simple, "^[a-zA-Z][a-zA-Z0-9_-]*").Value;
        if (!string.IsNullOrEmpty(type) && !type.Equals(panel.TagName, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (Match match in Regex.Matches(simple, "[.#]([a-zA-Z0-9_-]+)"))
        {
            if (match.Value[0] == '.' && !panel.Classes.Contains(match.Groups[1].Value)) return false;
            if (match.Value[0] == '#' && !string.Equals(panel.Id, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        foreach (Match match in Regex.Matches(simple, @"\[([a-zA-Z0-9_-]+)(?:=([^\]]+))?\]"))
        {
            var attrName = match.Groups[1].Value;
            var attrVal = match.Groups[2].Success ? match.Groups[2].Value.Trim('"', '\'') : null;
            if (attrVal is null)
            {
                if (!panel.Attributes.ContainsKey(attrName) && !panel.HasScope(attrName)) return false;
            }
            else
            {
                if (!panel.Attributes.TryGetValue(attrName, out var v) || !string.Equals(v, attrVal, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        return true;
    }
}

public sealed class StyleSheet
{
    private readonly List<StyleRule> _rules = [];
    public IReadOnlyList<StyleRule> Rules => _rules;

    public void AddRules(IEnumerable<StyleRule> rules) => _rules.AddRange(rules);
    public void Clear() => _rules.Clear();

    public static StyleSheet Parse(string css, string? scopeId = null)
    {
        var sheet = new StyleSheet();
        var order = 0;
        foreach (Match match in Regex.Matches(css, "(?s)([^{}]+)\\{([^{}]*)\\}"))
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in match.Groups[2].Value.Split(';'))
            {
                var split = declaration.Split(':', 2);
                if (split.Length == 2) properties[split[0].Trim()] = split[1].Trim();
            }
            foreach (var selector in match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var scopedSelector = string.IsNullOrWhiteSpace(scopeId) ? selector.Trim() : ScopeSelector(selector.Trim(), scopeId.Trim());
                sheet._rules.Add(new StyleRule(scopedSelector, properties, order++));
            }
        }
        return sheet;
    }

    public static string ScopeSelector(string selector, string scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId) || string.IsNullOrWhiteSpace(selector)) return selector;
        var scopeAttr = $"[{scopeId}]";
        if (selector.Contains(scopeAttr, StringComparison.OrdinalIgnoreCase)) return selector;

        var deepMatch = Regex.Match(selector, @"::deep|:deep\(([^)]+)\)");
        if (deepMatch.Success)
        {
            var beforeDeep = selector[..deepMatch.Index].TrimEnd();
            var afterDeep = deepMatch.Groups[1].Success
                ? deepMatch.Groups[1].Value
                : selector[(deepMatch.Index + deepMatch.Length)..].TrimStart();

            var scopedBefore = ScopeCompoundSelectors(beforeDeep, scopeAttr);
            return string.IsNullOrWhiteSpace(afterDeep) ? scopedBefore : $"{scopedBefore} {afterDeep.Trim()}";
        }

        return ScopeCompoundSelectors(selector, scopeAttr);
    }

    private static string ScopeCompoundSelectors(string selector, string scopeAttr)
    {
        var parts = Regex.Split(selector, @"(?<=[\s>+~])|(?=[\s>+~])");
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || part == ">" || part == "+" || part == "~")
            {
                sb.Append(part);
                continue;
            }
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.Contains(scopeAttr, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(part);
                continue;
            }

            var pseudoIdx = trimmed.IndexOf(':');
            string scopedPart;
            if (pseudoIdx >= 0)
            {
                scopedPart = trimmed[..pseudoIdx] + scopeAttr + trimmed[pseudoIdx..];
            }
            else
            {
                scopedPart = trimmed + scopeAttr;
            }
            sb.Append(scopedPart);
        }
        return sb.ToString();
    }

    public ComputedStyle Compute(Panel panel)
    {
        var style = new ComputedStyle();
        foreach (var rule in _rules.Where(r => r.Matches(panel)).OrderBy(r => r.Order)) Apply(style, rule.Properties);
        Apply(style, panel.InlineStyle);
        return style;
    }

    internal static void Apply(ComputedStyle style, IReadOnlyDictionary<string, string> properties)
    {
        foreach (var (key, value) in properties)
        {
            if (key.Equals("display", StringComparison.OrdinalIgnoreCase)) style.Display = value;
            else if (key.Equals("flex-direction", StringComparison.OrdinalIgnoreCase)) style.FlexDirection = value;
            else if (key.Equals("align-items", StringComparison.OrdinalIgnoreCase)) style.AlignItems = value;
            else if (key.Equals("justify-content", StringComparison.OrdinalIgnoreCase)) style.JustifyContent = value;
            else if (key.Equals("overflow", StringComparison.OrdinalIgnoreCase)) style.Overflow = value;
            else if (key.Equals("text-align", StringComparison.OrdinalIgnoreCase)) style.TextAlign = value;
            else if (key.Equals("vertical-align", StringComparison.OrdinalIgnoreCase)) style.VerticalAlign = value;
            else if (key.Equals("box-sizing", StringComparison.OrdinalIgnoreCase)) style.BoxSizing = value;
            else if (key.Equals("width", StringComparison.OrdinalIgnoreCase)) style.Width = ParseLength(value);
            else if (key.Equals("height", StringComparison.OrdinalIgnoreCase)) style.Height = ParseLength(value);
            else if (key.Equals("min-width", StringComparison.OrdinalIgnoreCase)) style.MinWidth = ParseLength(value);
            else if (key.Equals("max-width", StringComparison.OrdinalIgnoreCase)) style.MaxWidth = ParseLength(value);
            else if (key.Equals("min-height", StringComparison.OrdinalIgnoreCase)) style.MinHeight = ParseLength(value);
            else if (key.Equals("max-height", StringComparison.OrdinalIgnoreCase)) style.MaxHeight = ParseLength(value);
            else if (key.Equals("margin", StringComparison.OrdinalIgnoreCase)) ApplyBox(value, (t, r, b, l) => { style.Margin = t; style.MarginTop = t; style.MarginRight = r; style.MarginBottom = b; style.MarginLeft = l; });
            else if (key.Equals("margin-top", StringComparison.OrdinalIgnoreCase)) style.MarginTop = ParseLength(value) ?? 0;
            else if (key.Equals("margin-right", StringComparison.OrdinalIgnoreCase)) style.MarginRight = ParseLength(value) ?? 0;
            else if (key.Equals("margin-bottom", StringComparison.OrdinalIgnoreCase)) style.MarginBottom = ParseLength(value) ?? 0;
            else if (key.Equals("margin-left", StringComparison.OrdinalIgnoreCase)) style.MarginLeft = ParseLength(value) ?? 0;
            else if (key.Equals("padding", StringComparison.OrdinalIgnoreCase)) ApplyBox(value, (t, r, b, l) => { style.Padding = t; style.PaddingTop = t; style.PaddingRight = r; style.PaddingBottom = b; style.PaddingLeft = l; });
            else if (key.Equals("padding-top", StringComparison.OrdinalIgnoreCase)) style.PaddingTop = ParseLength(value) ?? 0;
            else if (key.Equals("padding-right", StringComparison.OrdinalIgnoreCase)) style.PaddingRight = ParseLength(value) ?? 0;
            else if (key.Equals("padding-bottom", StringComparison.OrdinalIgnoreCase)) style.PaddingBottom = ParseLength(value) ?? 0;
            else if (key.Equals("padding-left", StringComparison.OrdinalIgnoreCase)) style.PaddingLeft = ParseLength(value) ?? 0;
            else if (key.Equals("gap", StringComparison.OrdinalIgnoreCase)) { style.Gap = ParseLength(value) ?? 0; style.RowGap = style.Gap; style.ColumnGap = style.Gap; }
            else if (key.Equals("row-gap", StringComparison.OrdinalIgnoreCase)) style.RowGap = ParseLength(value) ?? 0;
            else if (key.Equals("column-gap", StringComparison.OrdinalIgnoreCase)) style.ColumnGap = ParseLength(value) ?? 0;
            else if (key.Equals("flex-grow", StringComparison.OrdinalIgnoreCase)) style.FlexGrow = ParseFloat(value);
            else if (key.Equals("opacity", StringComparison.OrdinalIgnoreCase)) style.Opacity = ParseFloat(value);
            else if (key.Equals("border-radius", StringComparison.OrdinalIgnoreCase)) style.BorderRadius = ParseLength(value) ?? 0;
            else if (key.Equals("font-size", StringComparison.OrdinalIgnoreCase)) style.FontSize = ParseLength(value) ?? 16;
            else if (key.Equals("line-height", StringComparison.OrdinalIgnoreCase)) style.LineHeight = ParseLength(value) ?? style.FontSize * 1.25f;
            else if (key.Equals("transition", StringComparison.OrdinalIgnoreCase)) ApplyTransition(style, value);
            else if (key.Equals("transition-property", StringComparison.OrdinalIgnoreCase)) style.TransitionProperty = value;
            else if (key.Equals("transition-duration", StringComparison.OrdinalIgnoreCase)) style.TransitionDuration = ParseTime(value);
            else if (key.Equals("transition-timing-function", StringComparison.OrdinalIgnoreCase)) style.TransitionTimingFunction = value;
            else if (key.Equals("background-color", StringComparison.OrdinalIgnoreCase) && UiColor.TryParse(value, out var bg)) style.BackgroundColor = bg;
            else if (key.Equals("color", StringComparison.OrdinalIgnoreCase) && UiColor.TryParse(value, out var fg)) style.Color = fg;
        }
    }

    private static void ApplyBox(string value, Action<float, float, float, float> apply)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseLength).Select(v => v ?? 0).ToArray();
        if (values.Length == 0) return;
        var top = values[0];
        var right = values.Length > 1 ? values[1] : top;
        var bottom = values.Length > 2 ? values[2] : top;
        var left = values.Length > 3 ? values[3] : right;
        apply(top, right, bottom, left);
    }

    private static void ApplyTransition(ComputedStyle style, string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        style.TransitionProperty = parts[0];
        if (parts.Length > 1) style.TransitionDuration = ParseTime(parts[1]);
        if (parts.Length > 2) style.TransitionTimingFunction = parts[2];
    }

    private static float ParseTime(string value)
    {
        value = value.Trim().ToLowerInvariant();
        var multiplier = value.EndsWith("ms", StringComparison.Ordinal) ? 0.001f : 1f;
        value = value.TrimEnd('m', 's');
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? Math.Max(0, seconds * multiplier) : 0;
    }

    private static float? ParseLength(string value) => float.TryParse(value.Trim().TrimEnd('p', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? Math.Max(0, n) : null;
    private static float ParseFloat(string value) => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
