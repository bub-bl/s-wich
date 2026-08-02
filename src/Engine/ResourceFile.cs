namespace Crowbar.Engine;

[AttributeUsage(AttributeTargets.Class)]
public sealed class AssetTypeAttribute(string extension) : Attribute
{
    public string Extension { get; } = extension;
}

public abstract class ResourceFile : IValid, IDisposable
{
    public string Path { get; internal set; } = string.Empty;
    public FileStream? Data { get; private set; }
    public bool IsValid { get; private set; }

    public void Load()
    {
        if (File.Exists(Path))
        {
            Data = File.OpenRead(Path);
            IsValid = true;
        }
        else
        {
            IsValid = false;
            throw new FileNotFoundException("The file does not exist.", Path);
        }
    }

    public void Unload()
    {
        Data?.Dispose();
        IsValid = false;
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}