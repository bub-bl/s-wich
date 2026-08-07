using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crowbar.Engine.UI;

public abstract class RazorPanel : PanelComponent, IComponent
{
    // Razor's design-time component discovery identifies components through
    // IComponent. The native renderer does not use Blazor's render pipeline,
    // so these explicit no-op implementations only provide the standard
    // contract required by the Razor language services.
    void IComponent.Attach(RenderHandle renderHandle)
    {
    }

    Task IComponent.SetParametersAsync(ParameterView parameters) => Task.CompletedTask;

    public string? ScopeId { get; set; }

    /// <summary>
    /// Placeholder emitted by <see cref="Write"/> when a component renders its
    /// default <c>@ChildContent</c> fragment. The native parser replaces it
    /// with the panels captured from the markup between the component's tags.
    /// </summary>
    internal const string ChildContentMarker = "[[__CROWBAR_CHILDCONTENT__]]";

    private const string NamedFragmentMarkerPrefix = "[[__CROWBAR_FRAGMENT__:";

    /// <summary>Returns the marker text emitted for a <see cref="RenderFragment"/> parameter.</summary>
    internal static string FragmentMarker(string name) => name.Equals("ChildContent", StringComparison.OrdinalIgnoreCase)
        ? ChildContentMarker
        : NamedFragmentMarkerPrefix + name + "]]";

    /// <summary>
    /// Panels captured from the markup provided for the component's
    /// <see cref="RenderFragment"/> parameters (named regions like <c>Header</c>
    /// or the default <c>ChildContent</c>), keyed by parameter name.
    /// <see langword="null"/> panels mean the fragment is provided but empty.
    /// </summary>
    private readonly Dictionary<string, (IReadOnlyList<Panel>? Panels, string Signature)> _fragments =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps each live sentinel fragment instance to the parameter it stands for.</summary>
    private readonly Dictionary<RenderFragment, string> _fragmentNames = new();

    private int _fragmentVersion;
    private int _builtFragmentVersion = -1;

    // These two cover every fragment (ChildContent and named regions alike);
    // the names are kept for compatibility with earlier single-fragment builds.
    internal bool NeedsContentRebuild() => _fragmentVersion != _builtFragmentVersion;

    internal void MarkChildContentBuilt() => _builtFragmentVersion = _fragmentVersion;

    internal string? GetFragmentSignature(string name) =>
        _fragments.TryGetValue(name, out var fragment) ? fragment.Signature : null;

    internal IReadOnlyList<Panel>? GetFragmentPanels(string name) =>
        _fragments.TryGetValue(name, out var fragment) ? fragment.Panels : null;

    /// <summary>Names of the fragments currently provided to this component (snapshot).</summary>
    internal string[] ProvidedFragmentNames => [.. _fragments.Keys];

    /// <summary>
    /// True when <paramref name="elementName"/> can be a named child content
    /// region: a writable <c>[Parameter]</c> property of type
    /// <see cref="RenderFragment"/> on this component. Matching follows the
    /// engine's usual case-insensitive parameter resolution, so an element like
    /// <c>&lt;header&gt;</c> is consumed as a <c>Header</c> region when the
    /// component exposes such a fragment parameter.
    /// </summary>
    internal bool HasRenderFragmentParameter(string elementName)
    {
        var property = FindParameter(elementName);
        return property is not null && IsRazorParameter(property) && property.CanWrite &&
               typeof(RenderFragment).IsAssignableFrom(property.PropertyType);
    }

    private readonly StringBuilder _output = new();
    private string _attributeSuffix = string.Empty;
    private readonly Dictionary<string, RazorPanel> _childComponents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeChildren = new(StringComparer.Ordinal);
    private bool _initialized;

    protected void WriteLiteral(string value) => _output.Append(value);

    protected void Write(object? value)
    {
        // @ChildContent / @Header / ... compile to Write(fragment). The fragment
        // content is captured as panels by the parent's parser; emitting a named
        // placeholder here lets the parser splice them into the child's tree at
        // the right spot. The sentinel instances registered by SetFragment are
        // how Write knows which parameter it is rendering.
        if (value is RenderFragment fragment)
        {
            if (_fragmentNames.TryGetValue(fragment, out var name))
            {
                _output.Append(FragmentMarker(name));
            }
            else if (_fragments.TryGetValue("ChildContent", out var childContent) &&
                     childContent.Panels is not null)
            {
                // Unregistered inline fragment with real ChildContent to splice:
                // keep the legacy fallback so the content still lands somewhere.
                _output.Append(ChildContentMarker);
            }

            return;
        }

        _output.Append(System.Net.WebUtility.HtmlEncode(value?.ToString() ?? string.Empty));
    }

