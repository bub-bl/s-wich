using System.IO;
using System.Text.RegularExpressions;

namespace Crowbar;

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
public sealed class Shader
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
        return [.. EntryPointPattern.Matches(source)
            .Select(match => new ShaderEntryPoint(
                match.Groups["name"].Value,
                Enum.Parse<ShaderStageKind>(match.Groups["stage"].Value, ignoreCase: true)))];
    }

    private static IReadOnlyList<ShaderParameter> DetectParameters(string source)
    {
        Match uniform = UniformPattern.Match(source);
        if (!uniform.Success) return Array.Empty<ShaderParameter>();

        return FieldPattern.Matches(uniform.Groups["body"].Value)
            .Select(match => TryParseParameter(match.Groups["name"].Value, match.Groups["type"].Value))
            .Where(parameter => parameter != null)
            .Select(parameter => parameter!)
            .ToArray();
    }

    private static ShaderParameter? TryParseParameter(string name, string type)
    {
        if (!Enum.TryParse(type switch
        {
            "f32" => nameof(ShaderParameterType.Float),
            "vec2<f32>" => nameof(ShaderParameterType.Vector2),
            "vec3<f32>" => nameof(ShaderParameterType.Vector3),
            "vec4<f32>" => nameof(ShaderParameterType.Vector4),
            "i32" => nameof(ShaderParameterType.Int),
            "u32" => nameof(ShaderParameterType.UInt),
            "bool" => nameof(ShaderParameterType.Bool),
            _ => string.Empty
        }, out ShaderParameterType parameterType))
        {
            return null;
        }

        return new ShaderParameter(name, parameterType);
    }

    private static readonly Regex EntryPointPattern = new(
        @"(?m)^\s*@(?<stage>vertex|fragment|compute)\s+fn\s+(?<name>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UniformPattern = new(
        @"(?s)struct\s+\w*Uniforms\s*\{(?<body>.*?)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FieldPattern = new(
        @"(?m)^\s*(?<name>[A-Za-z_]\w*)\s*:\s*(?<type>[A-Za-z0-9_<>]+)\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
