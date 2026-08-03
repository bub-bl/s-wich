using Avalonia.Controls;

namespace Crowbar.Editor.Tools;

/// <summary>
/// Base class for editor windows authored entirely in C#.
/// Tool authors own the complete Avalonia window and its content.
/// </summary>
public abstract class EditorWindow : Window
{
    protected EditorWindow(EditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    protected EditorContext Context { get; }
}
