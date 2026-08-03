using Avalonia.Media;

namespace Crowbar.Editor;

/// <summary>
/// Shared colors for the editor surface and controls.
/// </summary>
public static class EditorTheme
{
    public const string WindowBackground = "#121212";
    public const string Surface = "#1E2021";
    public const string SurfaceRaised = "#27272A";
    public const string Border = "#3F3F46";
    public const string BorderSubtle = "#52525B";
    public const string ConsoleBackground = "#121214";
    public const string StatusBackground = "#09090B";
    public const string Overlay = "#CC18181B";
    public const string OverlaySubtle = "#AA18181B";

    public const string AccentBlue = "#60A5FA";
    public const string AccentBlueStrong = "#2563EB";
    public const string AccentOrange = "#D97706";
    public const string IconYellow = "#F5B94C";
    public const string Success = "#22C55E";
    public const string SuccessBright = "#4ADE80";
    public const string Danger = "#EF4444";

    public const string TextPrimary = "#E4E4E7";
    public const string TextBright = "#F4F4F5";
    public const string TextWhite = "#FFFFFF";
    public const string TextMuted = "#A1A1AA";
    public const string TextSubtle = "#71717A";
    public const string TextTitle = "#D4D4D8";
    public const string TextSecondary = "#9CA3AF";
    public const string TextLabel = "#6B7280";
    public const string AxisRed = "#EF4444";
    public const string AxisGreen = "#10B981";
    public const string AxisBlue = "#3B82F6";

    public static readonly IBrush WindowBackgroundBrush = Brush(WindowBackground);
    public static readonly IBrush SurfaceBrush = Brush(Surface);
    public static readonly IBrush SurfaceRaisedBrush = Brush(SurfaceRaised);
    public static readonly IBrush BorderBrush = Brush(Border);
    public static readonly IBrush OverlayBrush = Brush(Overlay);
    public static readonly IBrush OverlaySubtleBrush = Brush(OverlaySubtle);
    public static readonly IBrush AccentBlueBrush = Brush(AccentBlue);
    public static readonly IBrush SuccessBrightBrush = Brush(SuccessBright);
    public static readonly IBrush TextPrimaryBrush = Brush(TextPrimary);
    public static readonly IBrush TextBrightBrush = Brush(TextBright);
    public static readonly IBrush TextWhiteBrush = Brush(TextWhite);
    public static readonly IBrush TextMutedBrush = Brush(TextMuted);
    public static readonly IBrush TextSubtleBrush = Brush(TextSubtle);

    public static IBrush Brush(string color) => Avalonia.Media.Brush.Parse(color);
}
