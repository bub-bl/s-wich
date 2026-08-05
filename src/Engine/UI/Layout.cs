using Facebook.Yoga;

namespace Crowbar.Engine.UI;

/// <summary>Small Yoga-compatible flex layout adapter. The tree is kept independent of the renderer.</summary>
public sealed class YogaLayoutEngine
{
    public int LayoutPasses { get; private set; }

    public void Layout(Panel root, float width, float height, StyleSheet? sheet = null)
    {
        LayoutPasses++;
        ApplyStyles(root, sheet, null);
        var yogaRoot = BuildYogaTree(root, new YogaConfig());
        yogaRoot.Width = root.ComputedStyle.Width ?? width;
        yogaRoot.Height = root.ComputedStyle.Height ?? height;
        yogaRoot.CalculateLayout();
        ReadLayout(root, yogaRoot, 0, 0);
        root.ClearDirty();
    }

    private static void ApplyStyles(Panel panel, StyleSheet? sheet, ComputedStyle? inherited)
    {
        panel.ApplyComputedStyle(sheet?.Compute(panel) ?? new ComputedStyle());
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

    private static YogaNode BuildYogaTree(Panel panel, YogaConfig config)
    {
        var style = panel.ComputedStyle;
        var node = new YogaNode(config)
        {
            FlexDirection = style.FlexDirection.Equals("row", StringComparison.OrdinalIgnoreCase) ? YogaFlexDirection.Row : YogaFlexDirection.Column,
            JustifyContent = ParseJustify(style.JustifyContent),
            AlignItems = ParseAlign(style.AlignItems),
            Display = style.Display.Equals("none", StringComparison.OrdinalIgnoreCase) || !panel.IsVisible ? YogaDisplay.None : YogaDisplay.Flex,
            FlexGrow = style.FlexGrow,
            Width = style.Width ?? YogaValue.Undefined(),
            Height = style.Height ?? YogaValue.Undefined(),
            MinWidth = style.MinWidth ?? YogaValue.Undefined(),
            MaxWidth = style.MaxWidth ?? YogaValue.Undefined(),
            MinHeight = style.MinHeight ?? YogaValue.Undefined(),
            MaxHeight = style.MaxHeight ?? YogaValue.Undefined(),
            PaddingTop = style.PaddingTop,
            PaddingRight = style.PaddingRight,
            PaddingBottom = style.PaddingBottom,
            PaddingLeft = style.PaddingLeft,
            MarginTop = style.MarginTop,
            MarginRight = style.MarginRight,
            MarginBottom = style.MarginBottom,
            MarginLeft = style.MarginLeft,
            Overflow = style.Overflow.Equals("hidden", StringComparison.OrdinalIgnoreCase) ? YogaOverflow.Hidden : YogaOverflow.Visible,
        };
        node.Data = panel;
        if ((panel.TagName.Equals("text", StringComparison.OrdinalIgnoreCase) || panel is TextInput) && !string.IsNullOrEmpty(panel is TextInput input ? input.Value : panel.Text))
        {
            var text = panel is TextInput inputValue ? inputValue.Value : panel.Text;
            node.SetMeasureFunction((_, width, _, _, _) => new YogaSize
            {
                width = Math.Min(width > 0 ? width : float.MaxValue, text.Length * panel.ComputedStyle.FontSize * 0.56f + panel.ComputedStyle.PaddingLeft + panel.ComputedStyle.PaddingRight),
                height = (panel.ComputedStyle.LineHeight > 0 ? panel.ComputedStyle.LineHeight : panel.ComputedStyle.FontSize * 1.25f) + panel.ComputedStyle.PaddingTop + panel.ComputedStyle.PaddingBottom
            });
        }
        for (var i = 0; i < panel.Children.Count; i++)
            node.AddChild(BuildYogaTree(panel.Children[i], config, style, i));
        return node;
    }

    private static YogaNode BuildYogaTree(Panel panel, YogaConfig config, ComputedStyle? parentStyle, int childIndex)
    {
        var node = BuildYogaTree(panel, config);
        if (parentStyle is not null && childIndex > 0)
        {
            if (parentStyle.FlexDirection.Equals("row", StringComparison.OrdinalIgnoreCase)) node.MarginLeft = panel.ComputedStyle.MarginLeft + parentStyle.ColumnGap;
            else node.MarginTop = panel.ComputedStyle.MarginTop + parentStyle.RowGap;
        }
        return node;
    }

    private static void ReadLayout(Panel panel, YogaNode node, float parentX, float parentY)
    {
        panel.Layout = new UiRect(parentX + node.LayoutX, parentY + node.LayoutY, node.LayoutWidth, node.LayoutHeight);
        for (var i = 0; i < panel.Children.Count && i < node.Count; i++) ReadLayout(panel.Children[i], node[i], panel.Layout.X, panel.Layout.Y);
    }

    private static YogaAlign ParseAlign(string value) => value switch
    {
        "center" => YogaAlign.Center,
        "flex-end" => YogaAlign.FlexEnd,
        _ => YogaAlign.Stretch
    };
    private static YogaJustify ParseJustify(string value) => value switch
    {
        "center" => YogaJustify.Center,
        "flex-end" => YogaJustify.FlexEnd,
        "space-between" => YogaJustify.SpaceBetween,
        "space-around" => YogaJustify.SpaceAround,
        _ => YogaJustify.FlexStart
    };
}
