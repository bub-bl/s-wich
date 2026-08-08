using Facebook.Yoga;
using SkiaSharp;

namespace Crowbar.UI;

/// <summary>
/// Small Yoga-compatible flex layout adapter backed by Yoga.Net (the maintained
/// C# port of Meta's Yoga, namespace Facebook.Yoga). The tree is rebuilt on
/// every pass and kept independent of the renderer.
/// </summary>
public sealed class YogaLayoutEngine
{
    public int LayoutPasses { get; private set; }

    public void Layout(Panel root, float width, float height, StyleSheet? sheet = null)
    {
        LayoutPasses++;
        ApplyStyles(root, sheet, null);
        var rootWidth = root.ComputedStyle.Width ?? width;
        var rootHeight = root.ComputedStyle.Height ?? height;
        var yogaRoot = BuildYogaTree(root);
        yogaRoot.Style.SetDimension(Dimension.Width, StyleSizeLength.Points(rootWidth));
        yogaRoot.Style.SetDimension(Dimension.Height, StyleSizeLength.Points(rootHeight));
        LayoutAlgorithm.CalculateLayout(yogaRoot, rootWidth, rootHeight, Direction.LTR);
        ReadLayout(root, yogaRoot, 0, 0);
        root.ClearDirty();
    }

    private static void ApplyStyles(Panel panel, StyleSheet? sheet, ComputedStyle? inherited)
    {
        ComputedStyle computed;
        if (sheet is not null)
        {
            computed = sheet.Compute(panel);
        }
        else
        {
            // Without a global sheet the panel's inline styles still apply.
            computed = new ComputedStyle();
            StyleSheet.Apply(computed, panel.InlineStyle);
        }

        panel.ApplyComputedStyle(computed);
        if (inherited is not null)
        {
            if (panel.ComputedStyle.Color == UiColor.White) panel.ComputedStyle.Color = inherited.Color;
            panel.ComputedStyle.Opacity *= inherited.Opacity;
            // Text is rendered by the leaf label/input inside controls such as
            // Button. Carry the inherited text metrics down so the leaf uses
            // the same alignment and line box as its parent.
            if (panel.ComputedStyle.TextAlign == "left") panel.ComputedStyle.TextAlign = inherited.TextAlign;
            if (panel.ComputedStyle.VerticalAlign == "top") panel.ComputedStyle.VerticalAlign = inherited.VerticalAlign;
            if (Math.Abs(panel.ComputedStyle.FontSize - 16) < 0.0001f) panel.ComputedStyle.FontSize = inherited.FontSize;
            if (panel.ComputedStyle.LineHeight == 0) panel.ComputedStyle.LineHeight = inherited.LineHeight;
        }
        foreach (var child in panel.Children) ApplyStyles(child, sheet, panel.ComputedStyle);
    }

