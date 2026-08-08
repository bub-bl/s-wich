using System.Reflection;
using System.Text.RegularExpressions;

namespace Crowbar.UI;

/// <summary>Bridges synthetic <c>data-codex-*</c> attributes to component members at runtime.</summary>
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
