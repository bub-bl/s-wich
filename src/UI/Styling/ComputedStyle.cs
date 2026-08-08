namespace Crowbar.UI;

/// <summary>
/// The fully computed style of a panel after the CSS cascade, inheritance and
/// transitions have been applied. Values are plain CLR properties; the
/// <see cref="CssProperties"/> registry owns parsing, equality and animation
/// for every property. Layout lengths carry their unit (<see cref="CssLength"/>)
/// so Yoga.Net can resolve percentages, auto margins and content-based sizes.
/// </summary>
public sealed class ComputedStyle
{
    public string Display { get; set; } = "flex";
    public string FlexDirection { get; set; } = "column";
    public string FlexWrap { get; set; } = "nowrap";
    public string AlignItems { get; set; } = "stretch";
    public string AlignContent { get; set; } = "flex-start";
    public string AlignSelf { get; set; } = "auto";
    public string JustifyContent { get; set; } = "flex-start";
    public string JustifyItems { get; set; } = "stretch";
    public string JustifySelf { get; set; } = "auto";
    public string PositionType { get; set; } = "relative";
    public string Direction { get; set; } = "inherit";
    public string Overflow { get; set; } = "visible";
    public string TextAlign { get; set; } = "left";
    public string VerticalAlign { get; set; } = "top";
    public string BoxSizing { get; set; } = "border-box";

    public CssLength Width { get; set; }
    public CssLength Height { get; set; }
    public CssLength MinWidth { get; set; }
    public CssLength MaxWidth { get; set; }
    public CssLength MinHeight { get; set; }
    public CssLength MaxHeight { get; set; }
    public CssLength FlexBasis { get; set; }
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; } = 1;
    public float AspectRatio { get; set; }

    public CssLength Gap { get; set; }
    public CssLength RowGap { get; set; }
    public CssLength ColumnGap { get; set; }

    public CssLength Margin { get; set; }
    public CssLength MarginTop { get; set; }
    public CssLength MarginRight { get; set; }
    public CssLength MarginBottom { get; set; }
    public CssLength MarginLeft { get; set; }

    public CssLength Padding { get; set; }
    public CssLength PaddingTop { get; set; }
    public CssLength PaddingRight { get; set; }
    public CssLength PaddingBottom { get; set; }
    public CssLength PaddingLeft { get; set; }

    public CssLength Border { get; set; }
    public CssLength BorderTop { get; set; }
    public CssLength BorderRight { get; set; }
    public CssLength BorderBottom { get; set; }
    public CssLength BorderLeft { get; set; }

    public CssLength PositionTop { get; set; }
    public CssLength PositionRight { get; set; }
    public CssLength PositionBottom { get; set; }
    public CssLength PositionLeft { get; set; }

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
