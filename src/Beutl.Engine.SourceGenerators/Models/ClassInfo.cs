using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Models;

// A CoreObject that is not an EngineObject gets only its property scan, and only when it declares properties.
public readonly record struct ClassInfo(
    INamedTypeSymbol Symbol,
    bool IsPartial,
    bool IsEngineObject,
    INamedTypeSymbol? BaseResourceOwner,
    ImmutableArray<ValuePropertyInfo> ValueProperties,
    ImmutableArray<ObjectPropertyInfo> ObjectProperties,
    ImmutableArray<ListPropertyInfo> ListProperties,
    ImmutableArray<NodePortPropertyInfo> NodePortProperties,
    ImmutableArray<object> OrderedProperties,
    bool SuppressedResourceGeneration)
{
    public bool ShouldGenerate()
    {
        if (!IsEngineObject)
            return !OrderedProperties.IsEmpty;

        return !SuppressedResourceGeneration
            || !OrderedProperties.IsEmpty
            || NodePortProperties.Length > 0;
    }
}

public readonly record struct ValuePropertyInfo(string Name, ITypeSymbol ValueType, ImmutableArray<AttributeData> Attributes, bool ExcludeFromResource);

public readonly record struct ObjectPropertyInfo(string Name, INamedTypeSymbol ValueType, ImmutableArray<AttributeData> Attributes, bool ExcludeFromResource);

public readonly record struct ListPropertyInfo(string Name, ITypeSymbol ElementType, ImmutableArray<AttributeData> Attributes, bool ExcludeFromResource);

public enum NodePortKind
{
    Input,
    Output,
    Item
}

public readonly record struct NodePortPropertyInfo(string Name, ITypeSymbol ValueType, NodePortKind Kind);
