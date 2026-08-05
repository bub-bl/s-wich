using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Crowbar.Engine.UI;

public abstract class RazorTemplateBase : PanelComponent
{
    private readonly StringBuilder _output = new();
    protected void WriteLiteral(string value) => _output.Append(value);
    protected void Write(object? value) => _output.Append(System.Net.WebUtility.HtmlEncode(value?.ToString() ?? string.Empty));
    internal async Task<string> RenderMarkupAsync() { _output.Clear(); await ExecuteAsync(); return _output.ToString(); }
    public abstract Task ExecuteAsync();
}

public interface IRazorComponentCompiler
{
    PanelComponent Compile(string razorSource, string className, Type baseType, params Assembly[] references);
}

public sealed class RazorComponentFactory : IRazorComponentCompiler
{
    public PanelComponent Compile(string razorSource, string className, Type baseType, params Assembly[] references)
    {
        var source = RazorSourceDocument.Create(razorSource, className + ".razor");
        var project = RazorProjectEngine.Create(RazorConfiguration.Default, RazorProjectFileSystem.Create(AppContext.BaseDirectory), b =>
        {
            b.SetNamespace("Crowbar.Engine.UI.Generated");
            b.SetBaseType(typeof(RazorTemplateBase).FullName!);
        });
        var codeDocument = project.Process(source, className + ".razor", [], []);
        var generated = codeDocument.GetCSharpDocument().GeneratedCode;
        var tree = CSharpSyntaxTree.ParseText(generated);
        var platformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Crowbar.Razor." + Guid.NewGuid().ToString("N"),
            [tree],
            references.Concat([typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(RazorTemplateBase).Assembly, typeof(RazorProjectEngine).Assembly])
                .Distinct().Select(a => MetadataReference.CreateFromFile(a.Location)).Concat(platformReferences),
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
        var template = (RazorTemplateBase)Activator.CreateInstance(generatedType)!;
        var markup = template.RenderMarkupAsync().GetAwaiter().GetResult();
        var root = HtmlPanelParser.Parse(markup, template);
        template.MarkBuilt(null);
        return root;
    }
}

internal static class HtmlPanelParser
{
    public static PanelComponent Parse(string markup, RazorTemplateBase root)
    {
        root.TagName = "root";
        root.ClearChildren();
        if (string.IsNullOrWhiteSpace(markup)) return root;
        var xml = XDocument.Parse("<root>" + markup + "</root>", LoadOptions.PreserveWhitespace);
        foreach (var node in xml.Root!.Nodes()) AddNode(root, node);
        return root;
    }

    private static void AddNode(Panel parent, XNode node)
    {
        if (node is XText text && !string.IsNullOrWhiteSpace(text.Value)) { parent.AddChild(new Panel { TagName = "text", Text = text.Value }); return; }
        if (node is not XElement element) return;
        var panel = element.Name.LocalName.ToLowerInvariant() switch
        {
            "button" => new Button(),
            "input" => new TextInput(),
            "img" or "image" => new Image(),
            "label" or "span" => new Label(),
            _ => new Panel()
        };
        panel.TagName = element.Name.LocalName;
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name == "class") foreach (var c in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) panel.AddClass(c);
            else if (attribute.Name == "id") panel.Id = attribute.Value;
            else if (attribute.Name == "style") foreach (var declaration in attribute.Value.Split(';')) { var p = declaration.Split(':', 2); if (p.Length == 2) panel.SetInlineStyle(p[0].Trim(), p[1].Trim()); }
            else if (attribute.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase) && panel is TextInput input) input.SetValue(attribute.Value);
            else panel.Attributes[attribute.Name.LocalName] = attribute.Value;
        }
        foreach (var child in element.Nodes()) AddNode(panel, child);
        parent.AddChild(panel);
    }

}
