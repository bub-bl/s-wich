using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Crowbar;

public sealed class HierarchyNode : INotifyPropertyChanged
{
    private readonly SceneObject? _sceneObject;
    private string _name;

    public event PropertyChangedEventHandler? PropertyChanged;

    public HierarchyNode(string name, string icon, string kind, SceneObject? sceneObject = null)
    {
        _name = name;
        Icon = icon;
        Kind = kind;
        _sceneObject = sceneObject;
    }

    public string Name => _sceneObject?.Name ?? _name;
    public string Icon { get; }
    public string Kind { get; }
    public bool IsObjectNode => _sceneObject != null;
    public bool HasVisibility => IsObjectNode;
    public bool IsVisible
    {
        get => _sceneObject?.IsVisible ?? true;
        set
        {
            if (_sceneObject != null) _sceneObject.IsVisible = value;
        }
    }

    public ObservableCollection<HierarchyNode> Children { get; } = new();

    public SceneObject? SceneObject => _sceneObject;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsVisible));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
