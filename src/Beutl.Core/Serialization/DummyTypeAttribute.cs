using System.Diagnostics.CodeAnalysis;

namespace Beutl.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = true, AllowMultiple = false)]
public sealed class DummyTypeAttribute : Attribute
{
    public DummyTypeAttribute(Type dummyType)
    {
        DummyType = dummyType;
    }

    public Type DummyType { get; }
}

public interface IDummy : ICoreSerializable
{
    bool TryGetTypeName([NotNullWhen(true)] out string? result);
}
