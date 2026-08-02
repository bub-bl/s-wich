namespace Crowbar.Engine;

internal static class ModelLoader
{
    internal static Model Load(string requestedPath)
    {
        string filePath = ResolvePath(requestedPath)
            ?? throw new FileNotFoundException($"Model file '{requestedPath}' was not found.", requestedPath);
        ModelFormat format = GetFormat(filePath);

        if (format is ModelFormat.Gltf or ModelFormat.Glb && GltfModelReader.TryRead(filePath, out GltfImportData? gltf) && gltf is not null)
        {
            if (gltf.Meshes.Count == 0)
                throw new FormatException($"Model '{requestedPath}' does not contain any renderable meshes.");
            return new Model(requestedPath, filePath, format, gltf.Meshes, gltf.Materials);
        }

        ModelImportData imported = AssimpModelImporter.Read(filePath);
        if (imported.Meshes.Count == 0)
            throw new FormatException($"Model '{requestedPath}' does not contain any renderable meshes.");
        return new Model(requestedPath, filePath, format, imported.Meshes, imported.Materials);
    }

    private static string? ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return File.Exists(path) ? Path.GetFullPath(path) : null;

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, path),
            Path.GetFullPath(path)
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static ModelFormat GetFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".obj" => ModelFormat.Obj,
        ".gltf" => ModelFormat.Gltf,
        ".glb" => ModelFormat.Glb,
        ".fbx" => ModelFormat.Fbx,
        _ => ModelFormat.Other
    };
}
