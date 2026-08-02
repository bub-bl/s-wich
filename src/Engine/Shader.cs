using System.Numerics;
using System.Text.RegularExpressions;

namespace Crowbar.Engine;

public enum ShaderStageKind
{
    Vertex,
    Fragment,
    Compute
}

public sealed record ShaderEntryPoint(string Name, ShaderStageKind Stage);

public readonly union ShaderParameter(float, Vector2, Vector3, Vector4, Matrix4x4, int, uint, bool)
{
    public string TypeName => this switch
    {
        float => "f32",
        int => "i32",
        uint => "u32",
        bool => "bool",
        Vector2 => "vec2f",
        Vector3 => "vec3f",
        Vector4 => "vec4f",
        Matrix4x4 => "mat4f",
        _ => throw new NotImplementedException()
    };

    public static ShaderParameterDefinition? TryParseParameter(string name, string type)
    {
        return type switch
        {
            "f32" => new ShaderParameterDefinition(name, typeof(float)),
            "vec2<f32>" or "vec2f" => new ShaderParameterDefinition(name, typeof(Vector2)),
            "vec3<f32>" or "vec3f" => new ShaderParameterDefinition(name, typeof(Vector3)),
            "vec4<f32>" or "vec4f" => new ShaderParameterDefinition(name, typeof(Vector4)),
            "mat4x4<f32>" or "mat4f" => new ShaderParameterDefinition(name, typeof(Matrix4x4)),
            "i32" => new ShaderParameterDefinition(name, typeof(int)),
            "u32" => new ShaderParameterDefinition(name, typeof(uint)),
            "bool" => new ShaderParameterDefinition(name, typeof(bool)),
            _ => null,
        };
    }
}

public sealed record ShaderParameterDefinition(string Name, Type Type);

/// <summary>
/// A shader source loaded from a file shipped with the application.
/// </summary>
public sealed partial class Shader
{
    public string Path { get; }

    public string FilePath { get; }

    public string Name { get; }

    public string Source { get; }

    public IReadOnlyList<ShaderEntryPoint> EntryPoints { get; }

    public IReadOnlyList<ShaderParameterDefinition> Parameters { get; }

    private Shader(string requestedPath, string filePath, string source)
    {
        Path = requestedPath;
        FilePath = filePath;
        Source = source;
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        EntryPoints = DetectEntryPoints(source);
        Parameters = DetectParameters(source);
    }

    public ShaderEntryPoint GetEntryPoint(string name)
    {
        return EntryPoints.FirstOrDefault(entry => entry.Name == name)
               ?? throw new InvalidOperationException(
                   $"Shader '{Name}' does not contain an entry point named '{name}'.");
    }

    public static Shader Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var candidates = System.IO.Path.IsPathRooted(path)
            ? [path]
            : new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, path),
                System.IO.Path.GetFullPath(path)
            };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new Shader(path, candidate, File.ReadAllText(candidate));
            }
        }

        throw new FileNotFoundException(
            $"Shader file '{path}' was not found.",
            path);
    }

    private static IReadOnlyList<ShaderEntryPoint> DetectEntryPoints(string source)
    {
        return
        [
            .. EntryPointPattern.Matches(source)
                .Select(match => new ShaderEntryPoint(
                    match.Groups["name"].Value,
                    Enum.Parse<ShaderStageKind>(match.Groups["stage"].Value, ignoreCase: true)))
        ];
    }

    private static IReadOnlyList<ShaderParameterDefinition> DetectParameters(string source)
    {
        var uniform = UniformPattern.Match(source);
        if (!uniform.Success) return [];

        return
        [
            .. FieldPattern.Matches(uniform.Groups["body"].Value)
                .Select(match =>
                    ShaderParameter.TryParseParameter(match.Groups["name"].Value, match.Groups["type"].Value))
                .Where(parameter => parameter != null)
                .Select(parameter => parameter!)
        ];
    }

    [GeneratedRegex(@"(?m)^\s*@(?<stage>vertex|fragment|compute)\s+fn\s+(?<name>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex EntryPointPattern { get; }

    [GeneratedRegex(@"(?s)struct\s+\w*Uniforms\s*\{(?<body>.*?)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex UniformPattern { get; }

    [GeneratedRegex(@"(?m)^\s*(?<name>[A-Za-z_]\w*)\s*:\s*(?<type>[A-Za-z0-9_<>]+)\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex FieldPattern { get; }
}