using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crowbar.Engine.UI;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ParameterAttribute : Attribute { }

public abstract class RazorTemplateBase : PanelComponent
{
    private readonly StringBuilder _output = new();
    private readonly Dictionary<string, RazorTemplateBase> _childComponents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeChildren = new(StringComparer.Ordinal);
    private bool _initialized;

    protected void WriteLiteral(string value) => _output.Append(value);
    protected void Write(object? value) => _output.Append(System.Net.WebUtility.HtmlEncode(value?.ToString() ?? string.Empty));

    protected virtual void OnInitialized() { }
    protected virtual void OnParametersSet() { }
    protected virtual void OnAfterRender(bool firstRender) { }

    internal async Task<string> RenderMarkupAsync()
    {
        if (!_initialized) { _initialized = true; OnInitialized(); }
        OnParametersSet();
        _output.Clear();
        await ExecuteAsync();
        return _output.ToString();
    }

    internal void NotifyRendered(bool firstRender) => OnAfterRender(firstRender);
    internal void BeginRenderPass() => _activeChildren.Clear();
    internal RazorTemplateBase GetOrCreateChild(string key, Func<RazorTemplateBase> factory)
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
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = GetType().GetProperty(name, flags);
        if (property is not null)
        {
            if (!property.IsDefined(typeof(ParameterAttribute), true))
                throw new InvalidOperationException($"Razor property '{name}' on {GetType().Name} is not marked with [Parameter].");
            if (!property.CanWrite)
                throw new InvalidOperationException($"Razor parameter '{name}' on {GetType().Name} is read-only.");
            try { property.SetValue(this, ConvertParameter(value, property.PropertyType)); }
            catch (Exception ex) { throw new InvalidOperationException($"Razor parameter '{name}' on {GetType().Name} could not convert value '{value}'.", ex); }
            return;
        }
        throw new InvalidOperationException($"Razor parameter '{name}' was not found on {GetType().Name}.");
    }

    private static object? ConvertParameter(string value, Type type) =>
        type == typeof(string) ? value : Convert.ChangeType(value, Nullable.GetUnderlyingType(type) ?? type);
    public abstract Task ExecuteAsync();
}

public interface IRazorComponentCompiler
{
    PanelComponent Compile(string razorSource, string className, Type baseType, params Assembly[] references);
}

public sealed class RazorComponentFactory : IRazorComponentCompiler
{
    private readonly IReadOnlyDictionary<string, Func<RazorTemplateBase>> _components;

    public RazorComponentFactory(IReadOnlyDictionary<string, Func<RazorTemplateBase>>? components = null)
    {
        _components = components ?? new Dictionary<string, Func<RazorTemplateBase>>(StringComparer.OrdinalIgnoreCase);
    }

    public PanelComponent Compile(string razorSource, string className, Type baseType, params Assembly[] references)
    {
        var template = CompileTemplate(razorSource, className, baseType, references);
        return BuildTree(template);
    }

