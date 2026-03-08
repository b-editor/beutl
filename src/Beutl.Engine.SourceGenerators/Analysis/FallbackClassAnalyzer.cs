using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analysis;

public readonly record struct FallbackClassInfo(
    INamedTypeSymbol Symbol,
    bool IsPartial,
    ImmutableArray<AbstractMethodInfo> AbstractMethods,
    ImmutableArray<AbstractMethodInfo> ResourceAbstractMethods);

public readonly record struct AbstractMethodInfo(
    IMethodSymbol Method,
    Accessibility DeclaredAccessibility);

public static class FallbackClassAnalyzer
{
    public static FallbackClassInfo? TryExtract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // Check if the class implements IFallback
        INamedTypeSymbol? iFallbackSymbol = context.SemanticModel.Compilation
            .GetTypeByMetadataName("Beutl.Serialization.IFallback");

        if (iFallbackSymbol is null)
        {
            return null;
        }

        bool implementsIFallback = symbol.AllInterfaces
            .Any(iface => SymbolEqualityComparer.Default.Equals(iface, iFallbackSymbol));

        if (!implementsIFallback)
        {
            return null;
        }

        bool isPartial = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        // Collect unimplemented abstract methods from the base class hierarchy
        var abstractMethods = CollectUnimplementedAbstractMethods(symbol);

        // Collect abstract methods from the base class's Resource nested type
        var resourceAbstractMethods = CollectResourceAbstractMethods(symbol);

        return new FallbackClassInfo(symbol, isPartial, abstractMethods, resourceAbstractMethods);
    }

    private static ImmutableArray<AbstractMethodInfo> CollectUnimplementedAbstractMethods(INamedTypeSymbol symbol)
    {
        var result = ImmutableArray.CreateBuilder<AbstractMethodInfo>();

        // Collect all abstract methods from base classes
        var abstractMethods = new Dictionary<string, IMethodSymbol>();
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            foreach (IMethodSymbol method in current.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m is { IsAbstract: true, MethodKind: MethodKind.Ordinary, IsStatic: false }))
            {
                string key = GetMethodSignatureKey(method);
                if (!abstractMethods.ContainsKey(key))
                {
                    abstractMethods[key] = method;
                }
            }
        }

        // Remove methods already overridden in the user's partial declaration
        foreach (string key in symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsOverride: true, MethodKind: MethodKind.Ordinary })
            .Select(GetMethodSignatureKey))
        {
            abstractMethods.Remove(key);
        }

        foreach (var kvp in abstractMethods)
        {
            result.Add(new AbstractMethodInfo(kvp.Value, kvp.Value.DeclaredAccessibility));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<AbstractMethodInfo> CollectResourceAbstractMethods(INamedTypeSymbol symbol)
    {
        var result = ImmutableArray.CreateBuilder<AbstractMethodInfo>();

        // Find the Resource nested type in the base class hierarchy
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            foreach (INamedTypeSymbol nestedType in current.GetTypeMembers("Resource"))
            {
                foreach (IMethodSymbol method in nestedType.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m is { IsAbstract: true, MethodKind: MethodKind.Ordinary, IsStatic: false }))
                {
                    result.Add(new AbstractMethodInfo(method, method.DeclaredAccessibility));
                }
            }
        }

        // Remove methods already overridden in the user's Resource partial declaration
        foreach (INamedTypeSymbol nestedType in symbol.GetTypeMembers("Resource"))
        {
            foreach (string key in nestedType.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => m is { IsOverride: true, MethodKind: MethodKind.Ordinary })
                .Select(GetMethodSignatureKey))
            {
                result.RemoveAll(info => GetMethodSignatureKey(info.Method) == key);
            }
        }

        return result.ToImmutable();
    }

    private static string GetMethodSignatureKey(IMethodSymbol method)
    {
        var parts = new List<string> { method.Name };
        foreach (IParameterSymbol param in method.Parameters)
        {
            parts.Add(param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return string.Join("|", parts);
    }
}
