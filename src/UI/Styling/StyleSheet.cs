using System.Text;
using System.Text.RegularExpressions;

namespace Crowbar.UI;

/// <summary>A single CSS rule: a selector, its declarations and its cascade order.</summary>
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
        if (pseudo.Length > 0 &&
            !pseudo.Equals("hover", StringComparison.OrdinalIgnoreCase) &&
            !pseudo.Equals("active", StringComparison.OrdinalIgnoreCase) &&
            !pseudo.Equals("focus", StringComparison.OrdinalIgnoreCase) &&
            !pseudo.Equals("disabled", StringComparison.OrdinalIgnoreCase) &&
            !pseudo.Equals("checked", StringComparison.OrdinalIgnoreCase)) return false;
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
            if (match.Value[0] == '#' && !string.Equals(panel.Id, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                return false;
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
                if (!panel.Attributes.TryGetValue(attrName, out var v) ||
                    !string.Equals(v, attrVal, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }

        return true;
    }
}

/// <summary>
/// An ordered collection of <see cref="StyleRule"/>s. Cascading computes a
/// <see cref="ComputedStyle"/> by applying matching rules in declaration order
/// and finishing with the panel's inline styles. Each declaration is dispatched
/// through the <see cref="CssProperties"/> registry, so custom registered
/// properties cascade automatically.
/// </summary>
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
                var scopedSelector = string.IsNullOrWhiteSpace(scopeId)
                    ? selector.Trim()
                    : ScopeSelector(selector.Trim(), scopeId.Trim());
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
        var sb = new StringBuilder();
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

    /// <summary>
    /// Applies declarations to a computed style. Unknown properties and invalid
    /// values are silently ignored, mirroring CSS semantics.
    /// </summary>
    internal static void Apply(ComputedStyle style, IReadOnlyDictionary<string, string> properties)
    {
        foreach (var (name, value) in properties) CssProperties.TryApply(style, name, value);
    }
}
