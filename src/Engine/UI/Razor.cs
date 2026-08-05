using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Crowbar.Engine.UI;

public abstract class RazorTemplateBase : PanelComponent
{
    private readonly StringBuilder _output = new();
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
    internal void SetParameter(string name, string value)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = GetType().GetProperty(name, flags);
        if (property?.CanWrite == true) { property.SetValue(this, ConvertParameter(value, property.PropertyType)); return; }
        var field = GetType().GetField(name, flags);
        if (field is not null) { field.SetValue(this, ConvertParameter(value, field.FieldType)); return; }
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
        var (source, classMembers) = ExtractCodeBlock(razorSource);
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
        var markup = template.RenderMarkupAsync().GetAwaiter().GetResult();
        var root = HtmlPanelParser.Parse(markup, template, _components);
        template.MarkBuilt(null);
        template.NotifyRendered(false);
        return root;
    }

    private static string RewriteUiAttributes(string source)
    {
        source = Regex.Replace(source, "@onclick\\s*=\\s*(['\"])([^'\"]+)\\1", "data-codex-onclick=\"$2\"", RegexOptions.IgnoreCase);
        source = Regex.Replace(source, "@onchange\\s*=\\s*(['\"])([^'\"]+)\\1", "data-codex-onchange=\"$2\"", RegexOptions.IgnoreCase);
        source = Regex.Replace(source, "@bind-value\\s*=\\s*(['\"])([^'\"]+)\\1", "data-codex-bind-value=\"$2\"", RegexOptions.IgnoreCase);
        return source;
    }

    private static (string Source, string Members) ExtractCodeBlock(string source)
    {
        var marker = Regex.Match(source, "@code\\s*{", RegexOptions.IgnoreCase);
        if (!marker.Success) return (source, string.Empty);
        var open = source.IndexOf('{', marker.Index);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                var withoutCode = source.Remove(marker.Index, i - marker.Index + 1);
                return (withoutCode, source.Substring(open + 1, i - open - 1));
            }
        }
        throw new InvalidOperationException("Razor @code block is missing its closing brace.");
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
            foreach (var node in xml.Root!.Nodes()) AddNode(root, node, root, components);
            return root;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Razor rendered invalid UI markup: " + ex.Message, ex);
        }
    }

    private static void AddNode(Panel parent, XNode node, RazorTemplateBase runtime, IReadOnlyDictionary<string, Func<RazorTemplateBase>>? components)
    {
        if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
        {
            parent.AddChild(new Panel { TagName = "text", Text = text.Value });
            return;
        }
        if (node is not XElement element) return;
        if (components is not null && components.TryGetValue(element.Name.LocalName, out var componentFactory))
        {
            var child = componentFactory();
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
        foreach (var child in element.Nodes()) AddNode(panel, child, runtime, components);
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
