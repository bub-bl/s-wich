namespace Crowbar.Engine;

internal class ModelImportData
{
    internal required IReadOnlyList<ModelMesh> Meshes { get; init; }
    internal required IReadOnlyList<ModelMaterial> Materials { get; init; }
}

internal sealed class GltfImportData : ModelImportData
{
}
