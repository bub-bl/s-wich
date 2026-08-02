using System.Diagnostics.CodeAnalysis;

namespace Crowbar.Engine;

public interface IValid
{
    bool IsValid { get; }
}

public static class ValidExtensions
{
    extension<T>([NotNullWhen(true)] T? value) where T : class, IValid
    {
        public bool IsValid()
        {
            return value is not null && value.IsValid;
        }
    }
}