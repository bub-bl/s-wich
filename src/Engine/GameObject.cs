using System.Numerics;

namespace Crowbar.Engine;

public sealed class GameObject
{
    private readonly ComponentList _components = [];

    public IReadOnlyList<Component> Components => _components;
    public ModelRenderer? ModelRenderer { get; private set; }
    internal Scene? Scene { get; set; }
    public bool IsValid { get; private set; } = true;
    public Transform WorldTransform { get; set; } = Transform.Zero;

    public Vector3 WorldPosition
    {
        get => WorldTransform.Position;
        set => WorldTransform = WorldTransform.WithPosition(value);
    }

    public Rotation WorldRotation
    {
        get => WorldTransform.Rotation;
        set => WorldTransform = WorldTransform.WithRotation(value);
    }

    public Vector3 WorldScale
    {
        get => WorldTransform.Scale;
        set => WorldTransform = WorldTransform.WithScale(value);
    }

    public void AddComponent(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!IsValid) throw new InvalidOperationException("Cannot add a component to a destroyed GameObject.");
        if (component.GameObject is not null && !ReferenceEquals(component.GameObject, this))
            throw new InvalidOperationException("The component already belongs to another GameObject.");

        Transform initialTransform = component is ModelRenderer renderer
            ? renderer.SceneObject.WorldTransform
            : WorldTransform;

        component.GameObject = this;
        _components.AddComponent(component);
        if (component is ModelRenderer modelRenderer)
        {
            ModelRenderer = modelRenderer;
            WorldTransform = initialTransform;
        }
        component.OnStart();
    }

    public void AddComponent<T>(T component) where T : Component
    {
        AddComponent((Component)component);
    }

    public void RemoveComponent(Component component)
    {
        if (!_components.RemoveComponent(component)) return;

        if (ReferenceEquals(ModelRenderer, component))
            ModelRenderer = null;
        component.GameObject = null!;
    }

    public void RemoveComponent<T>(T component) where T : Component
    {
        RemoveComponent((Component)component);
    }

    public T? GetComponent<T>() where T : Component
    {
        foreach (Component component in _components)
        {
            if (component is T typedComponent)
                return typedComponent;
        }

        return null;
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    public void Destroy()
    {
        if (!IsValid) return;

        IsValid = false;

        foreach (Component component in _components.ToArray())
        {
            component.Destroy();
        }

        Scene?.RemoveGameObject(this);
    }
}
