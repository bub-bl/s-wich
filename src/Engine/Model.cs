using System.Numerics;
using Silk.NET.Assimp;
using AssimpFace = Silk.NET.Assimp.Face;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using AssimpScene = Silk.NET.Assimp.Scene;

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
public sealed unsafe class Model
{
    public string Path { get; }
    public string FilePath { get; }
    public string Name { get; }
    public ModelFormat Format { get; }
    public IReadOnlyList<ModelMesh> Meshes { get; }

    private Model(string requestedPath, string filePath, ModelFormat format, IReadOnlyList<ModelMesh> meshes)
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

        var filePath = ResolvePath(path);
        if (filePath == null)
            throw new FileNotFoundException($"Model file '{path}' was not found.", path);

        var assimp = Assimp.GetApi();

        const PostProcessSteps postProcess =
            PostProcessSteps.Triangulate |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.ImproveCacheLocality |
            PostProcessSteps.SortByPrimitiveType |
            PostProcessSteps.ValidateDataStructure;

        var scene = assimp.ImportFile(filePath, (uint)postProcess);

        if (scene == null)
        {
            var error = assimp.GetErrorStringS();
            throw new FormatException($"Assimp could not import model '{path}': {error}");
        }

        try
        {
            var meshes = new List<ModelMesh>((int)scene->MNumMeshes);

            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];
                if (mesh == null || mesh->MNumVertices == 0 || mesh->MNumFaces == 0)
                    continue;

                meshes.Add(ConvertMesh(mesh, i));
            }

            if (meshes.Count == 0)
                throw new FormatException($"Model '{path}' does not contain any renderable meshes.");

            return new Model(path, filePath, GetFormat(filePath), meshes);
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }

    private static ModelMesh ConvertMesh(AssimpMesh* mesh, uint meshIndex)
    {
        var vertexCount = checked((int)mesh->MNumVertices);
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var textureCoordinates = new Vector2[vertexCount];

        for (var i = 0; i < vertexCount; i++)
        {
            positions[i] = mesh->MVertices[i];
            normals[i] = mesh->MNormals == null ? Vector3.UnitY : mesh->MNormals[i];

            if (mesh->MTextureCoords.Element0 != null)
            {
                var uv = mesh->MTextureCoords.Element0[i];
                textureCoordinates[i] = new Vector2(uv.X, uv.Y);
            }
        }

        var indices = new List<int>(checked((int)mesh->MNumFaces * 3));

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];

            for (uint j = 0; j < face.MNumIndices; j++)
                indices.Add(checked((int)face.MIndices[j]));
        }

        var name = mesh->MName.AsString;
        if (string.IsNullOrWhiteSpace(name)) name = $"Mesh_{meshIndex}";

        return new ModelMesh(name, positions, normals, textureCoordinates, indices);
    }

    private static ModelFormat GetFormat(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" => ModelFormat.Obj,
        ".gltf" => ModelFormat.Gltf,
        ".glb" => ModelFormat.Glb,
        ".fbx" => ModelFormat.Fbx,
        _ => ModelFormat.Other
    };

    private static string? ResolvePath(string path)
    {
        if (System.IO.Path.IsPathRooted(path))
            return System.IO.File.Exists(path) ? System.IO.Path.GetFullPath(path) : null;

        string[] candidates =
        [
            System.IO.Path.Combine(AppContext.BaseDirectory, path),
            System.IO.Path.GetFullPath(path)
        ];

        return candidates.FirstOrDefault(System.IO.File.Exists);
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