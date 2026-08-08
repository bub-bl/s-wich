using Crowbar.UI;

namespace Crowbar.UI.Tests.Styling;

public class ColorParsingTests
{
    [Theory]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("#000", 0, 0, 0, 255)]
    [InlineData("#333", 51, 51, 51, 255)]
    [InlineData("#f00", 255, 0, 0, 255)]
    [InlineData("#0f08", 0, 255, 0, 136)]
    [InlineData("#ff0000", 255, 0, 0, 255)]
    [InlineData("#0000ff", 0, 0, 255, 255)]
    [InlineData("#ff000080", 255, 0, 0, 128)]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    public void HexParses(string value, byte r, byte g, byte b, byte a)
    {
        Assert.True(UiColor.TryParse(value, out var color));
        Assert.Equal(new UiColor(r, g, b, a), color);
    }

    [Theory]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#ggg")]
    [InlineData("123456")]
    public void InvalidHexIsRejected(string value) => Assert.False(UiColor.TryParse(value, out _));

    [Theory]
    [InlineData("red", 255, 0, 0, 255)]
    [InlineData("lime", 0, 255, 0, 255)]
    [InlineData("blue", 0, 0, 255, 255)]
    [InlineData("transparent", 0, 0, 0, 0)]
    [InlineData("white", 255, 255, 255, 255)]
    [InlineData("black", 0, 0, 0, 255)]
    [InlineData("rebeccapurple", 102, 51, 153, 255)]
    [InlineData("lightseagreen", 32, 178, 170, 255)]
    [InlineData("slategray", 112, 128, 144, 255)]
    [InlineData("RED", 255, 0, 0, 255)]
    [InlineData("RebeccaPurple", 102, 51, 153, 255)]
    public void NamedColorsParse(string name, byte r, byte g, byte b, byte a)
    {
        Assert.True(UiColor.TryParse(name, out var color));
        Assert.Equal(new UiColor(r, g, b, a), color);
    }

    [Fact]
    public void AllCssNamedColorsResolve()
    {
        // Spot-check a representative slice of the standard table.
        var expected = new Dictionary<string, UiColor>
        {
            ["aliceblue"] = new(240, 248, 255, 255),
            ["goldenrod"] = new(218, 165, 32, 255),
            ["mediumvioletred"] = new(199, 21, 133, 255),
            ["navajowhite"] = new(255, 222, 173, 255),
            ["olivedrab"] = new(107, 142, 35, 255),
            ["papayawhip"] = new(255, 239, 213, 255),
            ["thistle"] = new(216, 191, 216, 255),
            ["yellowgreen"] = new(154, 205, 50, 255),
            ["cornsilk"] = new(255, 248, 220, 255),
            ["darkturquoise"] = new(0, 206, 209, 255)
        };
        foreach (var (name, expectedColor) in expected)
        {
            Assert.True(UiColor.TryParse(name, out var actual), $"'{name}' should parse");
            Assert.Equal(expectedColor, actual);
        }
    }

    [Fact]
    public void UnknownNameIsRejected() => Assert.False(UiColor.TryParse("notacolor", out _));

    [Theory]
    [InlineData("rgb(255, 0, 128)", 255, 0, 128, 255)]
    [InlineData("rgb(0, 0, 0)", 0, 0, 0, 255)]
    [InlineData("rgba(255, 0, 128, 0.5)", 255, 0, 128, 128)]
    [InlineData("rgba(255, 0, 128, 50%)", 255, 0, 128, 128)]
    [InlineData("rgb(100%, 0%, 50%)", 255, 0, 128, 255)]
    [InlineData("rgb(255 0 128)", 255, 0, 128, 255)]
    [InlineData("rgb(255 0 128 / 0.5)", 255, 0, 128, 128)]
    [InlineData("rgb(255 0 128 / 50%)", 255, 0, 128, 128)]
    [InlineData("RGB(255,0,0)", 255, 0, 0, 255)]
    [InlineData("rgba(255 0 128 / 0.25)", 255, 0, 128, 64)]
    public void RgbVariantsParse(string value, byte r, byte g, byte b, byte a)
    {
        Assert.True(UiColor.TryParse(value, out var color));
        Assert.Equal(new UiColor(r, g, b, a), color);
    }

    [Theory]
    [InlineData("rgb(1, 2)")] // missing channel
    [InlineData("rgb(1 2 3 4)")] // too many space-separated channels
    [InlineData("rgb()")]
    [InlineData("rgb(255, 0)")]
    public void InvalidRgbIsRejected(string value) => Assert.False(UiColor.TryParse(value, out _));

    [Fact]
    public void OutOfRangeRgbChannelsClampLikeCss()
    {
        // Per CSS, out-of-range channels are clamped rather than rejected.
        Assert.True(UiColor.TryParse("rgb(300, 0, 0)", out var color));
        Assert.Equal(new UiColor(255, 0, 0, 255), color);
    }

    [Theory]
    [InlineData("hsl(0, 100%, 50%)", 255, 0, 0, 255)]
    [InlineData("hsl(120, 100%, 50%)", 0, 255, 0, 255)]
    [InlineData("hsl(240, 100%, 50%)", 0, 0, 255, 255)]
    [InlineData("hsl(0, 0%, 50%)", 128, 128, 128, 255)]
    [InlineData("hsl(0, 0%, 0%)", 0, 0, 0, 255)]
    [InlineData("hsl(0, 0%, 100%)", 255, 255, 255, 255)]
    [InlineData("hsla(120, 50%, 50%, 0.5)", 64, 191, 64, 128)]
    [InlineData("hsl(120 50% 50%)", 64, 191, 64, 255)]
    [InlineData("hsl(120deg 50% 50% / 50%)", 64, 191, 64, 128)]
    [InlineData("hsl(0.5turn 100% 50%)", 0, 255, 255, 255)]
    [InlineData("HSL(240, 100%, 50%)", 0, 0, 255, 255)]
    public void HslVariantsParse(string value, byte r, byte g, byte b, byte a)
    {
        Assert.True(UiColor.TryParse(value, out var color));
        Assert.Equal(new UiColor(r, g, b, a), color);
    }

    [Theory]
    [InlineData("hsl(0, 100%)")]
    [InlineData("hsl(notahue, 100%, 50%)")]
    [InlineData("hsl()")]
    public void InvalidHslIsRejected(string value) => Assert.False(UiColor.TryParse(value, out _));

    [Theory]
    [InlineData("hwb(0 0% 0%)", 255, 0, 0, 255)]
    [InlineData("hwb(120 0% 0%)", 0, 255, 0, 255)]
    [InlineData("hwb(0 100% 0%)", 255, 255, 255, 255)]
    [InlineData("hwb(0 0% 100%)", 0, 0, 0, 255)]
    [InlineData("hwb(120 20% 40% / 0.5)", 61, 143, 61, 128)]
    public void HwbParses(string value, byte r, byte g, byte b, byte a)
    {
        Assert.True(UiColor.TryParse(value, out var color));
        Assert.Equal(new UiColor(r, g, b, a), color);
    }

    [Fact]
    public void WhitespaceIsTrimmed() => Assert.True(UiColor.TryParse("  #ff0000  ", out _));

    [Fact]
    public void EmptyAndNullLikeValuesAreRejected()
    {
        Assert.False(UiColor.TryParse("", out _));
        Assert.False(UiColor.TryParse("   ", out _));
    }

    [Fact]
    public void CustomColorFormatCanBeRegistered()
    {
        // The registry is the extension point: a new syntax plugs in with a
        // single ICssColorFormat implementation.
        var custom = new CustomColorFormat();
        CssColors.Register(custom);
        try
        {
            Assert.True(UiColor.TryParse("custom-42", out var color));
            Assert.Equal(new UiColor(42, 0, 0, 255), color);
        }
        finally
        {
            Assert.Contains(custom, CssColors.All);
        }
    }

    [Fact]
    public void DuplicateColorFormatNameThrows()
    {
        Assert.Throws<InvalidOperationException>(() => CssColors.Register(new CustomColorFormat("hex")));
    }

    private sealed class CustomColorFormat : ICssColorFormat
    {
        public CustomColorFormat(string name = "custom") => Name = name;

        public string Name { get; }

        public bool TryParse(string input, out UiColor color)
        {
            if (!input.StartsWith("custom-", StringComparison.OrdinalIgnoreCase) ||
                !byte.TryParse(input[7..], out var value))
            {
                color = default;
                return false;
            }

            color = new UiColor(value, 0, 0, 255);
            return true;
        }
    }
}