    /// <summary>
    /// Captures the panels built from the markup provided for a fragment
    /// parameter (the default <c>ChildContent</c> or a named region like
    /// <c>Header</c>) and binds them to the matching writable <c>[Parameter]</c>
    /// property of type <see cref="RenderFragment"/>. A <see langword="null"/>
    /// (or empty) panel list clears a previously captured fragment so
    /// conditional regions disappear on re-render.
    /// </summary>
    internal void SetFragment(string name, IReadOnlyList<Panel>? panels, string signature)
    {
        _fragmentVersion++;
        _fragments[name] = (panels, signature);
        RemoveFragmentRegistration(name);
        var property = FindParameter(name);
        if (property is not null && !typeof(RenderFragment).IsAssignableFrom(property.PropertyType))
            property = null; // Not a fragment parameter; nothing to bind or reset.
        if (panels is not { Count: > 0 })
        {
            // No (or no longer any) content for this fragment: reset the
            // parameter so @Fragment renders nothing and no stale panels are
            // spliced.
            if (property is not null && IsRazorParameter(property) && property.CanWrite)
                property.SetValue(this, null);
            return;
        }

        if (property is null)
            throw new InvalidOperationException(
                $"{GetType().Name} does not expose a [Parameter] {name} property of type RenderFragment, " +
                "but markup was provided for it.");
        if (!IsRazorParameter(property))
            throw new InvalidOperationException(
                $"Razor property '{name}' on {GetType().Name} is not marked with [Parameter].");
        if (!property.CanWrite)
            throw new InvalidOperationException($"Razor parameter '{name}' on {GetType().Name} is read-only.");
        var sentinel = CreateFragmentSentinel();
        _fragmentNames[sentinel] = name;
        property.SetValue(this, sentinel);
    }

    private void RemoveFragmentRegistration(string name)
    {
        foreach (var key in _fragmentNames.Where(kv => kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                     .Select(kv => kv.Key).ToArray())
            _fragmentNames.Remove(key);
    }

    private static RenderFragment CreateFragmentSentinel()
    {
        // Each sentinel captures a unique token so delegate equality can never
        // conflate two fragments rendered by the same component.
        var token = Guid.NewGuid();
        return builder => { _ = token; };
    }

    // Helpers emitted by Razor for attributes containing C# expressions,
    // e.g. value="@name". The native renderer still receives plain markup;
    // these methods only reproduce the small writer contract needed by the
    // generated Razor class.
    protected void BeginWriteAttribute(string name, string prefix, int prefixOffset, string suffix, int suffixOffset,
        int attributeValuesCount)
    {
        _attributeSuffix = suffix;
        _output.Append(prefix);
    }

    protected void WriteAttributeValue(string prefix, int prefixOffset, object? value, int valueOffset, int valueLength,
        bool isLiteral)
    {
        _output.Append(prefix);
        var text = value?.ToString() ?? string.Empty;
        _output.Append(isLiteral ? text : System.Net.WebUtility.HtmlEncode(text));
    }

    protected void EndWriteAttribute() => _output.Append(_attributeSuffix);

    protected virtual void OnInitialized()
    {
    }

    protected virtual void OnParametersSet()
    {
    }

    protected virtual void OnAfterRender(bool firstRender)
    {
    }

    internal async Task<string> RenderMarkupAsync()
    {
        if (!_initialized)
        {
            _initialized = true;
            OnInitialized();
        }

        OnParametersSet();
        _output.Clear();
        await ExecuteAsync();
        return _output.ToString();
    }

    internal void NotifyRendered(bool firstRender) => OnAfterRender(firstRender);
    internal void BeginRenderPass() => _activeChildren.Clear();

    internal RazorPanel GetOrCreateChild(string key, Func<RazorPanel> factory)
    {
        if (!_childComponents.TryGetValue(key, out var child))
        {
            child = factory();
            _childComponents[key] = child;
        }

        _activeChildren.Add(key);
        return child;
    }

    internal void EndRenderPass()
    {
        foreach (var key in _childComponents.Keys.Where(key => !_activeChildren.Contains(key)).ToArray())
            _childComponents.Remove(key);
    }

    internal void SetParameter(string name, string value)
    {
        var property = FindParameter(name);
        if (property is not null)
        {
            if (!IsRazorParameter(property))
                throw new InvalidOperationException(
                    $"Razor property '{name}' on {GetType().Name} is not marked with [Parameter].");
            if (!property.CanWrite)
                throw new InvalidOperationException($"Razor parameter '{name}' on {GetType().Name} is read-only.");
            try
            {
                property.SetValue(this, ConvertParameter(value, property.PropertyType));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Razor parameter '{name}' on {GetType().Name} could not convert value '{value}'.", ex);
            }

            return;
        }

        throw new InvalidOperationException($"Razor parameter '{name}' was not found on {GetType().Name}.");
    }

    private static object? ConvertParameter(string value, Type type)
    {
        if (typeof(RenderFragment).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"Razor parameter of type {type.Name} cannot be set from a string attribute; " +
                "pass the content between the component tags instead.");
        return type == typeof(string) ? value : Convert.ChangeType(value, Nullable.GetUnderlyingType(type) ?? type);
    }

    private static bool IsRazorParameter(PropertyInfo property) =>
        property.IsDefined(typeof(Microsoft.AspNetCore.Components.ParameterAttribute), true);

    private PropertyInfo? FindParameter(string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var exact = GetType().GetProperty(name, flags);
        if (exact is not null) return exact;
        // Case-insensitive fallback (route parameters, attribute casing). When
        // several properties collide ignoring case, prefer the one declared on
        // the most derived type (e.g. a generated [Parameter] Id over Panel.Id).
        return GetType().GetProperties(flags | BindingFlags.IgnoreCase)
            .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.DeclaringType == GetType() ? 0 : 1)
            .ThenBy(p => p.Name.Equals(name, StringComparison.Ordinal) ? 0 : 1)
            .FirstOrDefault();
    }

