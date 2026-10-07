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

        if (KnownTypes.Resolve(context.SemanticModel.Compilation) is not { } types)
        {
            return null;
        }

        if (SymbolEqualityComparer.Default.Equals(symbol, types.EngineObject))
        {
            return null;
        }

        if (!TypeAnalysisHelpers.InheritsFrom(symbol, types.EngineObject))
        {
            return null;
        }

        bool suppressedResourceClassGeneration = TypeAnalysisHelpers.HasSuppressResourceClassGenerationAttribute(symbol, types.SuppressAttribute);
        bool isPartial = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        var valueProperties = ImmutableArray.CreateBuilder<ValuePropertyInfo>();
        var objectProperties = ImmutableArray.CreateBuilder<ObjectPropertyInfo>();
        var listProperties = ImmutableArray.CreateBuilder<ListPropertyInfo>();
        var orderedProperties = ImmutableArray.CreateBuilder<object>();
        CollectResourceProperties(symbol, types, valueProperties, objectProperties, listProperties, orderedProperties);

        // NodePort property detection for GraphNode subclasses
        var portProperties = ImmutableArray.CreateBuilder<NodePortPropertyInfo>();
        bool isNodeSubclass = types.Node != null && TypeAnalysisHelpers.InheritsFrom(symbol, types.Node);

        if (isNodeSubclass && types.InputPort != null && types.OutputPort != null && types.NodeMember != null)
        {
            CollectNodePortProperties(symbol, types, valueProperties, objectProperties, listProperties, portProperties);
        }

        INamedTypeSymbol? baseResourceOwner = null;
        if (symbol.BaseType is INamedTypeSymbol baseType
            && TypeAnalysisHelpers.InheritsFrom(baseType, types.EngineObject))
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

    private static void CollectResourceProperties(
        INamedTypeSymbol symbol,
        KnownTypes types,
        ImmutableArray<ValuePropertyInfo>.Builder valueProperties,
        ImmutableArray<ObjectPropertyInfo>.Builder objectProperties,
        ImmutableArray<ListPropertyInfo>.Builder listProperties,
        ImmutableArray<object>.Builder orderedProperties)
    {
        foreach (ISymbol member in symbol.GetMembers())
        {
            if (!TryGetOwnInstanceProperty(member, symbol, out IPropertySymbol propertySymbol, out INamedTypeSymbol namedType))
            {
                continue;
            }

            var excludeResource = TypeAnalysisHelpers.HasAttribute(propertySymbol, types.SuppressAttribute);

            if (namedType.IsGenericType && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, types.IProperty))
            {
                ITypeSymbol valueType = namedType.TypeArguments[0];
                ImmutableArray<AttributeData> propAttrs = propertySymbol.GetAttributes();
                if (TypeAnalysisHelpers.IsEngineObjectType(valueType, types.EngineObject) && valueType is INamedTypeSymbol engineObjectType)
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

            if (namedType.IsGenericType && SymbolEqualityComparer.Default.Equals(namedType.ConstructedFrom, types.IListProperty))
            {
                ITypeSymbol elementType = namedType.TypeArguments[0];
                if (!TypeAnalysisHelpers.IsEngineObjectType(elementType, types.EngineObject)) continue;

                ImmutableArray<AttributeData> listAttrs = propertySymbol.GetAttributes();
                var propInfo = new ListPropertyInfo(propertySymbol.Name, elementType, listAttrs, excludeResource);
                listProperties.Add(propInfo);
                orderedProperties.Add(propInfo);
            }
        }
    }

    private static void CollectNodePortProperties(
        INamedTypeSymbol symbol,
        KnownTypes types,
        ImmutableArray<ValuePropertyInfo>.Builder valueProperties,
        ImmutableArray<ObjectPropertyInfo>.Builder objectProperties,
        ImmutableArray<ListPropertyInfo>.Builder listProperties,
        ImmutableArray<NodePortPropertyInfo>.Builder portProperties)
    {
        foreach (ISymbol member in symbol.GetMembers())
        {
            if (!TryGetOwnInstanceProperty(member, symbol, out IPropertySymbol propertySymbol, out INamedTypeSymbol namedType)
                || !namedType.IsGenericType)
                continue;
            if (TypeAnalysisHelpers.HasAttribute(propertySymbol, types.SuppressAttribute))
                continue;

            // Skip if name conflicts with IProperty-based properties
            if (valueProperties.Any(v => v.Name == propertySymbol.Name)
                || objectProperties.Any(o => o.Name == propertySymbol.Name)
                || listProperties.Any(l => l.Name == propertySymbol.Name))
                continue;

            NodePortKind? kind = GetNodePortKind(namedType.ConstructedFrom, types);

            if (kind.HasValue)
            {
                ITypeSymbol valueType = namedType.TypeArguments[0];
                portProperties.Add(new NodePortPropertyInfo(propertySymbol.Name, valueType, kind.Value));
            }
        }
    }

    private static NodePortKind? GetNodePortKind(INamedTypeSymbol constructedFrom, KnownTypes types)
    {
        if (SymbolEqualityComparer.Default.Equals(constructedFrom, types.InputPort))
            return NodePortKind.Input;
        if (SymbolEqualityComparer.Default.Equals(constructedFrom, types.OutputPort))
            return NodePortKind.Output;
        if (SymbolEqualityComparer.Default.Equals(constructedFrom, types.NodeMember))
            return NodePortKind.Item;

        return null;
    }

    private static bool TryGetOwnInstanceProperty(
        ISymbol member, INamedTypeSymbol owner, out IPropertySymbol propertySymbol, out INamedTypeSymbol namedType)
    {
        propertySymbol = null!;
        namedType = null!;

        if (member is not IPropertySymbol property)
        {
            return false;
        }

        if (!SymbolEqualityComparer.Default.Equals(property.ContainingType, owner))
        {
            return false;
        }

        if (property.IsStatic)
        {
            return false;
        }

        if (property.Type is not INamedTypeSymbol type)
        {
            return false;
        }

        propertySymbol = property;
        namedType = type;
        return true;
    }

    private readonly record struct KnownTypes(
        INamedTypeSymbol EngineObject,
        INamedTypeSymbol IProperty,
        INamedTypeSymbol IListProperty,
        INamedTypeSymbol SuppressAttribute,
        INamedTypeSymbol? InputPort,
        INamedTypeSymbol? OutputPort,
        INamedTypeSymbol? NodeMember,
        INamedTypeSymbol? Node)
    {
        public static KnownTypes? Resolve(Compilation compilation)
        {
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

            return new KnownTypes(
                engineObjectSymbol,
                iPropertySymbol,
                iListPropertySymbol,
                suppressAttribute,
                inputNodePortSymbol,
                outputNodePortSymbol,
                nodeMemberGenericSymbol,
                nodeSymbol);
        }
    }
}
