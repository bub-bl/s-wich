using System.Collections;

namespace Crowbar.Engine;

public sealed class ComponentList : IReadOnlyList<Component>
{
    private readonly List<Component> _components = [];

    public IReadOnlyList<Component> Components => _components;

    public int Count => _components.Count;

    public Component this[int index] => _components[index];

    public void AddComponent(Component component)
    {
        _components.Add(component);
    }

    public void RemoveComponent(Component component)
    {
        _components.Remove(component);
    }

    public T? GetComponent<T>() where T : Component
    {
        return _components.OfType<T>().FirstOrDefault();
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    public IEnumerator<Component> GetEnumerator()
    {
        return _components.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}