using System.Numerics;

namespace Crowbar.Engine;

/// <summary>
/// Formats understood by the model loader.
/// </summary>
public enum ModelFormat
{
    Obj
}

/// <summary>
/// A CPU-side model loaded from an asset file.
/// </summary>
public sealed class Model
{
    private Model(string requestedPath, string filePath, ModelFormat format, IReadOnlyList<ModelMesh> meshes)
    {
        Path = requestedPath;
        FilePath = filePath;
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        Format = format;
        Meshes = meshes;
    }

    /// <summary>The path supplied to <see cref="Load"/>.</summary>
    public string Path { get; }

    /// <summary>The resolved path of the loaded file.</summary>
    public string FilePath { get; }

    public string Name { get; }
    public ModelFormat Format { get; }
    public IReadOnlyList<ModelMesh> Meshes { get; }

    public static Model Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string extension = System.IO.Path.GetExtension(path);
        if (!extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Model format '{extension}' is not supported yet. Supported formats: .obj.");
        }

        string? filePath = ResolvePath(path);
        if (filePath == null)
            throw new FileNotFoundException($"Model file '{path}' was not found.", path);

        return new Model(path, filePath, ModelFormat.Obj, ObjModelLoader.Load(filePath));
    }

    private static string? ResolvePath(string path)
    {
        if (System.IO.Path.IsPathRooted(path))
            return File.Exists(path) ? System.IO.Path.GetFullPath(path) : null;

        string[] candidates =
        [
            System.IO.Path.Combine(AppContext.BaseDirectory, path),
            System.IO.Path.GetFullPath(path)
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// A renderable mesh contained in a <see cref="Model"/>.
/// Every three consecutive indices form one triangle.
/// </summary>
public sealed class ModelMesh
{
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

    public string Name { get; }
    public IReadOnlyList<Vector3> Positions { get; }
    public IReadOnlyList<Vector3> Normals { get; }
    public IReadOnlyList<Vector2> TextureCoordinates { get; }
    public IReadOnlyList<int> Indices { get; }
}

internal static class ObjModelLoader
{
    private readonly record struct VertexKey(int Position, int TextureCoordinate, int Normal);

    private sealed class MeshBuilder(string name)
    {
        private readonly Dictionary<VertexKey, int> _vertices = [];
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<Vector2> _textureCoordinates = [];
        private readonly List<int> _indices = [];

        public string Name { get; } = name;

        public void AddTriangle(
            (int position, int textureCoordinate, int normal) a,
            (int position, int textureCoordinate, int normal) b,
            (int position, int textureCoordinate, int normal) c,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector2> textureCoordinates,
            IReadOnlyList<Vector3> normals)
        {
            AddVertex(a, positions, textureCoordinates, normals);
            AddVertex(b, positions, textureCoordinates, normals);
            AddVertex(c, positions, textureCoordinates, normals);
        }

        public ModelMesh Build()
        {
            bool hasNormals = _normals.Any(normal => normal.LengthSquared() > 0.000001f);
            if (!hasNormals)
            {
                foreach (int i in Enumerable.Range(0, _indices.Count).Where(i => i % 3 == 0))
                {
                    Vector3 a = _positions[_indices[i]];
                    Vector3 b = _positions[_indices[i + 1]];
                    Vector3 c = _positions[_indices[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (normal.LengthSquared() > 0.000001f)
                        normal = Vector3.Normalize(normal);

                    _normals[_indices[i]] += normal;
                    _normals[_indices[i + 1]] += normal;
                    _normals[_indices[i + 2]] += normal;
                }

                for (int i = 0; i < _normals.Count; i++)
                    _normals[i] = _normals[i].LengthSquared() > 0.000001f
                        ? Vector3.Normalize(_normals[i])
                        : Vector3.UnitY;
            }

            return new ModelMesh(Name, _positions, _normals, _textureCoordinates, _indices);
        }

        private void AddVertex(
            (int position, int textureCoordinate, int normal) value,
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector2> textureCoordinates,
            IReadOnlyList<Vector3> normals)
        {
            var key = new VertexKey(value.position, value.textureCoordinate, value.normal);
            if (!_vertices.TryGetValue(key, out int index))
            {
                index = _positions.Count;
                _vertices.Add(key, index);
                _positions.Add(positions[value.position]);
                _textureCoordinates.Add(value.textureCoordinate >= 0
                    ? textureCoordinates[value.textureCoordinate]
                    : Vector2.Zero);
                _normals.Add(value.normal >= 0 ? normals[value.normal] : Vector3.Zero);
            }

            _indices.Add(index);
        }
    }

    public static IReadOnlyList<ModelMesh> Load(string filePath)
    {
        List<Vector3> positions = [];
        List<Vector2> textureCoordinates = [];
        List<Vector3> normals = [];
        List<ModelMesh> meshes = [];
        MeshBuilder builder = new("Default");
        bool hasFaces = false;

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "vt" when parts.Length >= 3:
                    textureCoordinates.Add(new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2])));
                    break;
                case "vn" when parts.Length >= 4:
                    normals.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    break;
                case "o" or "g" when parts.Length >= 2:
                    if (hasFaces) meshes.Add(builder.Build());
                    builder = new MeshBuilder(parts[1]);
                    hasFaces = false;
                    break;
                case "f" when parts.Length >= 4:
                    var face = parts[1..].Select(part => ParseVertex(part, positions.Count,
                        textureCoordinates.Count, normals.Count)).ToArray();
                    for (int i = 1; i < face.Length - 1; i++)
                    {
                        builder.AddTriangle(face[0], face[i], face[i + 1], positions,
                            textureCoordinates, normals);
                        hasFaces = true;
                    }
                    break;
            }
        }

        if (hasFaces) meshes.Add(builder.Build());
        return meshes;
    }

    private static (int position, int textureCoordinate, int normal) ParseVertex(
        string value, int positionCount, int textureCoordinateCount, int normalCount)
    {
        string[] parts = value.Split('/');
        int position = ResolveIndex(parts[0], positionCount, "position");
        int textureCoordinate = parts.Length > 1 && parts[1].Length > 0
            ? ResolveIndex(parts[1], textureCoordinateCount, "texture coordinate")
            : -1;
        int normal = parts.Length > 2 && parts[2].Length > 0
            ? ResolveIndex(parts[2], normalCount, "normal")
            : -1;
        return (position, textureCoordinate, normal);
    }

    private static int ResolveIndex(string value, int count, string kind)
    {
        if (!int.TryParse(value, out int index) || index == 0)
            throw new FormatException($"Invalid OBJ {kind} index '{value}'.");

        int resolved = index > 0 ? index - 1 : count + index;
        if ((uint)resolved >= (uint)count)
            throw new FormatException($"OBJ {kind} index '{value}' is out of range.");
        return resolved;
    }

    private static float ParseFloat(string value) =>
        float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
