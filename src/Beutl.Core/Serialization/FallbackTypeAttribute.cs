using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
public sealed class FallbackTypeAttribute(Type fallbackType) : Attribute
{
    public Type FallbackType { get; } = fallbackType;
}

public enum FallbackReason
{
    TypeNotFound,
    DeserializationFailed,
}

public interface IFallback : ICoreSerializable
{
    /// <summary>Whether this fallback can persist edits while retaining all unavailable content.
    /// The default keeps recovered files protected from lossy saves.</summary>
    bool CanSerializeWithoutDataLoss => false;

    JsonObject? Json { get; set; }

    FallbackReason Reason { get; set; }

    string? ErrorMessage { get; set; }

    bool TryGetTypeName([NotNullWhen(true)] out string? result);
}
