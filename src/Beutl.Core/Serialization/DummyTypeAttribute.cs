using System.Diagnostics.CodeAnalysis;

namespace Beutl.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
public sealed class DummyTypeAttribute(Type dummyType) : Attribute
{
    public Type DummyType { get; } = dummyType;
}

public interface IDummy : ICoreSerializable
{
    bool TryGetTypeName([NotNullWhen(true)] out string? result);
}
