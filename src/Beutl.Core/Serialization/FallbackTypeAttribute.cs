using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
public sealed class FallbackTypeAttribute(Type fallbackType) : Attribute
{
    public Type FallbackType { get; } = fallbackType;
}

public interface IFallback : ICoreSerializable
{
    JsonObject? Json { get; set; }

    bool TryGetTypeName([NotNullWhen(true)] out string? result);
}
