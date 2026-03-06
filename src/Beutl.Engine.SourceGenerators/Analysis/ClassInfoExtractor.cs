using System.Collections.Immutable;

using Beutl.Engine.SourceGenerators.Models;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analysis;

public static class ClassInfoExtractor
{
    public static ClassInfo? TryExtract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        Compilation compilation = context.SemanticModel.Compilation;
        INamedTypeSymbol? engineObjectSymbol = compilation.GetTypeByMetadataName("Beutl.Engine.EngineObject");
        INamedTypeSymbol? iPropertySymbol = compilation.GetTypeByMetadataName("Beutl.Engine.IProperty`1");
        INamedTypeSymbol? suppressAttribute = compilation.GetTypeByMetadataName("Beutl.Engine.SuppressResourceClassGenerationAttribute");
        if (engineObjectSymbol is null || iPropertySymbol is null || suppressAttribute is null)
        {
            return null;
        }

        // Socket type symbols for Node subclasses
        INamedTypeSymbol? inputSocketSymbol = compilation.GetTypeByMetadataName("Beutl.NodeTree.InputSocket`1");
        INamedTypeSymbol? outputSocketSymbol = compilation.GetTypeByMetadataName("Beutl.NodeTree.OutputSocket`1");
        INamedTypeSymbol? nodeItemGenericSymbol = compilation.GetTypeByMetadataName("Beutl.NodeTree.NodeItem`1");
        INamedTypeSymbol? nodeSymbol = compilation.GetTypeByMetadataName("Beutl.NodeTree.Node");

        if (SymbolEqualityComparer.Default.Equals(symbol, engineObjectSymbol))
        {
            return null;
        }

        if (!TypeAnalysisHelpers.InheritsFrom(symbol, engineObjectSymbol))
        {
            return null;
        }

        if (TypeAnalysisHelpers.HasSuppressResourceClassGenerationAttribute(symbol, suppressAttribute))
        {
            return null;
        }

        bool isPartial = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        var valueProperties = ImmutableArray.CreateBuilder<ValuePropertyInfo>();
        var objectProperties = ImmutableArray.CreateBuilder<ObjectPropertyInfo>();
        var listProperties = ImmutableArray.CreateBuilder<ListPropertyInfo>();

        foreach (ISymbol member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol propertySymbol)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(propertySymbol.ContainingType, symbol))
            {
                continue;
            }

            if (propertySymbol.IsStatic)
            {
                continue;
            }

            if (propertySymbol.Type is not INamedTypeSymbol namedType)
            {
                continue;
            }

            if (propertySymbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, suppressAttribute)))
            {
                continue;
            }

            if (namedType.IsGenericType && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, iPropertySymbol))
            {
                ITypeSymbol valueType = namedType.TypeArguments[0];
                if (TypeAnalysisHelpers.IsEngineObjectType(valueType, engineObjectSymbol) && valueType is INamedTypeSymbol engineObjectType)
                {
                    objectProperties.Add(new ObjectPropertyInfo(propertySymbol.Name, engineObjectType));
                }
                else
                {
                    valueProperties.Add(new ValuePropertyInfo(propertySymbol.Name, valueType));
                }

                continue;
            }

            if (TypeAnalysisHelpers.TryGetListElementType(namedType, engineObjectSymbol, out INamedTypeSymbol? elementType))
            {
                listProperties.Add(new ListPropertyInfo(propertySymbol.Name, elementType!));
            }
        }

        // Socket property detection for Node subclasses
        var socketProperties = ImmutableArray.CreateBuilder<SocketPropertyInfo>();
        bool isNodeSubclass = nodeSymbol != null && TypeAnalysisHelpers.InheritsFrom(symbol, nodeSymbol);

        if (isNodeSubclass && inputSocketSymbol != null && outputSocketSymbol != null && nodeItemGenericSymbol != null)
        {
            foreach (ISymbol member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol propertySymbol)
                    continue;
                if (!SymbolEqualityComparer.Default.Equals(propertySymbol.ContainingType, symbol))
                    continue;
                if (propertySymbol.IsStatic)
                    continue;
                if (propertySymbol.Type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
                    continue;
                if (propertySymbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, suppressAttribute)))
                    continue;

                // Skip if name conflicts with IProperty-based properties
                if (valueProperties.Any(v => v.Name == propertySymbol.Name)
                    || objectProperties.Any(o => o.Name == propertySymbol.Name)
                    || listProperties.Any(l => l.Name == propertySymbol.Name))
                    continue;

                INamedTypeSymbol constructedFrom = namedType.ConstructedFrom;
                SocketKind? kind = null;

                if (SymbolEqualityComparer.Default.Equals(constructedFrom, inputSocketSymbol))
                    kind = SocketKind.Input;
                else if (SymbolEqualityComparer.Default.Equals(constructedFrom, outputSocketSymbol))
                    kind = SocketKind.Output;
                else if (SymbolEqualityComparer.Default.Equals(constructedFrom, nodeItemGenericSymbol))
                    kind = SocketKind.Item;

                if (kind.HasValue)
                {
                    ITypeSymbol valueType = namedType.TypeArguments[0];
                    socketProperties.Add(new SocketPropertyInfo(propertySymbol.Name, valueType, kind.Value));
                }
            }
        }

        INamedTypeSymbol? baseResourceOwner = null;
        if (symbol.BaseType is INamedTypeSymbol baseType
            && TypeAnalysisHelpers.InheritsFrom(baseType, engineObjectSymbol))
        {
            baseResourceOwner = baseType;
        }

        return new ClassInfo(
            symbol,
            isPartial,
            baseResourceOwner,
            valueProperties.ToImmutable(),
            objectProperties.ToImmutable(),
            listProperties.ToImmutable(),
            socketProperties.ToImmutable(),
            isNodeSubclass);
    }
}
