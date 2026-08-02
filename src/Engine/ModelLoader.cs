using System.Numerics;
using Silk.NET.Assimp;
using AssimpMesh = Silk.NET.Assimp.Mesh;

namespace Crowbar.Engine;

/// <summary>
/// Internal unsafe bridge between the safe model API and Assimp's native scene data.
/// </summary>
internal static unsafe class ModelLoader
{
    internal static Model Load(string path)
    {
        var filePath = ResolvePath(path);

        if (filePath is null)
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

        if (scene is null)
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

                if (mesh is null || mesh->MNumVertices is 0 || mesh->MNumFaces is 0)
                    continue;

                meshes.Add(ConvertMesh(mesh, i));
            }

            if (meshes.Count is 0)
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
            normals[i] = mesh->MNormals is null ? Vector3.UnitY : mesh->MNormals[i];

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

    private static ModelFormat GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" => ModelFormat.Obj,
        ".gltf" => ModelFormat.Gltf,
        ".glb" => ModelFormat.Glb,
        ".fbx" => ModelFormat.Fbx,
        _ => ModelFormat.Other
    };

    private static string? ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return System.IO.File.Exists(path) ? Path.GetFullPath(path) : null;

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, path),
            Path.GetFullPath(path)
        ];

        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}
