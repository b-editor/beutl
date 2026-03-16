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
        INamedTypeSymbol? iListPropertySymbol = compilation.GetTypeByMetadataName("Beutl.Engine.IListProperty`1");
        INamedTypeSymbol? suppressAttribute = compilation.GetTypeByMetadataName("Beutl.Engine.SuppressResourceClassGenerationAttribute");
        if (engineObjectSymbol is null || iPropertySymbol is null || iListPropertySymbol is null || suppressAttribute is null)
        {
            return null;
        }

        // NodePort type symbols for GraphNode subclasses
        INamedTypeSymbol? inputNodePortSymbol = compilation.GetTypeByMetadataName("Beutl.NodeGraph.InputPort`1");
        INamedTypeSymbol? outputNodePortSymbol = compilation.GetTypeByMetadataName("Beutl.NodeGraph.OutputPort`1");
        INamedTypeSymbol? nodeMemberGenericSymbol = compilation.GetTypeByMetadataName("Beutl.NodeGraph.NodeMember`1");
        INamedTypeSymbol? nodeSymbol = compilation.GetTypeByMetadataName("Beutl.NodeGraph.GraphNode");

        if (SymbolEqualityComparer.Default.Equals(symbol, engineObjectSymbol))
        {
            return null;
        }

        if (!TypeAnalysisHelpers.InheritsFrom(symbol, engineObjectSymbol))
        {
            return null;
        }

        bool suppressedResourceClassGeneration = TypeAnalysisHelpers.HasSuppressResourceClassGenerationAttribute(symbol, suppressAttribute);
        bool isPartial = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        var valueProperties = ImmutableArray.CreateBuilder<ValuePropertyInfo>();
        var objectProperties = ImmutableArray.CreateBuilder<ObjectPropertyInfo>();
        var listProperties = ImmutableArray.CreateBuilder<ListPropertyInfo>();
        var orderedProperties = ImmutableArray.CreateBuilder<object>();

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

            var excludeResource = propertySymbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, suppressAttribute));

            if (namedType.IsGenericType && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, iPropertySymbol))
            {
                ITypeSymbol valueType = namedType.TypeArguments[0];
                ImmutableArray<AttributeData> propAttrs = propertySymbol.GetAttributes();
                if (TypeAnalysisHelpers.IsEngineObjectType(valueType, engineObjectSymbol) && valueType is INamedTypeSymbol engineObjectType)
                {
                    var propInfo = new ObjectPropertyInfo(propertySymbol.Name, engineObjectType, propAttrs, excludeResource);
                    objectProperties.Add(propInfo);
                    orderedProperties.Add(propInfo);
                }
                else
                {
                    var propInfo = new ValuePropertyInfo(propertySymbol.Name, valueType, propAttrs, excludeResource);
                    valueProperties.Add(propInfo);
                    orderedProperties.Add(propInfo);
                }

                continue;
            }

            if (namedType.IsGenericType && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, iListPropertySymbol))
            {
                ITypeSymbol elementType = namedType.TypeArguments[0];
                if (!TypeAnalysisHelpers.IsEngineObjectType(elementType, engineObjectSymbol)) continue;

                ImmutableArray<AttributeData> listAttrs = propertySymbol.GetAttributes();
                var propInfo = new ListPropertyInfo(propertySymbol.Name, elementType, listAttrs, excludeResource);
                listProperties.Add(propInfo);
                orderedProperties.Add(propInfo);
            }
        }

        // NodePort property detection for GraphNode subclasses
        var portProperties = ImmutableArray.CreateBuilder<NodePortPropertyInfo>();
        bool isNodeSubclass = nodeSymbol != null && TypeAnalysisHelpers.InheritsFrom(symbol, nodeSymbol);

        if (isNodeSubclass && inputNodePortSymbol != null && outputNodePortSymbol != null && nodeMemberGenericSymbol != null)
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
                NodePortKind? kind = null;

                if (SymbolEqualityComparer.Default.Equals(constructedFrom, inputNodePortSymbol))
                    kind = NodePortKind.Input;
                else if (SymbolEqualityComparer.Default.Equals(constructedFrom, outputNodePortSymbol))
                    kind = NodePortKind.Output;
                else if (SymbolEqualityComparer.Default.Equals(constructedFrom, nodeMemberGenericSymbol))
                    kind = NodePortKind.Item;

                if (kind.HasValue)
                {
                    ITypeSymbol valueType = namedType.TypeArguments[0];
                    portProperties.Add(new NodePortPropertyInfo(propertySymbol.Name, valueType, kind.Value));
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
            portProperties.ToImmutable(),
            orderedProperties.ToImmutable(),
            isNodeSubclass,
            suppressedResourceClassGeneration);
    }
}
