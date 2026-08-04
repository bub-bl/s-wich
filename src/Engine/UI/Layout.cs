using Facebook.Yoga;

namespace Crowbar.Engine.UI;

/// <summary>Small Yoga-compatible flex layout adapter. The tree is kept independent of the renderer.</summary>
public sealed class YogaLayoutEngine
{
    public int LayoutPasses { get; private set; }

    public void Layout(Panel root, float width, float height, StyleSheet? sheet = null)
    {
        LayoutPasses++;
        ApplyStyles(root, sheet);
        var yogaRoot = BuildYogaTree(root, new YogaConfig());
        yogaRoot.Width = root.ComputedStyle.Width ?? width;
        yogaRoot.Height = root.ComputedStyle.Height ?? height;
        yogaRoot.CalculateLayout();
        ReadLayout(root, yogaRoot, 0, 0);
        root.ClearDirty();
    }

    private static void ApplyStyles(Panel panel, StyleSheet? sheet)
    {
        panel.ComputedStyle = sheet?.Compute(panel) ?? new ComputedStyle();
        foreach (var child in panel.Children) ApplyStyles(child, sheet);
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
            Padding = style.Padding,
            Margin = style.Margin,
            Overflow = style.Overflow.Equals("hidden", StringComparison.OrdinalIgnoreCase) ? YogaOverflow.Hidden : YogaOverflow.Visible,
        };
        node.Data = panel;
        foreach (var child in panel.Children) node.AddChild(BuildYogaTree(child, config));
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
