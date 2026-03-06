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
    bool IsNodeSubclass);

public readonly record struct ValuePropertyInfo(string Name, ITypeSymbol ValueType);

public readonly record struct ObjectPropertyInfo(string Name, INamedTypeSymbol ValueType);

public readonly record struct ListPropertyInfo(string Name, INamedTypeSymbol ElementType);

public enum SocketKind
{
    Input,
    Output,
    Item
}

public readonly record struct SocketPropertyInfo(string Name, ITypeSymbol ValueType, SocketKind Kind);
