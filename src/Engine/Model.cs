using System.Numerics;

namespace Crowbar.Engine;

public enum ModelFormat
{
    Obj,
    Gltf,
    Glb,
    Fbx,
    Other
}

/// <summary>
/// A CPU-side model imported through Assimp.
/// </summary>
public sealed class Model
{
    public string Path { get; }
    public string FilePath { get; }
    public string Name { get; }
    public ModelFormat Format { get; }
    public IReadOnlyList<ModelMesh> Meshes { get; }

    internal Model(string requestedPath, string filePath, ModelFormat format, IReadOnlyList<ModelMesh> meshes)
    {
        Path = requestedPath;
        FilePath = filePath;
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        Format = format;
        Meshes = meshes;
    }

    public static Model Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ModelLoader.Load(path);
    }
}

/// <summary>
/// A renderable mesh contained in a <see cref="Model"/>.
/// Every three consecutive indices form one triangle after Assimp triangulation.
/// </summary>
public sealed class ModelMesh
{
    public string Name { get; }
    public IReadOnlyList<Vector3> Positions { get; }
    public IReadOnlyList<Vector3> Normals { get; }
    public IReadOnlyList<Vector2> TextureCoordinates { get; }
    public IReadOnlyList<int> Indices { get; }

    internal ModelMesh(
        string name,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> textureCoordinates,
        IReadOnlyList<int> indices)
    {
        Name = name;
        Positions = positions;
        Normals = normals;
        TextureCoordinates = textureCoordinates;
        Indices = indices;
    }
}
