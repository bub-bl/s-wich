namespace Crowbar.Engine;

public sealed class GameObject
{
    private readonly ComponentList _components = [];

    public IReadOnlyList<Component> Components => _components;
    public ModelRenderer? ModelRenderer { get; private set; }

    public Transform Transform { get; set; } = Transform.Zero;
    public bool IsValid { get; private set; } = true;

    public GameObject()
    {
        Scene.Current?.AddGameObject(this);
    }

    public void AddComponent(Component component)
    {
        component.GameObject = this;
        _components.AddComponent(component);
        if (component is ModelRenderer modelRenderer)
            ModelRenderer = modelRenderer;
    }

    public void AddComponent<T>(T component) where T : Component
    {
        component.GameObject = this;
        _components.AddComponent(component);
        if (component is ModelRenderer modelRenderer)
            ModelRenderer = modelRenderer;
    }

    public void RemoveComponent(Component component)
    {
        _components.RemoveComponent(component);
        if (ReferenceEquals(ModelRenderer, component))
            ModelRenderer = null;
    }

    public void RemoveComponent<T>(T component) where T : Component
    {
        _components.RemoveComponent(component);
        if (ReferenceEquals(ModelRenderer, component))
            ModelRenderer = null;
    }

    public T? GetComponent<T>() where T : Component
    {
        return _components.OfType<T>().FirstOrDefault();
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    public void Destroy()
    {
        IsValid = false;

        foreach (var component in _components)
        {
            component.Destroy();
        }

        Scene.Current?.RemoveGameObject(this);
    }
}
