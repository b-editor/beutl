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
    ImmutableArray<SocketPropertyInfo> SocketProperties,
    ImmutableArray<object> OrderedProperties,
    bool IsNodeSubclass);

public readonly record struct ValuePropertyInfo(string Name, ITypeSymbol ValueType, ImmutableArray<AttributeData> Attributes);

public readonly record struct ObjectPropertyInfo(string Name, INamedTypeSymbol ValueType, ImmutableArray<AttributeData> Attributes);

public readonly record struct ListPropertyInfo(string Name, INamedTypeSymbol ElementType, ImmutableArray<AttributeData> Attributes);

public enum SocketKind
{
    Input,
    Output,
    Item
}

public readonly record struct SocketPropertyInfo(string Name, ITypeSymbol ValueType, SocketKind Kind);
