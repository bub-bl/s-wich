namespace Crowbar.Engine;

public sealed record SceneFileMetadata(string Name, string Version);

[AssetType("scene")]
public sealed class SceneFile : ResourceFile
{
    public Guid Id { get; init; }
    public required SceneFileMetadata Metadata { get; init; }
    // public List<GameObject> GameObjects { get; private set; } = [];
}