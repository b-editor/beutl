using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    public static bool HasDirectSuppressResourceClassGenerationAttribute(
        INamedTypeSymbol symbol,
        INamedTypeSymbol suppressAttribute)
    {
        return symbol.GetAttributes().Any(
            attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, suppressAttribute));
    }

    public static INamedTypeSymbol? FindInheritedTypedResourceUpdateOwner(
        INamedTypeSymbol symbol,
        INamedTypeSymbol engineObjectSymbol,
        INamedTypeSymbol compositionContextSymbol,
        INamedTypeSymbol suppressAttribute)
    {
        for (INamedTypeSymbol? owner = symbol.BaseType; owner is not null; owner = owner.BaseType)
        {
            ImmutableArray<INamedTypeSymbol> resourceTypes = owner.GetTypeMembers("Resource", 0);
            if (resourceTypes.IsEmpty)
                continue;

            if (resourceTypes.All(resourceType =>
                    IsPendingGeneratedResourcePart(owner, resourceType, suppressAttribute)))
            {
                // A user-authored partial extension of a Resource generated in this same compilation has no
                // generated base or hooks in the input symbol yet. Its owner will receive those members from this
                // generator, so continue to the handwritten typed ancestor that defines the contract.
                continue;
            }

            foreach (INamedTypeSymbol resourceType in resourceTypes)
            {
                if (resourceType.IsSealed || !IsAccessibleFromDerivedType(resourceType.DeclaredAccessibility))
                    continue;

                IMethodSymbol? update = FindMethod(
                    resourceType,
                    "Update",
                    method => method.ReturnsVoid
                        && method.Parameters.Length == 3
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, engineObjectSymbol)
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, compositionContextSymbol)
                        && method.Parameters[2] is { RefKind: RefKind.Ref, Type.SpecialType: SpecialType.System_Boolean });
                if (update is not { IsSealed: true })
                    continue;

                IMethodSymbol? compatibility = FindMethod(
                    resourceType,
                    "IsCompatibleUpdateOwner",
                    method => method.ReturnType.SpecialType == SpecialType.System_Boolean
                        && method.Parameters.Length == 1
                        && method.Parameters[0].RefKind == RefKind.None
                        && method.Parameters[0].Type is INamedTypeSymbol
                        && IsOverridableFromDerivedType(method));
                if (compatibility?.Parameters[0].Type is not INamedTypeSymbol typedOwner
                    || (!SymbolEqualityComparer.Default.Equals(symbol, typedOwner)
                        && !InheritsFrom(symbol, typedOwner)))
                {
                    continue;
                }

                IMethodSymbol? updateCore = FindMethod(
                    resourceType,
                    "UpdateCore",
                    method => method.ReturnsVoid
                        && method.Parameters.Length == 3
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, typedOwner)
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, compositionContextSymbol)
                        && method.Parameters[2] is { RefKind: RefKind.Ref, Type.SpecialType: SpecialType.System_Boolean }
                        && IsOverridableFromDerivedType(method));
                if (updateCore != null)
                    return typedOwner;
            }

            // C# nested-type lookup stops at the nearest declaration. A Resource that shadows the typed
            // ancestor contract cannot be bypassed because the emitter also inherits owner.Resource.
            return null;
        }

        return null;
    }

    private static bool IsPendingGeneratedResourcePart(
        INamedTypeSymbol owner,
        INamedTypeSymbol resourceType,
        INamedTypeSymbol suppressAttribute)
    {
        if (resourceType.IsSealed
            || HasDirectSuppressResourceClassGenerationAttribute(owner, suppressAttribute)
            || !IsPartialSourceType(owner)
            || resourceType.BaseType?.SpecialType != SpecialType.System_Object)
        {
            return false;
        }

        ImmutableArray<SyntaxReference> declarations = resourceType.DeclaringSyntaxReferences;
        if (declarations.IsEmpty)
            return false;

        foreach (SyntaxReference declaration in declarations)
        {
            if (declaration.GetSyntax() is not ClassDeclarationSyntax
                {
                    BaseList: null,
                    Modifiers: var modifiers,
                }
                || !modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPartialSourceType(INamedTypeSymbol type)
    {
        ImmutableArray<SyntaxReference> declarations = type.DeclaringSyntaxReferences;
        if (declarations.IsEmpty)
            return false;

        foreach (SyntaxReference declaration in declarations)
        {
            if (declaration.GetSyntax() is not ClassDeclarationSyntax { Modifiers: var modifiers }
                || !modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return false;
            }
        }

        return true;
    }

    private static IMethodSymbol? FindMethod(
        INamedTypeSymbol type,
        string name,
        Func<IMethodSymbol, bool> predicate)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol method in current.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (predicate(method))
                    return method;
            }
        }

        return null;
    }

    private static bool IsOverridableFromDerivedType(IMethodSymbol method)
    {
        return !method.IsStatic
            && !method.IsSealed
            && (method.IsAbstract || method.IsVirtual || method.IsOverride)
            && IsAccessibleFromDerivedType(method.DeclaredAccessibility);
    }

    private static bool IsAccessibleFromDerivedType(Accessibility accessibility)
    {
        return accessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;
    }
}
