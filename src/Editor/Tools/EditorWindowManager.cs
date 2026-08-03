using Avalonia.Controls;

namespace Crowbar.Editor.Tools;

/// <summary>
/// Owns the lifetime of editor tool windows.
/// </summary>
public sealed class EditorWindowManager
{
    private readonly Window _owner;
    private readonly HashSet<EditorWindow> _windows = [];

    public EditorWindowManager(Window owner)
    {
        _owner = owner;
    }

    public void Open(EditorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.IsVisible)
        {
            window.Activate();
            return;
        }

        _windows.Add(window);
        window.Closed += OnWindowClosed;
        window.Show(_owner);
    }

    public void CloseAll()
    {
        foreach (EditorWindow window in _windows.ToArray())
        {
            if (window.IsVisible)
                window.Close();
        }

        _windows.Clear();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is EditorWindow window)
        {
            window.Closed -= OnWindowClosed;
            _windows.Remove(window);
        }
    }
}
