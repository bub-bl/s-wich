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
    }

    public string Path { get; }

    public string FilePath { get; }

    public string Name { get; }

    public string Source { get; }

    public IReadOnlyList<ShaderEntryPoint> EntryPoints { get; }

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
            ? new[] { path }
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
        return EntryPointPattern.Matches(source)
            .Select(match => new ShaderEntryPoint(
                match.Groups["name"].Value,
                Enum.Parse<ShaderStageKind>(match.Groups["stage"].Value, ignoreCase: true)))
            .ToArray();
    }

    private static readonly Regex EntryPointPattern = new(
        @"(?m)^\s*@(?<stage>vertex|fragment|compute)\s+fn\s+(?<name>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
