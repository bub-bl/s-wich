namespace Crowbar.Engine;

public abstract class Component : IValid, IDestroyable
{
    public GameObject? GameObject { get; internal set; }
    public bool IsValid { get; private set; } = true;
    public bool Enabled { get; set; } = true;

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
        if (!IsValid) return;

        IsValid = false;
        GameObject? owner = GameObject;
        owner?.RemoveComponent(this);
        OnDestroy();
    }
}
