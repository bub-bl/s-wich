using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace Crowbar.Engine;

internal static class GltfModelReader
{
    internal static bool TryRead(string filePath, out GltfImportData? result)
    {
        try
        {
            result = Read(filePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or FormatException or InvalidDataException)
        {
            result = null;
            return false;
        }
    }

    private static GltfImportData Read(string filePath)
    {
        GltfDocument document = GltfDocument.Load(filePath);
        var meshes = new List<ModelMesh>();

        foreach (JsonElement meshElement in document.Root.GetProperty("meshes").EnumerateArray())
        {
            string meshName = meshElement.TryGetProperty("name", out JsonElement name)
                ? name.GetString() ?? "Mesh"
                : "Mesh";

            foreach (JsonElement primitive in meshElement.GetProperty("primitives").EnumerateArray())
            {
                JsonElement attributes = primitive.GetProperty("attributes");
                Vector3[] positions = document.ReadVector3(attributes.GetProperty("POSITION").GetInt32());
                Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normal)
                    ? document.ReadVector3(normal.GetInt32())
                    : Enumerable.Repeat(Vector3.UnitY, positions.Length).ToArray();
                Vector4[] tangents = attributes.TryGetProperty("TANGENT", out JsonElement tangent)
                    ? document.ReadVector4(tangent.GetInt32())
                    : Enumerable.Repeat(new Vector4(Vector3.UnitX, 1f), positions.Length).ToArray();
                Vector2[] uv = attributes.TryGetProperty("TEXCOORD_0", out JsonElement texcoord)
                    ? document.ReadVector2(texcoord.GetInt32())
                    : new Vector2[positions.Length];
                int[] indices = document.ReadIndices(primitive.GetProperty("indices").GetInt32());
                int materialIndex = primitive.TryGetProperty("material", out JsonElement material)
                    ? material.GetInt32()
                    : 0;

                ValidateAttributeCounts(positions, normals, tangents, uv);
                meshes.Add(new ModelMesh(meshName, positions, normals, tangents, uv, indices, materialIndex));
            }
        }

        return new GltfImportData
        {
            Meshes = meshes,
            Materials = document.ReadMaterials()
        };
    }

    private static void ValidateAttributeCounts(Vector3[] positions, Vector3[] normals, Vector4[] tangents, Vector2[] uv)
    {
        if (normals.Length != positions.Length || tangents.Length != positions.Length || uv.Length != positions.Length)
            throw new FormatException("A glTF primitive has vertex attributes with different lengths.");
    }

    private sealed class GltfDocument : IDisposable
    {
        private readonly JsonDocument _json;
        private readonly byte[] _binary;
        private readonly string _directory;

        internal JsonElement Root => _json.RootElement;

        private GltfDocument(JsonDocument json, byte[] binary, string directory)
        {
            _json = json;
            _binary = binary;
            _directory = directory;
        }

        internal static GltfDocument Load(string filePath)
        {
            byte[] file = File.ReadAllBytes(filePath);
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;

            return Path.GetExtension(filePath).Equals(".glb", StringComparison.OrdinalIgnoreCase)
                ? LoadGlb(file, directory)
                : LoadGltf(file, directory);
        }

        private static GltfDocument LoadGltf(byte[] file, string directory)
        {
            var json = JsonDocument.Parse(file);
            JsonElement buffers = json.RootElement.GetProperty("buffers");
            byte[] binary = buffers.GetArrayLength() == 0
                ? []
                : ReadUri(buffers[0].GetProperty("uri").GetString() ?? string.Empty, directory);
            return new GltfDocument(json, binary, directory);
        }

