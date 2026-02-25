using Microsoft.CodeAnalysis;

namespace Beutl.Engine.SourceGenerators.Analysis;

public static class TypeAnalysisHelpers
{
    public static bool InheritsFrom(INamedTypeSymbol symbol, INamedTypeSymbol baseSymbol)
    {
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseSymbol))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsEngineObjectType(ITypeSymbol type, INamedTypeSymbol engineObjectSymbol)
    {
        if (type is INamedTypeSymbol named)
        {
            if (SymbolEqualityComparer.Default.Equals(named, engineObjectSymbol))
            {
                return true;
            }

            return InheritsFrom(named, engineObjectSymbol);
        }

        return false;
    }

    public static bool IsListLike(INamedTypeSymbol type)
    {
        if (type.Name.EndsWith("List", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (INamedTypeSymbol interfaceType in type.AllInterfaces)
        {
            if (interfaceType.Name.EndsWith("List", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetListElementType(INamedTypeSymbol type, INamedTypeSymbol engineObjectSymbol, out INamedTypeSymbol? elementType)
    {
        elementType = null;

        // Check if the type directly has a generic argument
        if (IsListLike(type) && type.TypeArguments.Length == 1 && type.TypeArguments[0] is INamedTypeSymbol directElementType)
        {
            if (IsEngineObjectType(directElementType, engineObjectSymbol))
            {
                elementType = directElementType;
                return true;
            }
        }

        // Search in base classes
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (IsListLike(current) && current.TypeArguments.Length == 1 && current.TypeArguments[0] is INamedTypeSymbol baseElementType)
            {
                if (IsEngineObjectType(baseElementType, engineObjectSymbol))
                {
                    elementType = baseElementType;
                    return true;
                }
            }
        }

        // Search in interfaces
        foreach (INamedTypeSymbol interfaceType in type.AllInterfaces)
        {
            if (IsListLike(interfaceType) && interfaceType.TypeArguments.Length == 1 && interfaceType.TypeArguments[0] is INamedTypeSymbol interfaceElementType)
            {
                if (IsEngineObjectType(interfaceElementType, engineObjectSymbol))
                {
                    elementType = interfaceElementType;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool HasSuppressResourceClassGenerationAttribute(INamedTypeSymbol symbol, INamedTypeSymbol suppressAttribute)
    {
        // Check current class
        if (symbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, suppressAttribute)))
        {
            return true;
        }

        // Check base classes
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, suppressAttribute)))
            {
                return true;
            }
        }

        return false;
    }
}
