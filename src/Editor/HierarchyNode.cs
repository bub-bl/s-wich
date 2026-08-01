using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Crowbar.Engine;

namespace Crowbar.Editor;

public sealed class HierarchyNode(string name, string icon, string kind, SceneObject? sceneObject = null)
    : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => sceneObject?.Name ?? name;
    public string Icon { get; } = icon;
    public string Kind { get; } = kind;
    public bool IsObjectNode => sceneObject != null;
    public bool HasVisibility => IsObjectNode;
    
    public bool IsVisible
    {
        get => sceneObject?.IsVisible ?? true;
        set => sceneObject?.IsVisible = value;
    }

    public ObservableCollection<HierarchyNode> Children { get; } = [];

    public SceneObject? SceneObject => sceneObject;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsVisible));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