        private static GltfDocument LoadGlb(byte[] file, string directory)
        {
            if (file.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(file) != 0x46546C67)
                throw new FormatException("Invalid glTF binary header.");

            JsonDocument? json = null;
            byte[] binary = [];
            int offset = 12;
            while (offset + 8 <= file.Length)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset)));
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset + 4));
                offset += 8;
                if (length < 0 || offset + length > file.Length) throw new FormatException("Invalid glTF chunk length.");

                if (type == 0x4E4F534A)
                    json = JsonDocument.Parse(file.AsSpan(offset, length).ToArray());
                else if (type == 0x004E4942)
                    binary = file.AsSpan(offset, length).ToArray();
                offset += length;
            }

            return new GltfDocument(json ?? throw new FormatException("Missing glTF JSON chunk."), binary, directory);
        }

        internal Vector3[] ReadVector3(int accessorIndex) => ReadVector(accessorIndex, 3)
            .Select(values => new Vector3(values[0], values[1], values[2])).ToArray();

        internal Vector2[] ReadVector2(int accessorIndex) => ReadVector(accessorIndex, 2)
            .Select(values => new Vector2(values[0], values[1])).ToArray();

        internal Vector4[] ReadVector4(int accessorIndex) => ReadVector(accessorIndex, 4)
            .Select(values => new Vector4(values[0], values[1], values[2], values[3])).ToArray();

        private IEnumerable<float[]> ReadVector(int accessorIndex, int expectedComponents)
        {
            AccessorData data = GetAccessor(accessorIndex, expectedComponents);
            for (int index = 0; index < data.Count; index++)
            {
                int offset = index * data.Stride;
                var values = new float[expectedComponents];
                for (int component = 0; component < expectedComponents; component++)
                    values[component] = BitConverter.ToSingle(data.Bytes, offset + component * sizeof(float));
                yield return values;
            }
        }

        internal int[] ReadIndices(int accessorIndex)
        {
            AccessorData data = GetAccessor(accessorIndex, 1);
            int[] indices = new int[data.Count];
            for (int index = 0; index < data.Count; index++)
            {
                int offset = index * data.Stride;
                indices[index] = data.ComponentSize switch
                {
                    1 => data.Bytes[offset],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(data.Bytes.AsSpan(offset, 2)),
                    4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Bytes.AsSpan(offset, 4))),
                    _ => throw new FormatException("Unsupported glTF index component size.")
                };
            }
            return indices;
        }

        private AccessorData GetAccessor(int accessorIndex, int expectedComponents)
        {
            JsonElement accessor = Root.GetProperty("accessors")[accessorIndex];
            int componentType = accessor.GetProperty("componentType").GetInt32();
            int componentSize = componentType switch
            {
                5126 => 4,
                5121 => 1,
                5123 => 2,
                5125 => 4,
                _ => throw new FormatException($"Unsupported glTF component type '{componentType}'.")
            };
            string type = accessor.GetProperty("type").GetString()!;
            int componentCount = type switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                _ => throw new FormatException($"Unsupported glTF accessor type '{type}'.")
            };
            if (componentCount != expectedComponents || componentType != 5126 && expectedComponents != 1)
                throw new FormatException("Unsupported or incompatible glTF accessor.");

            JsonElement view = Root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
            int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;
            int packedSize = checked(componentCount * componentSize);
            int stride = view.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : packedSize;
            int count = accessor.GetProperty("count").GetInt32();
            int length = count == 0 ? 0 : checked((count - 1) * stride + packedSize);
            int start = checked(viewOffset + accessorOffset);
            if (start < 0 || length < 0 || start + length > _binary.Length)
                throw new FormatException("glTF accessor is outside its binary buffer.");
            return new AccessorData(_binary.AsSpan(start, length).ToArray(), count, stride, componentSize);
        }

        internal IReadOnlyList<ModelMaterial> ReadMaterials()
        {
            if (!Root.TryGetProperty("materials", out JsonElement elements)) return [];
            var materials = new List<ModelMaterial>(elements.GetArrayLength());
            foreach (JsonElement element in elements.EnumerateArray())
            {
                JsonElement pbr = element.TryGetProperty("pbrMetallicRoughness", out JsonElement pbrElement) ? pbrElement : default;
                materials.Add(new ModelMaterial
                {
                    BaseColorFactor = ReadColorFactor(pbr),
                    MetallicFactor = ReadFloat(pbr, "metallicFactor", 1f),
                    RoughnessFactor = ReadFloat(pbr, "roughnessFactor", 1f),
                    BaseColorTexture = ReadTexture(pbr, "baseColorTexture"),
                    MetallicRoughnessTexture = ReadTexture(pbr, "metallicRoughnessTexture"),
                    NormalTexture = ReadTexture(element, "normalTexture")
                });
            }
            return materials;
        }

        private ModelTexture? ReadTexture(JsonElement owner, string property)
        {
            if (!owner.TryGetProperty(property, out JsonElement reference) ||
                !reference.TryGetProperty("index", out JsonElement index) ||
                !Root.TryGetProperty("textures", out JsonElement textures)) return null;
            JsonElement texture = textures[index.GetInt32()];
            if (!texture.TryGetProperty("source", out JsonElement source) || !Root.TryGetProperty("images", out JsonElement images)) return null;
            JsonElement image = images[source.GetInt32()];

            byte[] bytes;
            if (image.TryGetProperty("uri", out JsonElement uri))
                bytes = ReadUri(uri.GetString() ?? string.Empty, _directory);
            else
            {
                int viewIndex = image.GetProperty("bufferView").GetInt32();
                JsonElement view = Root.GetProperty("bufferViews")[viewIndex];
                int offset = view.TryGetProperty("byteOffset", out JsonElement value) ? value.GetInt32() : 0;
                int length = view.GetProperty("byteLength").GetInt32();
                bytes = _binary.AsSpan(offset, length).ToArray();
            }
            return TextureDecoder.Decode(bytes);
        }

        private static Vector4 ReadColorFactor(JsonElement pbr)
        {
            if (!pbr.TryGetProperty("baseColorFactor", out JsonElement factor)) return Vector4.One;
            float[] values = factor.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            return values.Length == 4 ? new Vector4(values[0], values[1], values[2], values[3]) : Vector4.One;
        }

        private static float ReadFloat(JsonElement owner, string property, float fallback) =>
            owner.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? value.GetSingle()
                : fallback;

        private static byte[] ReadUri(string uri, string directory)
        {
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = uri.IndexOf(',');
                if (comma < 0) throw new FormatException("Invalid glTF data URI.");
                return Convert.FromBase64String(uri[(comma + 1)..]);
            }
            string path = Path.GetFullPath(Path.Combine(directory, uri.Replace('/', Path.DirectorySeparatorChar)));
            return File.ReadAllBytes(path);
        }

        public void Dispose() => _json.Dispose();

        private readonly record struct AccessorData(byte[] Bytes, int Count, int Stride, int ComponentSize);
    }
}