    internal Action<string>? NavigationRequested { get; set; }

    protected void NavigateTo(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        NavigationRequested?.Invoke(url);
    }

    // The Razor SDK generates a design-time declaration for .razor files.
    // That declaration contains the component shape but not the generated
    // ExecuteAsync body, so the base must remain instantiable from the IDE's
    // point of view. Runtime-compiled components override this method with
    // the real Razor output.
    public virtual Task ExecuteAsync() => Task.CompletedTask;
}

/// <summary>Compatibility name for components compiled by earlier versions.</summary>
public abstract class RazorTemplateBase : RazorPanel
{
}

public interface IRazorComponentCompiler
{
    PanelComponent Compile(string razorSource, string className, Type baseType, params Assembly[] references);
}

public sealed class RazorComponentFactory(IReadOnlyDictionary<string, Func<RazorPanel>>? components = null)
    : IRazorComponentCompiler
{
    private readonly IReadOnlyDictionary<string, Func<RazorPanel>> _components =
        components ?? new Dictionary<string, Func<RazorPanel>>(StringComparer.OrdinalIgnoreCase);

    public PanelComponent Compile(string razorSource, string className, Type baseType, params Assembly[] references)
    {
        var template = CompileTemplate(razorSource, className, baseType, references);
        return BuildTree(template);
    }

    public RazorPanel CompileTemplate(string razorSource, string className, Type baseType, params Assembly[] references)
    {
        var assembly = CompileAssembly(razorSource, className, baseType, references);
        return CreateTemplate(assembly, className);
    }

    /// <summary>
    /// Compiles the component from a file, caching the emitted assembly by
    /// (path, className, write time) so hot reloads of unchanged files skip the
    /// Roslyn emit. A fresh template instance is created on every call.
    /// </summary>
    public RazorPanel CompileTemplateFromFile(string razorPath, string className, Type baseType,
        params Assembly[] references)
    {
        razorPath = Path.GetFullPath(razorPath);
        var writeTime = File.GetLastWriteTimeUtc(razorPath).Ticks;
        var cacheKey = razorPath + "|" + className + "|" + (baseType.FullName ?? baseType.Name);
        var assembly = TemplateAssemblyCache.Get(cacheKey, writeTime);
        if (assembly is null)
        {
            assembly = CompileAssembly(ReadStableFileText(razorPath), className, baseType, references);
            TemplateAssemblyCache.Set(cacheKey, writeTime, assembly);
        }

        return CreateTemplate(assembly, className);
    }

    private static Assembly CompileAssembly(string razorSource, string className, Type baseType,
        params Assembly[] references)
    {
        // Event and binding expressions are intentionally converted to stable
        // markers before Razor parses the document. This keeps the generated
        // class strongly typed for @code, @if, @foreach and expressions while
        // allowing the native Panel tree to attach delegates after rendering.
        var directives = ExtractDirectives(razorSource);
        var source = directives.Source;
        var classMembers = NormalizeRuntimeTypeNames(directives.ClassMembers);
        baseType = ResolveBaseType(directives.BaseTypeName, baseType, references, directives.Usings);
        if (!typeof(RazorPanel).IsAssignableFrom(baseType))
            throw new InvalidOperationException($"Razor base type '{baseType.FullName}' must derive from RazorPanel.");
        if (baseType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                Type.EmptyTypes, null) is null)
            throw new InvalidOperationException(
                $"Razor base type '{baseType.FullName}' must have a parameterless constructor.");
        source = RewriteUiAttributes(source);
        var document = RazorSourceDocument.Create(source, className + ".razor");
        var project = RazorProjectEngine.Create(RazorConfiguration.Default,
            RazorProjectFileSystem.Create(AppContext.BaseDirectory), b =>
            {
                b.SetNamespace(directives.NamespaceName ?? "Crowbar.Engine.UI.Generated");
                b.SetBaseType(typeof(RazorPanel).FullName!);
            });
        var codeDocument = project.Process(document, className + ".razor", [], []);
        var generatedCode = codeDocument.GetCSharpDocument().GeneratedCode;
        var defaultUsings = new[]
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Threading",
            "System.Threading.Tasks"
        };
        var generatedUsings = defaultUsings.Concat(directives.Usings).Distinct(StringComparer.Ordinal);
        generatedCode = string.Join(Environment.NewLine, generatedUsings.Select(usingName => $"using {usingName};")) +
                        Environment.NewLine + generatedCode;
        if (!string.IsNullOrWhiteSpace(classMembers))
        {
            var generatedTree = CSharpSyntaxTree.ParseText(generatedCode);
            var generatedClass = generatedTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();
            if (generatedClass is null)
                throw new InvalidOperationException("Razor output did not contain a generated class.");
            generatedCode = generatedCode.Insert(generatedClass.CloseBraceToken.SpanStart, "\n" + classMembers + "\n");
        }

        var interfaceTypes = directives.Interfaces
            .Select(name => ResolveType(name, references, "interface", directives.Usings)).ToArray();
        var generatedTreeWithContracts = CSharpSyntaxTree.ParseText(generatedCode);
        var generatedClassWithContracts = generatedTreeWithContracts.GetRoot().DescendantNodes()
                                              .OfType<ClassDeclarationSyntax>().FirstOrDefault()
                                          ?? throw new InvalidOperationException(
                                              "Razor output did not contain a generated class.");
        var generatedBaseTypes = new List<BaseTypeSyntax>
        {
            SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseType.FullName!))
        };
        generatedBaseTypes.AddRange(interfaceTypes.Select(type =>
            SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(type.FullName!))));
        var classWithContracts = generatedClassWithContracts.WithBaseList(
            SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(generatedBaseTypes)));
        generatedCode = generatedTreeWithContracts.GetRoot()
            .ReplaceNode(generatedClassWithContracts, classWithContracts).ToFullString();
        var tree = CSharpSyntaxTree.ParseText(generatedCode);
        var platformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
        var assemblyReferences = references.Concat([
                typeof(object).Assembly, typeof(Enumerable).Assembly,
                typeof(RazorPanel).Assembly, typeof(RazorProjectEngine).Assembly
            ])
            .Distinct().Select(a => MetadataReference.CreateFromFile(a.Location)).Concat(platformReferences);
        var compilation = CSharpCompilation.Create(
            "Crowbar.Razor." + Guid.NewGuid().ToString("N"), [tree], assemblyReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException("Razor compilation failed:\n" + string.Join('\n',
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        return Assembly.Load(stream.ToArray());
    }

    private static RazorPanel CreateTemplate(Assembly assembly, string className)
    {
        var generatedType = assembly.GetTypes()
                                .FirstOrDefault(t => t.Name.Equals(className, StringComparison.OrdinalIgnoreCase))
                            ?? assembly.GetTypes().FirstOrDefault(t => typeof(RazorPanel).IsAssignableFrom(t));
        if (generatedType is null)
            throw new InvalidOperationException("Razor output did not contain a component type.");
        var templateInstance = (RazorPanel)Activator.CreateInstance(generatedType)!;
        if (string.IsNullOrEmpty(templateInstance.ScopeId))
            templateInstance.ScopeId = $"b-{className.ToLowerInvariant()}";
        return templateInstance;
    }

    private static string ReadStableFileText(string path)
    {
        string? previous = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                if (previous is not null && previous == text) return text;
                previous = text;
                Thread.Sleep(30);
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(25);
            }
        }

        return previous ?? File.ReadAllText(path);
    }

    private static readonly TemplateAssemblyCacheStore TemplateAssemblyCache = new();

    private sealed class TemplateAssemblyCacheStore
    {
        private const int MaxEntries = 32;

        private readonly Dictionary<string, (long WriteTime, Assembly Assembly, long LastUse)> _entries =
            new(StringComparer.Ordinal);

        private readonly object _lock = new();

        public Assembly? Get(string key, long writeTime)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out var entry) && entry.WriteTime == writeTime)
                {
                    _entries[key] = (entry.WriteTime, entry.Assembly, Environment.TickCount64);
                    return entry.Assembly;
                }
            }

            return null;
        }

        public void Set(string key, long writeTime, Assembly assembly)
        {
            lock (_lock)
            {
                _entries[key] = (writeTime, assembly, Environment.TickCount64);
                if (_entries.Count <= MaxEntries) return;
                foreach (var stale in _entries.OrderBy(kv => kv.Value.LastUse).Take(_entries.Count - MaxEntries))
                    _entries.Remove(stale.Key);
            }
        }
    }

    public PanelComponent BuildTree(RazorPanel template)
    {
        if (!template.NeedsBuild() && !template.NeedsContentRebuild()) return template;
        if (!template.CanRender())
        {
            template.MarkRenderSkipped();
            return template;
        }

        template.BeginRenderPass();
        var markup = template.RenderMarkupAsync().GetAwaiter().GetResult();
        var root = HtmlPanelParser.Parse(markup, template, _components);
        template.EndRenderPass();
        var firstRender = template.MarkBuilt(null);
        template.MarkChildContentBuilt();
        template.NotifyRendered(firstRender);
        return root;
    }

    private static string NormalizeRuntimeTypeNames(string source) =>
        Regex.Replace(source, @"(?<!global::)\bCrowbar\.Engine\.UI\.", "global::Crowbar.Engine.UI.");

    private static string RewriteUiAttributes(string source)
    {
        source = RewriteAttribute(source, "@onclick", "data-codex-onclick");
        source = RewriteAttribute(source, "@onchange", "data-codex-onchange");
        source = RewriteAttribute(source, "@bind-value", "data-codex-bind-value");
        source = RewriteAttribute(source, "@bind", "data-codex-bind-value");
        return source;
    }

    private static string RewriteAttribute(string source, string attributeName, string targetName)
    {
        var pattern = $@"{Regex.Escape(attributeName)}\s*=\s*(?:(['""])([^'""]+)\1|([^\s>]+))";
        return Regex.Replace(source, pattern, m =>
        {
            var val = (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim();
            val = CleanRazorExpression(val);
            return $"{targetName}=\"{val}\"";
        }, RegexOptions.IgnoreCase);
    }

    internal static string CleanRazorExpression(string val)
    {
        val = val.Trim();
        if (val.StartsWith("@", StringComparison.Ordinal))
        {
            val = val[1..].Trim();
            if (val.StartsWith("(", StringComparison.Ordinal) && val.EndsWith(")", StringComparison.Ordinal))
            {
                val = val[1..^1].Trim();
            }
        }

        return val;
    }

    private const string PageDirectivePattern = @"(?m)^\s*@page\s+""([^""]+)""\s*$";

    /// <summary>Returns the route templates declared by <c>@page</c> directives in a Razor source.</summary>
    public static string[] ExtractPages(string source) =>
        Regex.Matches(source, PageDirectivePattern).Select(match => match.Groups[1].Value.Trim()).ToArray();

    private static (string Source, string ClassMembers, string? BaseTypeName, IReadOnlyList<string> Interfaces,
        IReadOnlyList<string> Usings, string? NamespaceName, IReadOnlyList<string> Pages) ExtractDirectives(
            string source)
    {
        var pages = Regex.Matches(source, PageDirectivePattern).Select(match => match.Groups[1].Value.Trim()).ToArray();
        var baseMatch = Regex.Match(source, @"(?m)^\s*@inherits\s+([^\r\n]+)\s*$");
        var interfaces = Regex.Matches(source, @"(?m)^\s*@implements\s+([^\r\n]+)\s*$")
            .Select(match => match.Groups[1].Value.Trim()).ToArray();
        var usings = Regex.Matches(source, @"(?m)^\s*@using\s+([^\r\n;]+);?\s*$")
            .Select(match => match.Groups[1].Value.Trim()).ToArray();
        var namespaceMatch = Regex.Match(source, @"(?m)^\s*@namespace\s+([^\r\n]+)\s*$");
        var removable = new List<(int Index, int Length)>();
        if (baseMatch.Success) removable.Add((baseMatch.Index, baseMatch.Length));
        removable.AddRange(Regex.Matches(source, @"(?m)^\s*@implements\s+([^\r\n]+)\s*$")
            .Select(match => (match.Index, match.Length)));
        removable.AddRange(Regex.Matches(source, @"(?m)^\s*@using\s+([^\r\n;]+);?\s*$")
            .Select(match => (match.Index, match.Length)));
        removable.AddRange(Regex.Matches(source, PageDirectivePattern).Select(match => (match.Index, match.Length)));
        if (namespaceMatch.Success) removable.Add((namespaceMatch.Index, namespaceMatch.Length));
        foreach (var match in removable.OrderByDescending(match => match.Index))
            source = source.Remove(match.Index, match.Length);
        var first = Regex.Match(source, "@code\\s*{", RegexOptions.IgnoreCase);
        if (!first.Success)
            return (source, string.Empty, baseMatch.Success ? baseMatch.Groups[1].Value.Trim() : null, interfaces,
                usings, namespaceMatch.Success ? namespaceMatch.Groups[1].Value.Trim() : null, pages);
        var second = Regex.Match(source[(first.Index + first.Length)..], "@code\\s*{", RegexOptions.IgnoreCase);
        if (second.Success) throw new InvalidOperationException("A Razor component may contain only one @code block.");
        var open = source.IndexOf('{', first.Index);
        var close = FindClosingBrace(source, open);
        var withoutCode = source.Remove(first.Index, close - first.Index + 1);
        return (withoutCode, source.Substring(open + 1, close - open - 1),
            baseMatch.Success ? baseMatch.Groups[1].Value.Trim() : null, interfaces, usings,
            namespaceMatch.Success ? namespaceMatch.Groups[1].Value.Trim() : null, pages);
    }

    private static int FindClosingBrace(string source, int open)
    {
        var depth = 0;
        var state = 0;
        var escaped = false;
        for (var i = open; i < source.Length; i++)
        {
            var c = source[i];
            if (state == 1)
            {
                if (c == '\n') state = 0;
                continue;
            }

            if (state == 2)
            {
                if (c == '*' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    state = 0;
                    i++;
                }

                continue;
            }

            if (state is 3 or 4)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && state == 3)
                {
                    escaped = true;
                    continue;
                }

                if ((state == 3 && c == '"') || (state == 4 && c == '\'')) state = 0;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                state = 1;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                state = 2;
                i++;
                continue;
            }

            if (c == '"')
            {
                state = 3;
                continue;
            }

            if (c == '\'')
            {
                state = 4;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }

        throw new InvalidOperationException("Razor @code block is missing its closing brace.");
    }

    private static Type ResolveBaseType(string? name, Type fallback, Assembly[] references,
        IReadOnlyList<string> usings) =>
        name is null && typeof(RazorPanel).IsAssignableFrom(fallback) ? fallback :
        name is null ? typeof(RazorPanel) : ResolveType(name, references, "base type", usings);

    private static Type ResolveType(string name, Assembly[] references, string kind, IReadOnlyList<string> usings)
    {
        var candidates = references.Concat(AppDomain.CurrentDomain.GetAssemblies()).Distinct();
        var names = new[] { name }.Concat(usings.Select(usingName => usingName + "." + name));
        var type = names
            .SelectMany(candidate => candidates.Select(assembly => assembly.GetType(candidate, false, false)))
            .FirstOrDefault(found => found is not null) ?? names
            .Select(candidate => Type.GetType(candidate, false, false)).FirstOrDefault(found => found is not null);
        return type ?? throw new InvalidOperationException($"Razor {kind} '{name}' could not be resolved.");
    }
}

