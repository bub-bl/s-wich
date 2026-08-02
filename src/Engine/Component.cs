namespace Crowbar.Engine;

public abstract class Component : IValid, IDestroyable
{
    public GameObject? GameObject { get; internal set; }
    public bool IsValid { get; private set; }

    internal Component()
    {
        IsValid = true;
    }

    protected internal virtual void OnStart()
    {
    }

    protected internal virtual void OnUpdate()
    {
    }

    protected internal virtual void OnDestroy()
    {
        
    }

    public void Destroy()
    {
        IsValid = false;
        GameObject?.RemoveComponent(this);
        OnDestroy();
    }
}