using System.Numerics;

namespace Crowbar.Engine;

public abstract class Component : IValid, IDestroyable
{
    public GameObject GameObject { get; internal set; } = null!;
    public bool IsValid { get; private set; } = true;
    public bool Enabled { get; set; } = true;

    public virtual Vector3 WorldPosition
    {
        get => GameObject.WorldPosition;
        set => GameObject.WorldPosition = value;
    }

    public virtual Rotation WorldRotation
    {
        get => GameObject.WorldRotation;
        set => GameObject.WorldRotation = value;
    }

    public virtual Vector3 WorldScale
    {
        get => GameObject.WorldScale;
        set => GameObject.WorldScale = value;
    }

    protected internal virtual void OnStart()
    {
    }

    protected internal virtual void OnUpdate(float deltaTime)
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