internal static class HtmlPanelParser
{
    public static PanelComponent Parse(string markup, RazorPanel root,
        IReadOnlyDictionary<string, Func<RazorPanel>>? components = null)
    {
        root.TagName = "root";
        if (!string.IsNullOrEmpty(root.ScopeId)) root.AddScope(root.ScopeId);
        var preservedInputs = FindInputs(root);
        root.ClearChildren();
        if (string.IsNullOrWhiteSpace(markup)) return root;
        try
        {
            var xml = XDocument.Parse("<root>" + markup + "</root>", LoadOptions.PreserveWhitespace);
            var index = 0;
            foreach (var node in xml.Root!.Nodes())
            {
                // Keys mirror panel positions so that preserved inputs and child
                // components line up across renders. Whitespace-only text nodes
                // produce no panel, so they must not consume an index.
                if (node is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value)) continue;
                AddNode(root, node, root, components, $"root/{index}", preservedInputs);
                index++;
            }

            return root;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Razor rendered invalid UI markup: " + ex.Message, ex);
        }
    }

    private static Dictionary<string, TextInput> FindInputs(Panel root)
    {
        var result = new Dictionary<string, TextInput>(StringComparer.Ordinal);
        Visit(root, "root", result);
        return result;

        static void Visit(Panel panel, string key, Dictionary<string, TextInput> result)
        {
            if (panel is TextInput input) result[key] = input;
            for (var i = 0; i < panel.Children.Count; i++) Visit(panel.Children[i], $"{key}/{i}", result);
        }
    }

    private static void AddNode(Panel parent, XNode node, RazorPanel runtime,
        IReadOnlyDictionary<string, Func<RazorPanel>>? components, string key,
        IReadOnlyDictionary<string, TextInput> preservedInputs)
    {
        if (node is XText text)
        {
            if (FragmentMarkerRegex.IsMatch(text.Value))
            {
                SpliceChildContent(parent, text.Value, runtime, key, preservedInputs);
                return;
            }

            if (!string.IsNullOrWhiteSpace(text.Value))
            {
                var textPanel = new Panel { TagName = "text", Text = text.Value };
                if (!string.IsNullOrEmpty(runtime.ScopeId)) textPanel.AddScope(runtime.ScopeId);
                parent.AddChild(textPanel);
            }

            return;
        }

        if (node is not XElement element) return;
        if (components is not null && components.TryGetValue(element.Name.LocalName, out var componentFactory))
        {
            var child = runtime.GetOrCreateChild(key, componentFactory);
            child.StateChanged = runtime.StateHasChanged;
            child.NavigationRequested = runtime.NavigationRequested;
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.LocalName.Equals("class", StringComparison.OrdinalIgnoreCase))
                    foreach (var value in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        child.AddClass(value);
                else if (attribute.Name.LocalName.StartsWith("data-codex-", StringComparison.OrdinalIgnoreCase))
                    continue; // Skip synthetic event attributes – they are handled only on HTML elements
                else child.SetParameter(attribute.Name.LocalName, attribute.Value);
            }

            // Capture the markup between the component's tags. It is parsed with
            // the parent as runtime: expressions were already evaluated by the
            // parent's ExecuteAsync and event/binding attributes refer to parent
            // members. Named region elements (<Header>, <Body>, ...) matching a
            // RenderFragment parameter of the component feed that fragment;
            // everything else feeds the default ChildContent fragment. The
            // panels are handed to the child so its @Fragment placeholder (a
            // marker text node) can be replaced by them.
            var providedFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regionNodes = new Dictionary<string, List<XNode>>(StringComparer.OrdinalIgnoreCase);
            var childContentNodes = new List<XNode>();
            foreach (var childNode in element.Nodes())
            {
                if (childNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value)) continue;
                if (childNode is XElement regionElement &&
                    child.HasRenderFragmentParameter(regionElement.Name.LocalName))
                {
                    var regionName = regionElement.Name.LocalName;
                    if (!regionNodes.TryGetValue(regionName, out var region))
                        regionNodes[regionName] = region = [];
                    foreach (var inner in regionElement.Nodes())
                        if (inner is not XText whitespaceOnly || !string.IsNullOrWhiteSpace(whitespaceOnly.Value))
                            region.Add(inner);
                    continue;
                }

                childContentNodes.Add(childNode);
            }

            foreach (var (regionName, nodes) in regionNodes)
            {
                providedFragments.Add(regionName);
                var signature = string.Concat(nodes.Select(node => node.ToString()));
                if (signature != child.GetFragmentSignature(regionName))
                    child.SetFragment(regionName, BuildFragmentPanels(nodes, key, regionName, runtime, components),
                        signature);
            }

            providedFragments.Add("ChildContent");
            var contentSignature = string.Concat(childContentNodes.Select(node => node.ToString()));
            if (contentSignature != child.GetFragmentSignature("ChildContent"))
                child.SetFragment("ChildContent", BuildFragmentPanels(childContentNodes, key, "ChildContent", runtime,
                    components), contentSignature);

            // Fragments the parent no longer provides (e.g. a region removed by
            // an @if) must be cleared so the child re-renders without them.
            foreach (var staleName in child.ProvidedFragmentNames.Where(name => !providedFragments.Contains(name)))
                child.SetFragment(staleName, null, string.Empty);

            var childTree = new RazorComponentFactory(components).BuildTree(child);
            // The child tree keeps only its own scope. Applying the parent's scope
            // to the child's root would leak parent scoped CSS (e.g. the page's
            // `root { height: ... }` rule) into every nested component root.
            parent.AddChild(childTree);
            return;
        }

        var panel = element.Name.LocalName.ToLowerInvariant() switch
        {
            "button" => new Button(),
            "input" => new TextInput(),
            "img" or "image" => new Image(),
            "label" or "span" => new Label(),
            _ => new Panel()
        };
        panel.TagName = element.Name.LocalName;
        if (!string.IsNullOrEmpty(runtime.ScopeId)) panel.AddScope(runtime.ScopeId);
        string? click = null, change = null, bind = null;
        string? declaredValue = null;
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name == "class")
                foreach (var c in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    panel.AddClass(c);
            else if (attribute.Name == "id") panel.Id = attribute.Value;
            else if (attribute.Name == "style")
                foreach (var declaration in attribute.Value.Split(';'))
                {
                    var p = declaration.Split(':', 2);
                    if (p.Length == 2) panel.SetInlineStyle(p[0].Trim(), p[1].Trim());
                }
            else if (attribute.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase) && panel is TextInput)
                declaredValue = attribute.Value;
            else if (attribute.Name.LocalName.Equals("data-codex-onclick", StringComparison.OrdinalIgnoreCase))
                click = attribute.Value;
            else if (attribute.Name.LocalName.Equals("data-codex-onchange", StringComparison.OrdinalIgnoreCase))
                change = attribute.Value;
            else if (attribute.Name.LocalName.Equals("data-codex-bind-value", StringComparison.OrdinalIgnoreCase))
                bind = attribute.Value;
            else panel.Attributes[attribute.Name.LocalName] = attribute.Value;
        }

        var childIndex = 0;
        foreach (var child in element.Nodes())
        {
            if (child is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value)) continue;
            AddNode(panel, child, runtime, components, $"{key}/{childIndex}", preservedInputs);
            childIndex++;
        }

        if (panel is TextInput inputValue)
        {
            if (preservedInputs.TryGetValue(key, out var previous))
            {
                inputValue.SetValue(previous.Value, previous.CaretIndex);
                inputValue.CopyInteractionStateFrom(previous);
            }
            else inputValue.SetValue(declaredValue ?? string.Empty);
        }

        if (panel is Button button && click is not null)
            button.Clicked += e => RazorEventInvoker.Invoke(runtime, click, e);
        if (panel is TextInput textInput)
        {
            if (change is not null) textInput.ValueChanged += value => RazorEventInvoker.Invoke(runtime, change, value);
            if (bind is not null) textInput.ValueChanged += value => RazorEventInvoker.SetValue(runtime, bind, value);
        }

        parent.AddChild(panel);
    }

    /// <summary>
    /// Matches any fragment marker in a rendered text node (default ChildContent
    /// or a named region). The name is captured up to the closing brackets so
    /// non-ASCII identifiers (e.g. <c>Tête</c>) parse correctly; the marker is
    /// self-delimiting so there is no ambiguity.
    /// </summary>
    private static readonly Regex FragmentMarkerRegex = new(
        @"\[\[__CROWBAR_(?:CHILDCONTENT__|FRAGMENT__:([^\]\[]+))\]\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Replaces fragment marker text nodes with the captured fragment panels,
    /// preserving any surrounding text and restoring input state with keys
    /// relative to the current tree (fragment inputs were built fresh by the
    /// parent's capture pass, so they are restored here instead).
    /// </summary>
    private static void SpliceChildContent(Panel parent, string content, RazorPanel runtime, string key,
        IReadOnlyDictionary<string, TextInput> preservedInputs)
    {
        var lastSlash = key.LastIndexOf('/');
        var parentKey = lastSlash > 0 ? key[..lastSlash] : key;
        var baseIndex = lastSlash > 0 && int.TryParse(key[(lastSlash + 1)..], out var parsed) ? parsed : 0;
        var insertIndex = baseIndex;
        var position = 0;
        foreach (Match match in FragmentMarkerRegex.Matches(content))
        {
            if (match.Index > position)
                AddSpliceText(parent, content[position..match.Index], runtime, ref insertIndex);
            var fragmentName = match.Groups[1].Success ? match.Groups[1].Value : "ChildContent";
            var panels = runtime.GetFragmentPanels(fragmentName);
            if (panels is not null)
            {
                foreach (var panel in panels)
                {
                    RestorePreservedInputs(panel, $"{parentKey}/{insertIndex}", preservedInputs);
                    parent.AddChild(panel);
                    insertIndex++;
                }
            }

            position = match.Index + match.Length;
        }

        if (position < content.Length)
            AddSpliceText(parent, content[position..], runtime, ref insertIndex);
    }

    private static void AddSpliceText(Panel parent, string text, RazorPanel runtime, ref int insertIndex)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var textPanel = new Panel { TagName = "text", Text = text };
        if (!string.IsNullOrEmpty(runtime.ScopeId)) textPanel.AddScope(runtime.ScopeId);
        parent.AddChild(textPanel);
        insertIndex++;
    }

    private static List<Panel>? BuildFragmentPanels(List<XNode> nodes, string key, string name, RazorPanel runtime,
        IReadOnlyDictionary<string, Func<RazorPanel>>? components)
    {
        if (nodes.Count == 0) return null;
        var container = new Panel();
        var emptyPreserved = new Dictionary<string, TextInput>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
            AddNode(container, nodes[i], runtime, components, $"{key}/fragment/{name}/{i}", emptyPreserved);
        return [.. container.Children];
    }

    private static void RestorePreservedInputs(Panel panel, string key,
        IReadOnlyDictionary<string, TextInput> preservedInputs)
    {
        if (panel is TextInput input && preservedInputs.TryGetValue(key, out var previous))
        {
            input.SetValue(previous.Value, previous.CaretIndex);
            input.CopyInteractionStateFrom(previous);
        }

        for (var i = 0; i < panel.Children.Count; i++)
            RestorePreservedInputs(panel.Children[i], $"{key}/{i}", preservedInputs);
    }
}

