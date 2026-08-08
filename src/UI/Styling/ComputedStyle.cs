namespace Crowbar.UI;

/// <summary>
/// The fully computed style of a panel after the CSS cascade, inheritance and
/// transitions have been applied. Values are plain CLR properties; the
/// <see cref="CssProperties"/> registry owns parsing, equality and animation
/// for every property.
/// </summary>
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