    private static Node BuildYogaTree(Panel panel)
    {
        var style = panel.ComputedStyle;
        var node = new Node(Config.Default)
        {
            Style =
            {
                FlexDirection = style.FlexDirection.Equals("row", StringComparison.OrdinalIgnoreCase) ? FlexDirection.Row : FlexDirection.Column,
                JustifyContent = ParseJustify(style.JustifyContent),
                AlignItems = ParseAlign(style.AlignItems),
                Display = style.Display.Equals("none", StringComparison.OrdinalIgnoreCase) || !panel.IsVisible ? Display.None : Display.Flex,
                FlexGrow = new FloatOptional(style.FlexGrow),
                Overflow = style.Overflow.Equals("hidden", StringComparison.OrdinalIgnoreCase) ? Overflow.Hidden : Overflow.Visible,
                BoxSizing = style.BoxSizing.Equals("content-box", StringComparison.OrdinalIgnoreCase) ? BoxSizing.ContentBox : BoxSizing.BorderBox,
            }
        };
        node.SetContext(panel);

        node.Style.SetDimension(Dimension.Width, ToSize(style.Width));
        node.Style.SetDimension(Dimension.Height, ToSize(style.Height));
        node.Style.SetMinDimension(Dimension.Width, ToSize(style.MinWidth));
        node.Style.SetMaxDimension(Dimension.Width, ToSize(style.MaxWidth));
        node.Style.SetMinDimension(Dimension.Height, ToSize(style.MinHeight));
        node.Style.SetMaxDimension(Dimension.Height, ToSize(style.MaxHeight));

        SetPadding(node, Edge.Top, style.PaddingTop);
        SetPadding(node, Edge.Right, style.PaddingRight);
        SetPadding(node, Edge.Bottom, style.PaddingBottom);
        SetPadding(node, Edge.Left, style.PaddingLeft);
        SetMargin(node, Edge.Top, style.MarginTop);
        SetMargin(node, Edge.Right, style.MarginRight);
        SetMargin(node, Edge.Bottom, style.MarginBottom);
        SetMargin(node, Edge.Left, style.MarginLeft);

        // Yoga.Net supports gap natively (Facebook.Yoga had to fake it through
        // child margins), so the row/column gaps map straight to gutters.
        if (style.ColumnGap != 0) node.Style.SetGap(Gutter.Column, StyleLength.Points(style.ColumnGap));
        if (style.RowGap != 0) node.Style.SetGap(Gutter.Row, StyleLength.Points(style.RowGap));

        if ((panel.TagName.Equals("text", StringComparison.OrdinalIgnoreCase) || panel is TextInput) && !string.IsNullOrEmpty(panel is TextInput input ? input.Value : panel.Text))
        {
            var text = panel is TextInput inputValue ? inputValue.Value : panel.Text;
            var fontSize = style.FontSize;
            var lineHeight = style.LineHeight > 0 ? style.LineHeight : style.FontSize * 1.25f;
            // Yoga treats the measure result as the content box and adds the
            // node's padding/border around it, so the callback must only size
            // the text itself (adding padding here double-counted it). The text
            // is measured with the same Skia font as the renderer so the layout
            // box matches the drawn glyphs.
            var textWidth = 0f;
            using (var font = new SKFont { Size = fontSize })
            {
                foreach (var line in text.Split('\n'))
                    textWidth = Math.Max(textWidth, font.MeasureText(line));
            }
            node.SetMeasureFunc((_, availableWidth, _, _, _) => new YGSize
            {
                Width = Math.Min(availableWidth > 0 ? availableWidth : float.MaxValue, textWidth),
                Height = lineHeight
            });
        }
        for (var i = 0; i < panel.Children.Count; i++)
        {
            var child = BuildYogaTree(panel.Children[i]);
            node.InsertChild(child, (nuint)i);
            child.SetOwner(node);
        }
        return node;
    }

    private static void ReadLayout(Panel panel, Node node, float parentX, float parentY)
    {
        var layout = node.Layout;
        panel.Layout = new UiRect(
            parentX + layout.Position(PhysicalEdge.Left),
            parentY + layout.Position(PhysicalEdge.Top),
            layout.Dimension(Dimension.Width),
            layout.Dimension(Dimension.Height));
        for (var i = 0; i < panel.Children.Count && i < (int)node.GetChildCount(); i++)
            ReadLayout(panel.Children[i], node.GetChild((nuint)i)!, panel.Layout.X, panel.Layout.Y);
    }

    private static StyleSizeLength ToSize(float? value) => value is float v ? StyleSizeLength.Points(v) : StyleSizeLength.Undefined();

    private static void SetPadding(Node node, Edge edge, float value)
    {
        if (value != 0) node.Style.SetPadding(edge, StyleLength.Points(value));
    }

    private static void SetMargin(Node node, Edge edge, float value)
    {
        if (value != 0) node.Style.SetMargin(edge, StyleLength.Points(value));
    }

    private static Align ParseAlign(string value) => value switch
    {
        "auto" => Align.Auto,
        "flex-start" => Align.FlexStart,
        "center" => Align.Center,
        "flex-end" => Align.FlexEnd,
        "stretch" => Align.Stretch,
        "baseline" => Align.Baseline,
        "space-between" => Align.SpaceBetween,
        "space-around" => Align.SpaceAround,
        "space-evenly" => Align.SpaceEvenly,
        "start" => Align.Start,
        "end" => Align.End,
        _ => Align.Stretch
    };
    private static Justify ParseJustify(string value) => value switch
    {
        "auto" => Justify.Auto,
        "flex-start" => Justify.FlexStart,
        "center" => Justify.Center,
        "flex-end" => Justify.FlexEnd,
        "stretch" => Justify.Stretch,
        "space-between" => Justify.SpaceBetween,
        "space-around" => Justify.SpaceAround,
        "space-evenly" => Justify.SpaceEvenly,
        "start" => Justify.Start,
        "end" => Justify.End,
        _ => Justify.FlexStart
    };
}
