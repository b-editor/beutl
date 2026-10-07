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

    public static bool HasSuppressResourceClassGenerationAttribute(INamedTypeSymbol symbol, INamedTypeSymbol suppressAttribute)
    {
        // Check current class
        if (HasAttribute(symbol, suppressAttribute))
        {
            return true;
        }

        // Check base classes
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (HasAttribute(current, suppressAttribute))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute)
        => symbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attribute));
}