    public RazorTemplateBase CompileTemplate(string razorSource, string className, Type baseType, params Assembly[] references)
    {
        // Event and binding expressions are intentionally converted to stable
        // markers before Razor parses the document. This keeps the generated
        // class strongly typed for @code, @if, @foreach and expressions while
        // allowing the native Panel tree to attach delegates after rendering.
        var directives = ExtractDirectives(razorSource);
        var source = directives.Source;
        var classMembers = directives.ClassMembers;
        baseType = ResolveBaseType(directives.BaseTypeName, baseType, references);
        if (!typeof(RazorTemplateBase).IsAssignableFrom(baseType))
            throw new InvalidOperationException($"Razor base type '{baseType.FullName}' must derive from RazorTemplateBase.");
        if (baseType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) is null)
            throw new InvalidOperationException($"Razor base type '{baseType.FullName}' must have a parameterless constructor.");
        source = RewriteUiAttributes(source);
        var document = RazorSourceDocument.Create(source, className + ".razor");
        var project = RazorProjectEngine.Create(RazorConfiguration.Default, RazorProjectFileSystem.Create(AppContext.BaseDirectory), b =>
        {
            b.SetNamespace("Crowbar.Engine.UI.Generated");
            b.SetBaseType(typeof(RazorTemplateBase).FullName!);
        });
        var codeDocument = project.Process(document, className + ".razor", [], []);
        var generatedCode = codeDocument.GetCSharpDocument().GeneratedCode;
        if (!string.IsNullOrWhiteSpace(classMembers))
        {
            var generatedTree = CSharpSyntaxTree.ParseText(generatedCode);
            var generatedClass = generatedTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (generatedClass is null) throw new InvalidOperationException("Razor output did not contain a generated class.");
            generatedCode = generatedCode.Insert(generatedClass.CloseBraceToken.SpanStart, "\n" + classMembers + "\n");
        }
        var interfaceTypes = directives.Interfaces.Select(name => ResolveType(name, references, "interface")).ToArray();
        var generatedTreeWithContracts = CSharpSyntaxTree.ParseText(generatedCode);
        var generatedClassWithContracts = generatedTreeWithContracts.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("Razor output did not contain a generated class.");
        var generatedBaseTypes = new List<BaseTypeSyntax>
        {
            SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseType.FullName!))
        };
        generatedBaseTypes.AddRange(interfaceTypes.Select(type => SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(type.FullName!))));
        var classWithContracts = generatedClassWithContracts.WithBaseList(
            SyntaxFactory.BaseList(SyntaxFactory.SeparatedList(generatedBaseTypes)));
        generatedCode = generatedTreeWithContracts.GetRoot().ReplaceNode(generatedClassWithContracts, classWithContracts).ToFullString();
        var tree = CSharpSyntaxTree.ParseText(generatedCode);
        var platformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
        var assemblyReferences = references.Concat([
                typeof(object).Assembly, typeof(Enumerable).Assembly,
                typeof(RazorTemplateBase).Assembly, typeof(RazorProjectEngine).Assembly])
            .Distinct().Select(a => MetadataReference.CreateFromFile(a.Location)).Concat(platformReferences);
        var compilation = CSharpCompilation.Create(
            "Crowbar.Razor." + Guid.NewGuid().ToString("N"), [tree], assemblyReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException("Razor compilation failed:\n" + string.Join('\n', result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var assembly = Assembly.Load(stream.ToArray());
        var generatedType = assembly.GetTypes().FirstOrDefault(t => t.Name.Equals(className, StringComparison.OrdinalIgnoreCase))
            ?? assembly.GetTypes().FirstOrDefault(t => typeof(RazorTemplateBase).IsAssignableFrom(t));
        if (generatedType is null) throw new InvalidOperationException("Razor output did not contain a component type.");
        return (RazorTemplateBase)Activator.CreateInstance(generatedType)!;
    }

    public PanelComponent BuildTree(RazorTemplateBase template)
    {
        if (!template.NeedsBuild()) return template;
        if (template.NeedsBuild() && !template.CanRender())
        {
            template.MarkRenderSkipped();
            return template;
        }
        template.BeginRenderPass();
        var markup = template.RenderMarkupAsync().GetAwaiter().GetResult();
        var root = HtmlPanelParser.Parse(markup, template, _components);
        template.EndRenderPass();
        var firstRender = template.MarkBuilt(null);
        template.NotifyRendered(firstRender);
        return root;
    }

    private static string RewriteUiAttributes(string source)
    {
        source = Regex.Replace(source, "@onclick\\s*=\\s*(['\"])([^'\"]+)\\1", "data-codex-onclick=\"$2\"", RegexOptions.IgnoreCase);
        source = Regex.Replace(source, "@onchange\\s*=\\s*(['\"])([^'\"]+)\\1", "data-codex-onchange=\"$2\"", RegexOptions.IgnoreCase);
        source = Regex.Replace(source, "@bind-value\\s*=\\s*(['\"])([^'\"]+)\\1", "data-codex-bind-value=\"$2\"", RegexOptions.IgnoreCase);
        return source;
    }

    private static (string Source, string ClassMembers, string? BaseTypeName, IReadOnlyList<string> Interfaces) ExtractDirectives(string source)
    {
        var baseMatch = Regex.Match(source, @"(?m)^\s*@inherits\s+([^\r\n]+)\s*$");
        var interfaces = Regex.Matches(source, @"(?m)^\s*@implements\s+([^\r\n]+)\s*$").Select(match => match.Groups[1].Value.Trim()).ToArray();
        source = baseMatch.Success ? source.Remove(baseMatch.Index, baseMatch.Length) : source;
        foreach (Match match in Regex.Matches(source, @"(?m)^\s*@implements\s+([^\r\n]+)\s*$").ToArray().Reverse()) source = source.Remove(match.Index, match.Length);
        var first = Regex.Match(source, "@code\\s*{", RegexOptions.IgnoreCase);
        if (!first.Success) return (source, string.Empty, baseMatch.Success ? baseMatch.Groups[1].Value.Trim() : null, interfaces);
        var second = Regex.Match(source[(first.Index + first.Length)..], "@code\\s*{", RegexOptions.IgnoreCase);
        if (second.Success) throw new InvalidOperationException("A Razor component may contain only one @code block.");
        var open = source.IndexOf('{', first.Index);
        var close = FindClosingBrace(source, open);
        var withoutCode = source.Remove(first.Index, close - first.Index + 1);
        return (withoutCode, source.Substring(open + 1, close - open - 1), baseMatch.Success ? baseMatch.Groups[1].Value.Trim() : null, interfaces);
    }

    private static int FindClosingBrace(string source, int open)
    {
        var depth = 0; var state = 0; var escaped = false;
        for (var i = open; i < source.Length; i++)
        {
            var c = source[i];
            if (state == 1) { if (c == '\n') state = 0; continue; }
            if (state == 2) { if (c == '*' && i + 1 < source.Length && source[i + 1] == '/') { state = 0; i++; } continue; }
            if (state is 3 or 4)
            {
                if (escaped) { escaped = false; continue; }
                if (c == '\\' && state == 3) { escaped = true; continue; }
                if ((state == 3 && c == '"') || (state == 4 && c == '\'')) state = 0;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/') { state = 1; i++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*') { state = 2; i++; continue; }
            if (c == '"') { state = 3; continue; }
            if (c == '\'') { state = 4; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return i;
        }
        throw new InvalidOperationException("Razor @code block is missing its closing brace.");
    }

    private static Type ResolveBaseType(string? name, Type fallback, Assembly[] references) =>
        name is null && typeof(RazorTemplateBase).IsAssignableFrom(fallback) ? fallback :
        name is null ? typeof(RazorTemplateBase) : ResolveType(name, references, "base type");
    private static Type ResolveType(string name, Assembly[] references, string kind)
    {
        var candidates = references.Concat(AppDomain.CurrentDomain.GetAssemblies()).Distinct();
        var type = candidates.Select(assembly => assembly.GetType(name, false, false)).FirstOrDefault(found => found is not null)
            ?? Type.GetType(name, false, false);
        return type ?? throw new InvalidOperationException($"Razor {kind} '{name}' could not be resolved.");
    }
}

internal static class HtmlPanelParser
{
    public static PanelComponent Parse(string markup, RazorTemplateBase root, IReadOnlyDictionary<string, Func<RazorTemplateBase>>? components = null)
    {
        root.TagName = "root";
        root.ClearChildren();
        if (string.IsNullOrWhiteSpace(markup)) return root;
        try
        {
            var xml = XDocument.Parse("<root>" + markup + "</root>", LoadOptions.PreserveWhitespace);
            var index = 0;
            foreach (var node in xml.Root!.Nodes()) AddNode(root, node, root, components, $"root/{index++}");
            return root;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Razor rendered invalid UI markup: " + ex.Message, ex);
        }
    }

    private static void AddNode(Panel parent, XNode node, RazorTemplateBase runtime, IReadOnlyDictionary<string, Func<RazorTemplateBase>>? components, string key)
    {
        if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
        {
            parent.AddChild(new Panel { TagName = "text", Text = text.Value });
            return;
        }
        if (node is not XElement element) return;
        if (components is not null && components.TryGetValue(element.Name.LocalName, out var componentFactory))
        {
            var child = runtime.GetOrCreateChild(key, componentFactory);
            child.StateChanged = runtime.StateHasChanged;
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.LocalName.Equals("class", StringComparison.OrdinalIgnoreCase))
                    foreach (var value in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) child.AddClass(value);
                else child.SetParameter(attribute.Name.LocalName, attribute.Value);
            }
            var childTree = new RazorComponentFactory(components).BuildTree(child);
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
        string? click = null, change = null, bind = null;
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name == "class") foreach (var c in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) panel.AddClass(c);
            else if (attribute.Name == "id") panel.Id = attribute.Value;
            else if (attribute.Name == "style") foreach (var declaration in attribute.Value.Split(';')) { var p = declaration.Split(':', 2); if (p.Length == 2) panel.SetInlineStyle(p[0].Trim(), p[1].Trim()); }
            else if (attribute.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase) && panel is TextInput input) input.SetValue(attribute.Value);
            else if (attribute.Name.LocalName.Equals("data-codex-onclick", StringComparison.OrdinalIgnoreCase)) click = attribute.Value;
            else if (attribute.Name.LocalName.Equals("data-codex-onchange", StringComparison.OrdinalIgnoreCase)) change = attribute.Value;
            else if (attribute.Name.LocalName.Equals("data-codex-bind-value", StringComparison.OrdinalIgnoreCase)) bind = attribute.Value;
            else panel.Attributes[attribute.Name.LocalName] = attribute.Value;
        }
        var childIndex = 0;
        foreach (var child in element.Nodes()) AddNode(panel, child, runtime, components, $"{key}/{childIndex++}");
        if (panel is Button button && click is not null) button.Clicked += e => RazorEventInvoker.Invoke(runtime, click, e);
        if (panel is TextInput textInput)
        {
            if (change is not null) textInput.ValueChanged += value => RazorEventInvoker.Invoke(runtime, change, value);
            if (bind is not null) textInput.ValueChanged += value => RazorEventInvoker.SetValue(runtime, bind, value);
        }
        parent.AddChild(panel);
    }
}

