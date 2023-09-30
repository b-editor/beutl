using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
