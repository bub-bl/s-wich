using Crowbar.UI;

namespace Crowbar.UI.Tests.Layout;

public class LayoutTests
{
    private static YogaLayoutEngine Layout(Panel root, float width, float height, string? css = null)
    {
        var engine = new YogaLayoutEngine();
        var sheet = css is null ? null : StyleSheet.Parse(css);
        engine.Layout(root, width, height, sheet);
        return engine;
    }

    [Fact]
    public void FixedSizedChildLaysOutAtOrigin()
    {
        var root = new ScreenPanel();
        var child = new Panel { TagName = "div" };
        child.AddClass("box");
        root.AddChild(child);

        Layout(root, 320, 200, ".box { width: 100px; height: 40px; }");

        Assert.Equal(100, child.Layout.Width);
        Assert.Equal(40, child.Layout.Height);
    }

    [Fact]
    public void RowDirectionStacksChildrenHorizontally()
    {
        var root = new ScreenPanel();
        root.SetInlineStyle("flex-direction", "row");
        var a = new Panel { TagName = "div" };
        a.AddClass("a");
        var b = new Panel { TagName = "div" };
        b.AddClass("b");
        root.AddChild(a);
        root.AddChild(b);

        Layout(root, 320, 200, ".a { width: 50px; height: 20px; } .b { width: 30px; height: 20px; }");

        Assert.Equal(0, a.Layout.X);
        Assert.Equal(50, b.Layout.X);
    }

    [Fact]
    public void ColumnDirectionStacksChildrenVertically()
    {
        var root = new ScreenPanel();
        var a = new Panel { TagName = "div" };
        var b = new Panel { TagName = "div" };
        root.AddChild(a);
        root.AddChild(b);

        Layout(root, 320, 200, "div { height: 40px; }");

        Assert.Equal(0, a.Layout.Y);
        Assert.Equal(40, b.Layout.Y);
    }

    [Fact]
    public void GapSeparatesRowChildren()
    {
        var root = new ScreenPanel();
        root.SetInlineStyle("flex-direction", "row");
        root.SetInlineStyle("gap", "10px");
        var a = new Panel { TagName = "div" };
        var b = new Panel { TagName = "div" };
        root.AddChild(a);
        root.AddChild(b);

        Layout(root, 320, 200, "div { width: 40px; height: 20px; }");

        Assert.Equal(50, b.Layout.X);
    }

    [Fact]
    public void TextPanelGetsMeasuredSize()
    {
        var root = new ScreenPanel();
        var label = new Label("hello");
        root.AddChild(label);

        Layout(root, 320, 200);

        Assert.True(label.Layout.Width > 0);
        Assert.True(label.Layout.Height > 0);
    }

    [Fact]
    public void LayoutPassesIncrementPerLayout()
    {
        var root = new ScreenPanel();
        root.AddChild(new Panel { TagName = "div" });
        var engine = Layout(root, 320, 200);
        Assert.Equal(1, engine.LayoutPasses);
        engine.Layout(root, 320, 200);
        Assert.Equal(2, engine.LayoutPasses);
    }

    [Fact]
    public void RootRuleAppliesInsideLayout()
    {
        var root = new ScreenPanel { TagName = "root" };
        var child = new Panel { TagName = "div" };
        child.AddClass("box");
        root.AddChild(child);

        Layout(root, 320, 200, "root { width: 300px; } .box { width: 60px; height: 30px; }");

        Assert.Equal(300, root.ComputedStyle.Width);
        Assert.Equal(60, child.Layout.Width);
        Assert.Equal(30, child.Layout.Height);
    }
}