internal static class RazorEventInvoker
{
    public static void Invoke(object target, string expression, object argument)
    {
        var invocation = expression.Trim();
        if (invocation.Contains("=>", StringComparison.Ordinal)) invocation = invocation[(invocation.IndexOf("=>", StringComparison.Ordinal) + 2)..].Trim();
        var methodName = Regex.Match(invocation, "^[A-Za-z_][A-Za-z0-9_]*").Value;
        if (string.IsNullOrEmpty(methodName)) throw new InvalidOperationException($"Unsupported Razor event expression '{expression}'. Use a method or a method-call lambda.");
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name.Equals(methodName, StringComparison.Ordinal));
        if (method is null) throw new InvalidOperationException($"Razor event handler '{methodName}' was not found.");
        var parameters = method.GetParameters();
        object?[] args = parameters.Length == 0 ? [] : [ConvertArgument(argument, parameters[0].ParameterType)];
        var result = method.Invoke(target, args);
        if (result is Task task) task.GetAwaiter().GetResult();
        if (target is PanelComponent component) component.StateHasChanged();
    }

    public static void SetValue(object target, string memberName, string value)
    {
        var type = target.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true) { property.SetValue(target, value); if (target is PanelComponent c) c.StateHasChanged(); return; }
        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field is not null) { field.SetValue(target, value); if (target is PanelComponent c) c.StateHasChanged(); return; }
        throw new InvalidOperationException($"Razor binding target '{memberName}' was not found or is read-only.");
    }

    private static object? ConvertArgument(object argument, Type type)
    {
        if (type.IsInstanceOfType(argument)) return argument;
        if (type == typeof(string)) return argument.ToString();
        return Convert.ChangeType(argument, type);
    }
}
