using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Assimp;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using AssimpScene = Silk.NET.Assimp.Scene;
using AssimpMaterial = Silk.NET.Assimp.Material;
using AssimpTexture = Silk.NET.Assimp.Texture;

namespace Crowbar.Engine;

internal static unsafe class AssimpModelImporter
{
    internal static ModelImportData Read(string filePath)
    {
        var assimp = Assimp.GetApi();
        const PostProcessSteps postProcess =
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.ValidateDataStructure;

        var scene = assimp.ImportFile(filePath, (uint)postProcess);
        if (scene is null)
            throw new FormatException($"Assimp could not import model '{filePath}': {assimp.GetErrorStringS()}");

        try
        {
            return new ModelImportData
            {
                Meshes = ReadMeshes(scene),
                Materials = ReadMaterials(assimp, scene, filePath)
            };
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }

    private static IReadOnlyList<ModelMesh> ReadMeshes(AssimpScene* scene)
    {
        var meshes = new List<ModelMesh>((int)scene->MNumMeshes);
        for (uint index = 0; index < scene->MNumMeshes; index++)
        {
            AssimpMesh* mesh = scene->MMeshes[index];
            if (mesh is null || mesh->MNumVertices == 0 || mesh->MNumFaces == 0) continue;
            meshes.Add(ReadMesh(mesh, index));
        }
        return meshes;
    }

    private static ModelMesh ReadMesh(AssimpMesh* mesh, uint meshIndex)
    {
        int vertexCount = checked((int)mesh->MNumVertices);
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var tangents = new Vector4[vertexCount];
        var uvs = new Vector2[vertexCount];

        for (int index = 0; index < vertexCount; index++)
        {
            positions[index] = mesh->MVertices[index];
            normals[index] = mesh->MNormals is null ? Vector3.UnitY : mesh->MNormals[index];
            tangents[index] = ReadTangent(mesh, index, normals[index]);
            if (mesh->MTextureCoords.Element0 is not null)
            {
                Vector3 uv = mesh->MTextureCoords.Element0[index];
                uvs[index] = new Vector2(uv.X, uv.Y);
            }
        }

        var indices = new List<int>(checked((int)mesh->MNumFaces * 3));
        for (uint faceIndex = 0; faceIndex < mesh->MNumFaces; faceIndex++)
        {
            Face face = mesh->MFaces[faceIndex];
            for (uint index = 0; index < face.MNumIndices; index++)
                indices.Add(checked((int)face.MIndices[index]));
        }

        string name = mesh->MName.AsString;
        if (string.IsNullOrWhiteSpace(name)) name = $"Mesh_{meshIndex}";
        return new ModelMesh(name, positions, normals, tangents, uvs, indices, checked((int)mesh->MMaterialIndex));
    }

    private static Vector4 ReadTangent(AssimpMesh* mesh, int index, Vector3 normal)
    {
        if (mesh->MTangents is null) return new Vector4(Vector3.UnitX, 1f);
        Vector3 tangent = Vector3.Normalize(mesh->MTangents[index]);
        float handedness = 1f;
        if (mesh->MBitangents is not null)
        {
            Vector3 bitangent = Vector3.Normalize(mesh->MBitangents[index]);
            handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0f ? -1f : 1f;
        }
        return new Vector4(tangent, handedness);
    }

    private static IReadOnlyList<ModelMaterial> ReadMaterials(Assimp assimp, AssimpScene* scene, string modelPath)
    {
        var materials = new List<ModelMaterial>((int)scene->MNumMaterials);
        for (uint index = 0; index < scene->MNumMaterials; index++)
        {
            AssimpMaterial* material = scene->MMaterials[index];
            Vector4 baseColor = Vector4.One;
            if (material is not null)
            {
                fixed (byte* key = "$clr.base"u8)
                {
                    Vector4* color = &baseColor;
                    assimp.GetMaterialColor(material, key, 0, 0, color);
                }
            }

            materials.Add(new ModelMaterial
            {
                BaseColorFactor = baseColor,
                MetallicFactor = ReadFloat(assimp, material, "$mat.metallicFactor", 1f),
                RoughnessFactor = ReadFloat(assimp, material, "$mat.roughnessFactor", 1f),
                BaseColorTexture = ReadTexture(assimp, scene, material, TextureType.BaseColor, modelPath),
                NormalTexture = ReadTexture(assimp, scene, material, TextureType.Normals, modelPath),
                MetallicRoughnessTexture = ReadTexture(assimp, scene, material, TextureType.GltfMetallicRoughness, modelPath)
            });
        }
        return materials;
    }

    private static float ReadFloat(Assimp assimp, AssimpMaterial* material, string key, float fallback)
    {
        if (material is null) return fallback;
        float value = fallback;
        uint max = 1;
        return assimp.GetMaterialFloatArray(material, key, 0, 0, &value, &max) == Return.Success ? value : fallback;
    }

    private static ModelTexture? ReadTexture(Assimp assimp, AssimpScene* scene, AssimpMaterial* material, TextureType type, string modelPath)
    {
        if (material is null) return null;
        if (assimp.GetMaterialTextureCount(material, type) == 0)
        {
            if (type != TextureType.BaseColor || assimp.GetMaterialTextureCount(material, TextureType.Diffuse) == 0) return null;
            type = TextureType.Diffuse;
        }

        AssimpString path;
        TextureMapping mapping;
        uint uvIndex;
        float blend;
        TextureOp operation;
        TextureMapMode mapMode;
        uint flags;
        if (assimp.GetMaterialTexture(material, type, 0, &path, &mapping, &uvIndex, &blend, &operation, &mapMode, &flags) != Return.Success)
            return null;

        string texturePath = path.AsString;
        if (texturePath.StartsWith('*') && uint.TryParse(texturePath[1..], out uint embeddedIndex) && embeddedIndex < scene->MNumTextures)
            return ReadEmbeddedTexture(scene->MTextures[embeddedIndex]);

        if (!Path.IsPathRooted(texturePath))
        {
            string directory = Path.GetDirectoryName(modelPath) ?? string.Empty;
            texturePath = Path.GetFullPath(Path.Combine(directory, texturePath));
        }
        return System.IO.File.Exists(texturePath) ? TextureDecoder.Decode(System.IO.File.ReadAllBytes(texturePath)) : null;
    }

    private static ModelTexture? ReadEmbeddedTexture(AssimpTexture* texture)
    {
        if (texture is null) return null;
        if (texture->MHeight == 0)
        {
            byte[] encoded = new byte[checked((int)texture->MWidth)];
            Marshal.Copy((nint)texture->PcData, encoded, 0, encoded.Length);
            return TextureDecoder.Decode(encoded);
        }

        int pixelCount = checked((int)(texture->MWidth * texture->MHeight));
        byte[] pixels = new byte[pixelCount * 4];
        for (int index = 0; index < pixelCount; index++)
        {
            Texel texel = texture->PcData[index];
            int offset = index * 4;
            pixels[offset] = texel.R;
            pixels[offset + 1] = texel.G;
            pixels[offset + 2] = texel.B;
            pixels[offset + 3] = texel.A;
        }
        return new ModelTexture { Width = (int)texture->MWidth, Height = (int)texture->MHeight, Pixels = pixels };
    }
}
