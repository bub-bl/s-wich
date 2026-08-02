using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Buffers.Binary;
using Silk.NET.Assimp;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using AssimpScene = Silk.NET.Assimp.Scene;
using AssimpMaterial = Silk.NET.Assimp.Material;
using AssimpTexture = Silk.NET.Assimp.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
            var materials = GetFormat(filePath) is ModelFormat.Glb or ModelFormat.Gltf
                ? ConvertGltfMaterials(filePath) ?? ConvertMaterials(assimp, scene)
                : ConvertMaterials(assimp, scene);
            var meshes = GetFormat(filePath) == ModelFormat.Gltf
                ? ConvertGltfMeshes(filePath) ?? ConvertAssimpMeshes(scene)
                : ConvertAssimpMeshes(scene);

            if (meshes.Count is 0)
                throw new FormatException($"Model '{path}' does not contain any renderable meshes.");

            return new Model(path, filePath, GetFormat(filePath), meshes, materials);
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }

    private static List<ModelMesh> ConvertAssimpMeshes(AssimpScene* scene)
    {
        var meshes = new List<ModelMesh>((int)scene->MNumMeshes);
        for (uint i = 0; i < scene->MNumMeshes; i++)
        {
            var mesh = scene->MMeshes[i];
            if (mesh is null || mesh->MNumVertices is 0 || mesh->MNumFaces is 0) continue;
            meshes.Add(ConvertMesh(mesh, i));
        }
        return meshes;
    }

    private static List<ModelMesh>? ConvertGltfMeshes(string filePath)
    {
        using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllBytes(filePath));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("meshes", out JsonElement meshElements) ||
            !root.TryGetProperty("accessors", out JsonElement accessors) ||
            !root.TryGetProperty("bufferViews", out JsonElement bufferViews) ||
            !root.TryGetProperty("buffers", out JsonElement buffers)) return null;

        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string bufferUri = buffers[0].GetProperty("uri").GetString() ?? string.Empty;
        string bufferPath = Path.GetFullPath(Path.Combine(directory, bufferUri.Replace('/', Path.DirectorySeparatorChar)));
        if (!System.IO.File.Exists(bufferPath)) return null;
        byte[] binary = System.IO.File.ReadAllBytes(bufferPath);

        byte[] AccessorBytes(int accessorIndex, out int count, out int stride, out int componentCount, out int componentSize)
        {
            JsonElement accessor = accessors[accessorIndex];
            count = accessor.GetProperty("count").GetInt32();
            string type = accessor.GetProperty("type").GetString()!;
            componentCount = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => throw new FormatException($"Unsupported glTF accessor type '{type}'.") };
            int componentType = accessor.GetProperty("componentType").GetInt32();
            componentSize = componentType switch { 5126 or 5125 => 4, 5123 => 2, _ => throw new FormatException($"Unsupported glTF component type '{componentType}'.") };
            JsonElement view = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
            int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
            int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
            stride = view.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : componentCount * componentSize;
            int length = checked((count - 1) * stride + componentCount * componentSize);
            return binary.AsSpan(viewOffset + accessorOffset, length).ToArray();
        }

        Vector3[] ReadVector3(int accessorIndex)
        {
            byte[] bytes = AccessorBytes(accessorIndex, out int count, out int stride, out _, out _);
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                result[i] = new Vector3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));
            }
            return result;
        }

        Vector2[] ReadVector2(int accessorIndex)
        {
            byte[] bytes = AccessorBytes(accessorIndex, out int count, out int stride, out _, out _);
            var result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                result[i] = new Vector2(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4));
            }
            return result;
        }

        Vector4[] ReadVector4(int accessorIndex)
        {
            byte[] bytes = AccessorBytes(accessorIndex, out int count, out int stride, out _, out _);
            var result = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                result[i] = new Vector4(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8), BitConverter.ToSingle(bytes, offset + 12));
            }
            return result;
        }

        int[] ReadIndices(int accessorIndex)
        {
            byte[] bytes = AccessorBytes(accessorIndex, out int count, out int stride, out _, out int componentSize);
            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                result[i] = componentSize == 2
                    ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2))
                    : checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            }
            return result;
        }

        var meshes = new List<ModelMesh>();
        foreach (JsonElement meshElement in meshElements.EnumerateArray())
        {
            string meshName = meshElement.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "Mesh" : "Mesh";
            foreach (JsonElement primitive in meshElement.GetProperty("primitives").EnumerateArray())
            {
                JsonElement attributes = primitive.GetProperty("attributes");
                Vector3[] positions = ReadVector3(attributes.GetProperty("POSITION").GetInt32());
                Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normal) ? ReadVector3(normal.GetInt32()) : Enumerable.Repeat(Vector3.UnitY, positions.Length).ToArray();
                Vector4[] tangents = attributes.TryGetProperty("TANGENT", out JsonElement tangent) ? ReadVector4(tangent.GetInt32()) : Enumerable.Repeat(new Vector4(Vector3.UnitX, 1f), positions.Length).ToArray();
                Vector2[] uv = attributes.TryGetProperty("TEXCOORD_0", out JsonElement texcoord) ? ReadVector2(texcoord.GetInt32()) : new Vector2[positions.Length];
                int[] indices = ReadIndices(primitive.GetProperty("indices").GetInt32());
                int materialIndex = primitive.TryGetProperty("material", out JsonElement material) ? material.GetInt32() : 0;
                meshes.Add(new ModelMesh(meshName, positions, normals, tangents, uv, indices, materialIndex));
            }
        }
        return meshes;
    }

    private static ModelMesh ConvertMesh(AssimpMesh* mesh, uint meshIndex)
    {
        var vertexCount = checked((int)mesh->MNumVertices);
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var tangents = new Vector4[vertexCount];
        var textureCoordinates = new Vector2[vertexCount];

        for (var i = 0; i < vertexCount; i++)
        {
            positions[i] = mesh->MVertices[i];
            normals[i] = mesh->MNormals is null ? Vector3.UnitY : mesh->MNormals[i];

            if (mesh->MTangents is not null)
            {
                var tangent = Vector3.Normalize(mesh->MTangents[i]);
                float handedness = 1f;
                if (mesh->MBitangents is not null)
                {
                    var bitangent = Vector3.Normalize(mesh->MBitangents[i]);
                    handedness = Vector3.Dot(Vector3.Cross(normals[i], tangent), bitangent) < 0f ? -1f : 1f;
                }
                tangents[i] = new Vector4(tangent, handedness);
            }
            else
            {
                tangents[i] = new Vector4(Vector3.UnitX, 1f);
            }

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

        return new ModelMesh(name, positions, normals, tangents, textureCoordinates, indices,
            checked((int)mesh->MMaterialIndex));
    }

    private static List<ModelMaterial> ConvertMaterials(Assimp assimp, AssimpScene* scene)
    {
        var materials = new List<ModelMaterial>((int)scene->MNumMaterials);
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            var material = scene->MMaterials[i];
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
                MetallicFactor = ReadMaterialFloat(assimp, material, "$mat.metallicFactor", 0f),
                RoughnessFactor = ReadMaterialFloat(assimp, material, "$mat.roughnessFactor", 1f),
                BaseColorTexture = ReadTexture(assimp, scene, material, TextureType.BaseColor),
                NormalTexture = ReadTexture(assimp, scene, material, TextureType.Normals),
                MetallicRoughnessTexture = ReadTexture(assimp, scene, material, TextureType.GltfMetallicRoughness)
            });
        }

        return materials;
    }

    private static List<ModelMaterial>? ConvertGltfMaterials(string filePath)
    {
        byte[] file = System.IO.File.ReadAllBytes(filePath);
        JsonDocument? document = null;
        byte[]? binary = null;
        if (GetFormat(filePath) == ModelFormat.Glb)
        {
            if (file.Length < 20 || BitConverter.ToUInt32(file, 0) != 0x46546C67) return null;
            int offset = 12;
            while (offset + 8 <= file.Length)
            {
                int length = BitConverter.ToInt32(file, offset);
                uint type = BitConverter.ToUInt32(file, offset + 4);
                offset += 8;
                if (type == 0x4E4F534A)
                    document = JsonDocument.Parse(file.AsSpan(offset, length).ToArray());
                else if (type == 0x004E4942)
                    binary = file.AsSpan(offset, length).ToArray();
                offset += length;
            }
        }
        else
        {
            document = JsonDocument.Parse(file);
        }

        if (document is null) return null;
        using (document)
        {
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("materials", out JsonElement materialElements)) return [];
            root.TryGetProperty("textures", out JsonElement textureElements);
            root.TryGetProperty("images", out JsonElement imageElements);
            root.TryGetProperty("bufferViews", out JsonElement bufferViews);

            ModelTexture? ReadTexture(JsonElement textureReference)
            {
                if (!textureReference.TryGetProperty("index", out JsonElement textureIndex) ||
                    !textureElements.ValueKind.Equals(JsonValueKind.Array)) return null;
                int ti = textureIndex.GetInt32();
                if ((uint)ti >= textureElements.GetArrayLength()) return null;
                JsonElement texture = textureElements[ti];
                if (!texture.TryGetProperty("source", out JsonElement sourceElement)) return null;
                int imageIndex = sourceElement.GetInt32();
                if ((uint)imageIndex >= imageElements.GetArrayLength()) return null;
                JsonElement image = imageElements[imageIndex];
                if (image.TryGetProperty("uri", out JsonElement uriElement))
                {
                    string imagePath = uriElement.GetString() ?? string.Empty;
                    string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    string resolved = Path.GetFullPath(Path.Combine(directory, imagePath.Replace('/', Path.DirectorySeparatorChar)));
                    return System.IO.File.Exists(resolved)
                        ? DecodeImage(System.IO.File.ReadAllBytes(resolved))
                        : null;
                }
                if (binary is null) return null;
                if (!image.TryGetProperty("bufferView", out JsonElement viewElement)) return null;
                int viewIndex = viewElement.GetInt32();
                JsonElement view = bufferViews[viewIndex];
                int byteOffset = view.TryGetProperty("byteOffset", out JsonElement viewOffset) ? viewOffset.GetInt32() : 0;
                int byteLength = view.GetProperty("byteLength").GetInt32();
                if (byteOffset < 0 || byteLength < 0 || byteOffset + byteLength > binary.Length) return null;
                return DecodeImage(binary.AsSpan(byteOffset, byteLength).ToArray());
            }

            static float ReadFloat(JsonElement element, string property, float fallback) =>
                element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetSingle()
                    : fallback;

            var materials = new List<ModelMaterial>(materialElements.GetArrayLength());
            foreach (JsonElement element in materialElements.EnumerateArray())
            {
                JsonElement pbr = element.TryGetProperty("pbrMetallicRoughness", out JsonElement pbrElement)
                    ? pbrElement
                    : default;
                Vector4 baseColor = Vector4.One;
                if (pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("baseColorFactor", out JsonElement factor))
                {
                    float[] values = factor.EnumerateArray().Select(value => value.GetSingle()).ToArray();
                    if (values.Length == 4) baseColor = new Vector4(values[0], values[1], values[2], values[3]);
                }

                materials.Add(new ModelMaterial
                {
                    BaseColorFactor = baseColor,
                    MetallicFactor = ReadFloat(pbr, "metallicFactor", 1f),
                    RoughnessFactor = ReadFloat(pbr, "roughnessFactor", 1f),
                    BaseColorTexture = pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("baseColorTexture", out JsonElement baseTexture)
                        ? ReadTexture(baseTexture) : null,
                    MetallicRoughnessTexture = pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("metallicRoughnessTexture", out JsonElement metallicTexture)
                        ? ReadTexture(metallicTexture) : null,
                    NormalTexture = element.TryGetProperty("normalTexture", out JsonElement normalTexture)
                        ? ReadTexture(normalTexture) : null
                });
            }

            return materials;
        }
    }

    private static float ReadMaterialFloat(Assimp assimp, AssimpMaterial* material, string key, float fallback)
    {
        if (material is null) return fallback;
        float value = fallback;
        uint max = 1;
        return assimp.GetMaterialFloatArray(material, key, 0, 0, &value, &max) == Return.Success
            ? value
            : fallback;
    }

    private static ModelTexture? ReadTexture(Assimp assimp, AssimpScene* scene, AssimpMaterial* material, TextureType type)
    {
        if (material is null) return null;
        if (assimp.GetMaterialTextureCount(material, type) == 0)
        {
            // Assimp versions differ in whether glTF base-color maps are
            // exposed as BaseColor or the legacy Diffuse slot.
            if (type != TextureType.BaseColor || assimp.GetMaterialTextureCount(material, TextureType.Diffuse) == 0)
                return null;
            type = TextureType.Diffuse;
        }

        AssimpString path;
        TextureMapping mapping;
        uint uvIndex;
        float blend;
        TextureOp op;
        TextureMapMode mapMode;
        uint flags;
        if (assimp.GetMaterialTexture(material, type, 0, &path, &mapping, &uvIndex, &blend, &op, &mapMode, &flags) != Return.Success)
            return null;

        string texturePath = path.AsString;
        if (texturePath.StartsWith('*') && uint.TryParse(texturePath[1..], out uint embeddedIndex) && embeddedIndex < scene->MNumTextures)
        {
            return DecodeTexture(scene->MTextures[embeddedIndex]);
        }

        string? resolved = ResolvePath(texturePath);
        return resolved is null ? null : DecodeImage(System.IO.File.ReadAllBytes(resolved));
    }

    private static ModelTexture? DecodeTexture(AssimpTexture* texture)
    {
        if (texture is null) return null;
        if (texture->MHeight == 0)
        {
            var encoded = new byte[checked((int)texture->MWidth)];
            Marshal.Copy((nint)texture->PcData, encoded, 0, encoded.Length);
            return DecodeImage(encoded);
        }

        int pixelCount = checked((int)(texture->MWidth * texture->MHeight));
        var rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            Texel texel = texture->PcData[i];
            int offset = i * 4;
            rgba[offset] = texel.R;
            rgba[offset + 1] = texel.G;
            rgba[offset + 2] = texel.B;
            rgba[offset + 3] = texel.A;
        }
        return new ModelTexture
        {
            Width = checked((int)texture->MWidth),
            Height = checked((int)texture->MHeight),
            Pixels = rgba
        };
    }

    private static ModelTexture? DecodeImage(byte[] encoded)
    {
        try
        {
            using var image = Image.Load<Rgba32>(encoded);
            var pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);
            return new ModelTexture { Width = image.Width, Height = image.Height, Pixels = pixels };
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
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