internal static class RazorEventInvoker
{
    public static void Invoke(object target, string expression, object argument)
    {
        var invocation = RazorComponentFactory.CleanRazorExpression(expression);
        if (invocation.Contains("=>", StringComparison.Ordinal))
            invocation = invocation[(invocation.IndexOf("=>", StringComparison.Ordinal) + 2)..].Trim();
        if (invocation.StartsWith("this.", StringComparison.Ordinal)) invocation = invocation[5..].Trim();
        var methodName = Regex.Match(invocation, @"^[A-Za-z_][A-Za-z0-9_]*").Value;
        if (string.IsNullOrEmpty(methodName))
            throw new InvalidOperationException(
                $"Unsupported Razor event expression '{expression}'. Use a method or a method-call lambda.");
        var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name.Equals(methodName, StringComparison.Ordinal)).ToList();
        if (methods.Count == 0)
            throw new InvalidOperationException($"Razor event handler '{methodName}' was not found.");
        var method = methods.FirstOrDefault(m => m.GetParameters().Length == (argument is null ? 0 : 1)) ?? methods[0];
        var parameters = method.GetParameters();
        object?[] args = parameters.Length == 0 ? [] : [ConvertArgument(argument, parameters[0].ParameterType)];
        var result = method.Invoke(target, args);
        if (result is Task task) task.GetAwaiter().GetResult();
        if (target is PanelComponent component) component.StateHasChanged();
    }

    public static void SetValue(object target, string memberName, string value)
    {
        memberName = RazorComponentFactory.CleanRazorExpression(memberName);
        var type = target.GetType();
        var property =
            type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true)
        {
            property.SetValue(target, value);
            if (target is PanelComponent c) c.StateHasChanged();
            return;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is not null)
        {
            field.SetValue(target, value);
            if (target is PanelComponent c) c.StateHasChanged();
            return;
        }

        throw new InvalidOperationException($"Razor binding target '{memberName}' was not found or is read-only.");
    }

    private static object? ConvertArgument(object argument, Type type)
    {
        if (type.IsInstanceOfType(argument)) return argument;
        if (type == typeof(string)) return argument.ToString();
        return Convert.ChangeType(argument, type);
    }
}