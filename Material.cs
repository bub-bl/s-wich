using System.Numerics;

namespace Crowbar;

public enum ShaderParameterType
{
    Float,
    Vector2,
    Vector3,
    Vector4,
    Int,
    UInt,
    Bool
}

public sealed record ShaderParameter(string Name, ShaderParameterType Type);

/// <summary>
/// Values supplied to a shader for a renderable object.
/// The renderer is responsible for packing these values into GPU resources.
/// </summary>
public sealed class Material
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public Material(string name, Shader shader)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A material needs a name.", nameof(name))
            : name;
        Shader = shader ?? throw new ArgumentNullException(nameof(shader));
    }

    public string Name { get; }
    public Shader Shader { get; }
    public IReadOnlyDictionary<string, object> Values => _values;

    public static Material FromShader(string shaderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);

        string shaderPath = shaderName;
        if (!Path.HasExtension(shaderPath))
        {
            shaderPath += ".wgsl";
            if (!shaderPath.Contains(Path.DirectorySeparatorChar) && !shaderPath.Contains(Path.AltDirectorySeparatorChar))
                shaderPath = Path.Combine("Shaders", shaderPath);
        }

        Shader shader = Shader.Load(shaderPath);
        return new Material(shader.Name, shader);
    }

    public Material Set<T>(string parameterName, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ShaderParameter parameter = Shader.Parameters.FirstOrDefault(p => p.Name == parameterName)
            ?? throw new ArgumentException(
                $"Shader '{Shader.Name}' has no parameter named '{parameterName}'.", nameof(parameterName));

        if (!IsCompatible(parameter.Type, value))
            throw new ArgumentException(
                $"Value for '{parameterName}' is not compatible with shader type '{parameter.Type}'.", nameof(value));

        _values[parameterName] = value!;
        return this;
    }

    public bool TryGet<T>(string parameterName, out T value)
    {
        if (_values.TryGetValue(parameterName, out object? raw) && raw is T typed)
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

    private static bool IsCompatible(ShaderParameterType type, object? value) => type switch
    {
        ShaderParameterType.Float => value is float,
        ShaderParameterType.Vector2 => value is Vector2,
        ShaderParameterType.Vector3 => value is Vector3,
        ShaderParameterType.Vector4 => value is Vector4,
        ShaderParameterType.Int => value is int,
        ShaderParameterType.UInt => value is uint,
        ShaderParameterType.Bool => value is bool,
        _ => false
    };
}
