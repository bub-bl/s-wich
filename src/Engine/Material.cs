using System.Numerics;

namespace Crowbar.Engine;

/// <summary>
/// Values supplied to a shader for a renderable object.
/// The renderer is responsible for packing these values into GPU resources.
/// </summary>
public sealed class Material
{
    private readonly Dictionary<string, ShaderParameter> _values = [with(StringComparer.Ordinal)];

    public string Name { get; }
    public Shader Shader { get; }
    public IReadOnlyDictionary<string, ShaderParameter> Values => _values;

    private Material(string name, Shader shader)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A material needs a name.", nameof(name))
            : name;
        Shader = shader ?? throw new ArgumentNullException(nameof(shader));
    }

    public static Material FromShader(string shaderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);

        var shaderPath = shaderName;

        if (!Path.HasExtension(shaderPath))
        {
            shaderPath += ".wgsl";
            if (!shaderPath.Contains(Path.DirectorySeparatorChar) && !shaderPath.Contains(Path.AltDirectorySeparatorChar))
                shaderPath = Path.Combine("Shaders", shaderPath);
        }

        var shader = Shader.Load(shaderPath);
        return new Material(shader.Name, shader);
    }

    public Material Set(string parameterName, ShaderParameter value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        var parameter = Shader.Parameters.FirstOrDefault(p => p.Name == parameterName)
                        ?? throw new ArgumentException(
                            $"Shader '{Shader.Name}' has no parameter named '{parameterName}'.", nameof(parameterName));

        _values[parameterName] = value;
        return this;
    }

    public bool TryGet<T>(string parameterName, out T value)
    {
        if (_values.TryGetValue(parameterName, out var raw) && TryGetValue(raw, out T typed))
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public T Get<T>(string parameterName, T fallback = default!) =>
        TryGet(parameterName, out T value) ? value : fallback;

    public static Material CreateDefault(Shader shader) => new Material("Default", shader)
        .Set("color", new Vector4(0.2f, 0.6f, 1.0f, 1.0f))
        .Set("lightDir", Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.7f)));

    private static bool TryGetValue<T>(ShaderParameter parameter, out T value)
    {
        if (parameter.Value is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }
}
