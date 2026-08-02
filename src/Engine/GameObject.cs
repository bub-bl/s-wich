namespace Crowbar.Engine;

public sealed class GameObject
{
    private readonly ComponentList _components = [];

    public IReadOnlyList<Component> Components => _components;
    public bool IsValid { get; private set; } = true;

    public GameObject()
    {
        Scene.Current?.AddGameObject(this);
    }

    public void AddComponent(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.GameObject is not null && component.GameObject != this)
            throw new InvalidOperationException("The component already belongs to another GameObject.");

        component.GameObject = this;
        _components.AddComponent(component);
        component.OnStart();
    }

    public void AddComponent<T>(T component) where T : Component
    {
        component.GameObject = this;
        _components.AddComponent(component);
    }

    public void RemoveComponent(Component component)
    {
        if (!_components.Contains(component)) return;
        _components.RemoveComponent(component);
        component.GameObject = null;
    }

    public void RemoveComponent<T>(T component) where T : Component
    {
        _components.RemoveComponent(component);
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

        foreach (var component in _components.Components.ToArray())
        {
            component.Destroy();
        }

        Scene.Current?.RemoveGameObject(this);
    }
}
