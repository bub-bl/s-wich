using Avalonia.Controls;
using Avalonia.Threading;

namespace Crowbar.Editor.Tools;

/// <summary>
/// Base class for stateful C#-only editor UI components.
/// </summary>
public abstract class EditorControl : ContentControl
{
    private bool _stateChangePending;

    protected EditorControl()
    {
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    /// <summary>
    /// Rebuilds this component from its current state.
    /// Calling it repeatedly in the same UI turn is coalesced into one rebuild.
    /// </summary>
    public void StateHasChanged()
    {
        if (_stateChangePending)
            return;

        _stateChangePending = true;

        // Always defer the render. Besides keeping all visual-tree changes on
        // the UI thread, this coalesces several StateHasChanged calls made in
        // the same turn into a single rebuild.
        Dispatcher.UIThread.Post(RenderNow);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StateHasChanged();
    }

    /// <summary>
    /// Creates the current visual tree for this component.
    /// </summary>
    protected abstract Control BuildUi();

    private void RenderNow()
    {
        _stateChangePending = false;
        Content = BuildUi();
    }
}
