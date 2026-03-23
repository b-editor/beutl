using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Models;

public readonly record struct ClassInfo(
    INamedTypeSymbol Symbol,
    bool IsPartial,
    INamedTypeSymbol? BaseResourceOwner,
    ImmutableArray<ValuePropertyInfo> ValueProperties,
    ImmutableArray<ObjectPropertyInfo> ObjectProperties,
    ImmutableArray<ListPropertyInfo> ListProperties,
    ImmutableArray<NodePortPropertyInfo> NodePortProperties,
    ImmutableArray<object> OrderedProperties,
    bool IsNodeSubclass,
    bool SuppressedResourceGeneration)
{
    // 生成するものがあるかどうか
    public bool ShouldGenerate()
    {
        return !SuppressedResourceGeneration
            || ValueProperties.Any(p => p.Attributes.Length > 0 || !p.ExcludeFromResource)
            || ObjectProperties.Any(p => p.Attributes.Length > 0 || !p.ExcludeFromResource)
            || ListProperties.Any(p => p.Attributes.Length > 0 || !p.ExcludeFromResource)
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
