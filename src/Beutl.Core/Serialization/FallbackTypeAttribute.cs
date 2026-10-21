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
    JsonObject? Json { get; set; }

    FallbackReason Reason { get; set; }

    string? ErrorMessage { get; set; }

    bool TryGetTypeName([NotNullWhen(true)] out string? result);
}

internal static class FallbackExtensions
{
    // A fallback read from JSON writes its known members from the object and keeps the rest. One given JSON it
    // never read, such as one for an object whose data failed to load, writes that JSON as it is.
    internal static bool SavesItsMembers(this IFallback fallback) => fallback is CoreObject { UnknownMembers: not null };
}
