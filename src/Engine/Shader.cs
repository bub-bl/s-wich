using System.Text.RegularExpressions;

namespace Crowbar.Engine;

public enum ShaderStageKind
{
    Vertex,
    Fragment,
    Compute
}

public sealed record ShaderEntryPoint(string Name, ShaderStageKind Stage);

/// <summary>
/// A shader source loaded from a file shipped with the application.
/// </summary>
public sealed partial class Shader
{
    private Shader(string requestedPath, string filePath, string source)
    {
        Path = requestedPath;
        FilePath = filePath;
        Source = source;
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        EntryPoints = DetectEntryPoints(source);
        Parameters = DetectParameters(source);
    }

    public string Path { get; }

    public string FilePath { get; }

    public string Name { get; }

    public string Source { get; }

    public IReadOnlyList<ShaderEntryPoint> EntryPoints { get; }

    public IReadOnlyList<ShaderParameter> Parameters { get; }

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

    private static IReadOnlyList<ShaderParameter> DetectParameters(string source)
    {
        var uniform = UniformPattern.Match(source);
        if (!uniform.Success) return [];

        return FieldPattern.Matches(uniform.Groups["body"].Value)
            .Select(match => TryParseParameter(match.Groups["name"].Value, match.Groups["type"].Value))
            .Where(parameter => parameter != null)
            .Select(parameter => parameter!)
            .ToArray();
    }

    private static ShaderParameter? TryParseParameter(string name, string type)
    {
        return !Enum.TryParse(type switch
        {
            "f32" => nameof(ShaderParameterType.Float),
            "vec2<f32>" => nameof(ShaderParameterType.Vector2),
            "vec3<f32>" => nameof(ShaderParameterType.Vector3),
            "vec4<f32>" => nameof(ShaderParameterType.Vector4),
            "i32" => nameof(ShaderParameterType.Int),
            "u32" => nameof(ShaderParameterType.UInt),
            "bool" => nameof(ShaderParameterType.Bool),
            _ => string.Empty
        }, out ShaderParameterType parameterType)
            ? null
            : new ShaderParameter(name, parameterType);
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