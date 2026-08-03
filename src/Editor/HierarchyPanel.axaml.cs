using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Crowbar.Engine;
using Crowbar.Editor.Tools;

namespace Crowbar.Editor;

public sealed class HierarchyPanel : EditorControl
{
    private Scene? _scene;

    public Scene? Scene
    {
        get => _scene;
        set
        {
            if (ReferenceEquals(_scene, value)) return;

            if (_scene is not null)
            {
                _scene.GameObjects.CollectionChanged -= OnObjectsChanged;
                _scene.PropertyChanged -= OnScenePropertyChanged;
            }

            _scene = value;

            if (_scene is not null)
            {
                _scene.GameObjects.CollectionChanged += OnObjectsChanged;
                _scene.PropertyChanged += OnScenePropertyChanged;
            }

            StateHasChanged();
        }
    }

    public ObservableCollection<HierarchyNode> TreeRoots { get; } = [];

    protected override Control BuildUi()
    {
        TreeRoots.Clear();

        var tree = new TreeView
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4, 6, 4, 4),
            MinHeight = 100
        };
        tree.SelectionChanged += OnTreeSelectionChanged;

        RebuildTree(tree);

        return new Border
        {
            Background = Brush.Parse("#1F1F23"),
            BorderBrush = Brush.Parse("#27272A"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto, *"),
                Children =
                {
                    new Border
                    {
                        Background = Brush.Parse("#27272A"),
                        Padding = new Thickness(10, 6),
                        Child = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*, Auto"),
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "HIERARCHY",
                                    FontWeight = FontWeight.Bold,
                                    FontSize = 11,
                                    Foreground = Brush.Parse("#A1A1AA")
                                },
                                new TextBlock
                                {
                                    [Grid.ColumnProperty] = 1,
                                    Text = "⌄",
                                    FontSize = 14,
                                    Foreground = Brush.Parse("#71717A"),
                                    VerticalAlignment = VerticalAlignment.Center
                                }
                            }
                        }
                    },
                    tree
                }
            }
        };
    }

    private void RebuildTree(TreeView tree)
    {
        if (_scene is null) return;

        var sceneNode = new HierarchyNode("Scene", "◉", "Scene");
        var renderNode = new HierarchyNode("Render", "▾", "Group");
        renderNode.Children.Add(new HierarchyNode("Main Camera", "◈", "Camera"));
        renderNode.Children.Add(new HierarchyNode("Sky", "☼", "Environment"));

        foreach (GameObject gameObject in _scene.GameObjects)
        {
            SceneObject? sceneObject = gameObject.ModelRenderer?.SceneObject;
            if (sceneObject is null) continue;

            var icon = sceneObject.MeshType == "Pyramid" ? "◆" : "■";
            renderNode.Children.Add(new HierarchyNode(sceneObject.Name, icon, sceneObject.MeshType, sceneObject));
        }

        sceneNode.Children.Add(renderNode);
        TreeRoots.Add(sceneNode);
        tree.Items.Add(CreateTreeItem(sceneNode));
    }

    private TreeViewItem CreateTreeItem(HierarchyNode node)
    {
        var item = new TreeViewItem
        {
            Tag = node,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1),
            MinHeight = 28,
            Header = CreateNodeView(node)
        };

        foreach (HierarchyNode child in node.Children)
            item.Items.Add(CreateTreeItem(child));

        return item;
    }

    private Control CreateNodeView(HierarchyNode node)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("20, *, Auto"),
            MinWidth = 210
        };

        grid.Children.Add(new TextBlock
        {
            Text = node.Icon,
            FontSize = 13,
            Foreground = Brush.Parse("#F5B94C"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        grid.Children.Add(new TextBlock
        {
            [Grid.ColumnProperty] = 1,
            Text = node.Name,
            Foreground = Brush.Parse("#E4E4E7"),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Medium
        });

        if (node.HasVisibility)
        {
            var visibility = new CheckBox
            {
                [Grid.ColumnProperty] = 2,
                IsChecked = node.IsVisible,
                Content = "◉",
                FontSize = 11,
                Foreground = Brush.Parse("#A1A1AA"),
                Opacity = 0.85,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            visibility.IsCheckedChanged += (_, _) => node.IsVisible = visibility.IsChecked == true;
            grid.Children.Add(visibility);
        }

        return new Border
        {
            Padding = new Thickness(5, 4),
            CornerRadius = new CornerRadius(3),
            Child = grid
        };
    }

    private void OnObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => StateHasChanged();

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (object item in e.AddedItems)
        {
            if (item is TreeViewItem { Tag: HierarchyNode { SceneObject: not null } node } && _scene is not null)
            {
                _scene.SelectedObject = node.SceneObject;
                break;
            }
        }
    }

    private void OnScenePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Scene.SelectedObject) or nameof(SceneObject.Name) or nameof(SceneObject.IsVisible))
            StateHasChanged();
    }
}
