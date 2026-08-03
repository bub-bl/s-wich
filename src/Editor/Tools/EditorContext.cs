using Crowbar.Engine;

namespace Crowbar.Editor.Tools;

/// <summary>
/// Services and state exposed to editor tools.
/// Keep this type deliberately small: it is the public surface available to tool authors.
/// </summary>
public sealed class EditorContext
{
    public required Scene Scene { get; init; }

    public required EditorWindowManager Windows { get; init; }
}
