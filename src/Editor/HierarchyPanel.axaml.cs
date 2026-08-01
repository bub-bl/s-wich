using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Crowbar.Engine;

namespace Crowbar.Editor;

public partial class HierarchyPanel : UserControl
{
    public static readonly StyledProperty<Scene?> SceneProperty =
        AvaloniaProperty.Register<HierarchyPanel, Scene?>(nameof(Scene));

    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public ObservableCollection<HierarchyNode> TreeRoots { get; } = new();

    public static readonly StyledProperty<HierarchyNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<HierarchyPanel, HierarchyNode?>(nameof(SelectedNode));

    public HierarchyNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set
        {
            SetValue(SelectedNodeProperty, value);
            if (value?.SceneObject != null && Scene != null && Scene.SelectedObject != value.SceneObject)
            {
                Scene.SelectedObject = value.SceneObject;
            }
        }
    }

    public HierarchyPanel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != SceneProperty) return;
        
        if (change.OldValue is Scene oldScene)
        {
            oldScene.Objects.CollectionChanged -= OnObjectsChanged;
            oldScene.PropertyChanged -= OnScenePropertyChanged;
        }

        if (change.NewValue is Scene newScene)
        {
            newScene.Objects.CollectionChanged += OnObjectsChanged;
            newScene.PropertyChanged += OnScenePropertyChanged;
        }

        RebuildTree();
    }

    private void OnObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTree();

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.AddedItems)
        {
            if (item is HierarchyNode { SceneObject: not null } node && Scene != null)
            {
                Scene.SelectedObject = node.SceneObject;
                break;
            }
        }
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Scene.SelectedObject))
        {
            SelectedNode = FindNode(Scene?.SelectedObject);
        }
    }

    private void RebuildTree()
    {
        TreeRoots.Clear();
        if (Scene == null) return;

        var sceneNode = new HierarchyNode("Scene", "◉", "Scene");
        var renderNode = new HierarchyNode("Render", "▾", "Group");
        renderNode.Children.Add(new HierarchyNode("Main Camera", "◈", "Camera"));
        renderNode.Children.Add(new HierarchyNode("Sky", "☼", "Environment"));

        foreach (var sceneObject in Scene.Objects)
        {
            var icon = sceneObject.MeshType == "Pyramid" ? "◆" : "■";
            var objectNode = new HierarchyNode(sceneObject.Name, icon, sceneObject.MeshType, sceneObject);
            sceneObject.PropertyChanged += (_, _) => objectNode.Refresh();
            renderNode.Children.Add(objectNode);
        }

        sceneNode.Children.Add(renderNode);
        TreeRoots.Add(sceneNode);
        SelectedNode = FindNode(Scene.SelectedObject);
    }

    private HierarchyNode? FindNode(SceneObject? sceneObject)
    {
        if (sceneObject == null || TreeRoots.Count == 0) return null;
        
        foreach (var root in TreeRoots)
        {
            var found = FindNode(root, sceneObject);
            if (found != null) return found;
        }
        
        return null;
    }

    private static HierarchyNode? FindNode(HierarchyNode node, SceneObject sceneObject)
    {
        if (node.SceneObject == sceneObject) return node;
        
        foreach (var child in node.Children)
        {
            var found = FindNode(child, sceneObject);
            if (found != null) return found;
        }
        
        return null;
    }
}
