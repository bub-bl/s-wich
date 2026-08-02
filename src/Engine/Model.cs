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
    public IReadOnlyList<ModelMaterial> Materials { get; }

    internal Model(string requestedPath, string filePath, ModelFormat format, IReadOnlyList<ModelMesh> meshes, IReadOnlyList<ModelMaterial> materials)
    {
        Path = requestedPath;
        FilePath = filePath;
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        Format = format;
        Meshes = meshes;
        Materials = materials;
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
    public IReadOnlyList<Vector4> Tangents { get; }
    public IReadOnlyList<Vector2> TextureCoordinates { get; }
    public IReadOnlyList<int> Indices { get; }
    public int MaterialIndex { get; }

    internal ModelMesh(
        string name,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector4> tangents,
        IReadOnlyList<Vector2> textureCoordinates,
        IReadOnlyList<int> indices,
        int materialIndex)
    {
        Name = name;
        Positions = positions;
        Normals = normals;
        Tangents = tangents;
        TextureCoordinates = textureCoordinates;
        Indices = indices;
        MaterialIndex = materialIndex;
    }
}

public sealed class ModelMaterial
{
    public Vector4 BaseColorFactor { get; init; } = Vector4.One;
    public float MetallicFactor { get; init; } = 1f;
    public float RoughnessFactor { get; init; } = 1f;
    public ModelTexture? BaseColorTexture { get; init; }
    public ModelTexture? NormalTexture { get; init; }
    public ModelTexture? MetallicRoughnessTexture { get; init; }
}

public sealed class ModelTexture
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Pixels { get; init; }
}
