using System.Collections.Immutable;
using System.Text;

using Beutl.Engine.SourceGenerators.Analysis;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Beutl.Engine.SourceGenerators.Emit;
using Beutl.Engine.SourceGenerators.Models;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators;

[Generator]
public sealed class EngineObjectResourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntaxContext, cancellationToken) => ClassInfoExtractor.TryExtract(syntaxContext, cancellationToken))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!.Value);

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (productionContext, pair) =>
            Execute(productionContext, pair.Left, pair.Right));
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassInfo> classes)
    {
        if (classes.IsDefaultOrEmpty)
        {
            return;
        }

        var processed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        INamedTypeSymbol? defaultsProviderAttribute = compilation.GetTypeByMetadataName(
            "Beutl.Engine.ResourceDefaultValuesProviderAttribute");

        foreach (ClassInfo info in classes)
        {
            if (!processed.Add(info.Symbol))
            {
                continue;
            }

            if (!info.ShouldGenerate()) continue;

            if (!info.IsPartial)
            {
                // Class opted out via [SuppressResourceClassGeneration]; fall back to the
                // reflection-based base implementation instead of warning about partial.
                if (info.SuppressedResourceGeneration) continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingPartial,
                    info.Symbol.Locations.FirstOrDefault(),
                    info.Symbol.ToDisplayString()));
                continue;
            }

            IMethodSymbol? defaultsProvider = null;
            if (!info.SuppressedResourceGeneration
                && (!TryResolveResourceDefaultsProvider(
                        context,
                        info,
                        defaultsProviderAttribute,
                        out defaultsProvider)
                    || ReportMissingDerivedResourceDefaultsProvider(
                        context,
                        info,
                        defaultsProviderAttribute,
                        defaultsProvider)
                    || (defaultsProvider is null
                        && (ReportPrimaryConstructor(context, info)
                            || ReportInvalidResourcePropertyDeclarations(context, compilation, info)))))
            {
                continue;
            }

            string source = GenerateSource(info, defaultsProvider?.Name);
            string hintName = EmitHelpers.GetHintName(info.Symbol);
            context.AddSource(hintName, source);
        }
    }

    private static bool TryResolveResourceDefaultsProvider(
        SourceProductionContext context,
        ClassInfo info,
        INamedTypeSymbol? defaultsProviderAttribute,
        out IMethodSymbol? provider)
    {
        provider = null;
        if (defaultsProviderAttribute is null)
        {
            return true;
        }

        IMethodSymbol[] candidates = info.Symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => HasAttribute(method, defaultsProviderAttribute))
            .ToArray();
        if (candidates.Length == 0)
        {
            return true;
        }

        IMethodSymbol? candidate = candidates.Length == 1 ? candidates[0] : null;
        if (candidate is null
            || !candidate.IsStatic
            || candidate.MethodKind != MethodKind.Ordinary
            || candidate.Parameters.Length != 0
            || candidate.TypeParameters.Length != 0
            || !SymbolEqualityComparer.Default.Equals(candidate.ReturnType, info.Symbol)
            || candidate.ReturnNullableAnnotation == NullableAnnotation.Annotated)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ResourceDefaultValuesProviderInvalid,
                candidate?.Locations.FirstOrDefault() ?? info.Symbol.Locations.FirstOrDefault(),
                info.Symbol.ToDisplayString()));
            return false;
        }

        provider = candidate;
        return true;
    }

    private static bool ReportMissingDerivedResourceDefaultsProvider(
        SourceProductionContext context,
        ClassInfo info,
        INamedTypeSymbol? defaultsProviderAttribute,
        IMethodSymbol? defaultsProvider)
    {
        if (defaultsProvider is not null
            || !HasResourceDefaultsProviderInBaseType(info.Symbol, defaultsProviderAttribute))
        {
            return false;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ResourceDefaultValuesProviderRequiredOnDerivedType,
            info.Symbol.Locations.FirstOrDefault(),
            info.Symbol.ToDisplayString()));
        return true;
    }

    private static bool HasResourceDefaultsProviderInBaseType(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? defaultsProviderAttribute)
    {
        if (defaultsProviderAttribute is null)
        {
            return false;
        }

        for (INamedTypeSymbol? current = symbol.BaseType;
             current is not null;
             current = current.BaseType)
        {
            if (current.GetMembers()
                .OfType<IMethodSymbol>()
                .Any(method => HasAttribute(method, defaultsProviderAttribute)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute)
        => symbol.GetAttributes().Any(item =>
            SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute));

    private static bool ReportPrimaryConstructor(
        SourceProductionContext context,
        ClassInfo info)
    {
        if (info.SuppressedResourceGeneration)
        {
            return false;
        }

        ClassDeclarationSyntax? declaration = info.Symbol.DeclaringSyntaxReferences
            .Select(static syntaxReference => syntaxReference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(static classDeclaration => classDeclaration.ParameterList is not null);
        if (declaration?.ParameterList is null)
        {
            return false;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ResourcePrimaryConstructorNotSupported,
            declaration.ParameterList.GetLocation(),
            info.Symbol.ToDisplayString()));
        return true;
    }

    private static bool ReportInvalidResourcePropertyDeclarations(
        SourceProductionContext context,
        Compilation compilation,
        ClassInfo info)
    {
        if (info.SuppressedResourceGeneration)
        {
            return false;
        }

        var requiredNames = new HashSet<string>(
            info.ValueProperties
                .Where(static property => !property.ExcludeFromResource)
                .Select(static property => property.Name)
                .Concat(info.ObjectProperties
                    .Where(static property => !property.ExcludeFromResource)
                    .Select(static property => property.Name)),
            StringComparer.Ordinal);
        bool reported = false;
        foreach (IPropertySymbol property in info.Symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (!requiredNames.Contains(property.Name))
            {
                continue;
            }

            bool hasDeclarationTimeStorage = TryGetDeclarationTimeStorage(
                compilation,
                property,
                out ISymbol? storage);
            Location? constructorAssignment = storage is null
                ? null
                : FindInstanceConstructorAssignment(compilation, info.Symbol, storage);
            if (hasDeclarationTimeStorage && constructorAssignment is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ResourcePropertyMissingInitializer,
                constructorAssignment ?? property.Locations.FirstOrDefault(),
                info.Symbol.ToDisplayString(),
                property.Name));
            reported = true;
        }

        INamedTypeSymbol? iPropertyDefinition = compilation.GetTypeByMetadataName("Beutl.Engine.IProperty`1");
        INamedTypeSymbol? suppressAttribute = compilation.GetTypeByMetadataName(
            "Beutl.Engine.SuppressResourceClassGenerationAttribute");
        if (iPropertyDefinition is not null && suppressAttribute is not null)
        {
            for (INamedTypeSymbol? current = info.Symbol.BaseType;
                 current is not null;
                 current = current.BaseType)
            {
                foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.IsStatic
                        || property.Type is not INamedTypeSymbol { IsGenericType: true } propertyType
                        || !SymbolEqualityComparer.Default.Equals(
                            propertyType.ConstructedFrom,
                            iPropertyDefinition)
                        || HasAttribute(property, suppressAttribute))
                    {
                        continue;
                    }

                    Location? constructorAssignment = FindInstanceConstructorAssignment(
                        compilation,
                        info.Symbol,
                        property);
                    if (constructorAssignment is null)
                    {
                        continue;
                    }

                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ResourcePropertyMissingInitializer,
                        constructorAssignment,
                        info.Symbol.ToDisplayString(),
                        property.Name));
                    reported = true;
                }
            }
        }

        return reported;
    }

    private static bool TryGetDeclarationTimeStorage(
        Compilation compilation,
        IPropertySymbol property,
        out ISymbol? storage)
    {
        foreach (SyntaxReference syntaxReference in property.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.Initializer is not null)
            {
                storage = property;
                return true;
            }

            ExpressionSyntax? getterExpression = GetDirectGetterExpression(declaration);
            if (getterExpression is null)
            {
                continue;
            }

            IOperation? operation = compilation.GetSemanticModel(declaration.SyntaxTree)
                .GetOperation(getterExpression);
            while (operation is IConversionOperation conversion)
            {
                operation = conversion.Operand;
            }
            while (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }

            if (operation is not IFieldReferenceOperation
                {
                    Field: { IsReadOnly: true, IsStatic: false } field,
                    Instance: IInstanceReferenceOperation
                    {
                        ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                    },
                }
                || !field.DeclaringSyntaxReferences.Any(static fieldReference =>
                    fieldReference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null }))
            {
                continue;
            }

            storage = field;
            return true;
        }

        storage = null;
        return false;
    }

    private static ExpressionSyntax? GetDirectGetterExpression(PropertyDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody is not null)
        {
            return declaration.ExpressionBody.Expression;
        }

        AccessorDeclarationSyntax? getter = declaration.AccessorList?.Accessors
            .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (getter?.ExpressionBody is not null)
        {
            return getter.ExpressionBody.Expression;
        }

        return getter?.Body?.Statements.Count == 1
               && getter.Body.Statements[0] is ReturnStatementSyntax { Expression: { } expression }
            ? expression
            : null;
    }

    private static Location? FindInstanceConstructorAssignment(
        Compilation compilation,
        INamedTypeSymbol containingType,
        ISymbol storage)
    {
        foreach (SyntaxReference syntaxReference in containingType.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not ClassDeclarationSyntax declaration)
            {
                continue;
            }

            SemanticModel semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (ConstructorDeclarationSyntax constructor in declaration.Members
                         .OfType<ConstructorDeclarationSyntax>())
            {
                foreach (AssignmentExpressionSyntax assignment in constructor
                             .DescendantNodes(static node =>
                                 node is not AnonymousFunctionExpressionSyntax
                                     and not LocalFunctionStatementSyntax)
                             .OfType<AssignmentExpressionSyntax>())
                {
                    if (semanticModel.GetOperation(assignment) is not IAssignmentOperation operation)
                    {
                        continue;
                    }

                    if (ContainsAssignedStorage(operation.Target, storage))
                    {
                        return assignment.GetLocation();
                    }
                }
            }
        }

        return null;
    }

    private static bool ContainsAssignedStorage(IOperation target, ISymbol storage)
    {
        return target switch
        {
            IConversionOperation conversion => ContainsAssignedStorage(conversion.Operand, storage),
            IParenthesizedOperation parenthesized => ContainsAssignedStorage(parenthesized.Operand, storage),
            ITupleOperation tuple => tuple.Elements.Any(element => ContainsAssignedStorage(element, storage)),
            IPropertyReferenceOperation
            {
                Instance: IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                },
            } propertyReference => SymbolEqualityComparer.Default.Equals(propertyReference.Property, storage),
            IFieldReferenceOperation
            {
                Instance: IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                },
            } fieldReference => SymbolEqualityComparer.Default.Equals(fieldReference.Field, storage),
            _ => false,
        };
    }

    private static string GenerateSource(ClassInfo info, string? defaultsProviderMethod)
    {
        INamedTypeSymbol symbol = info.Symbol;
        string? namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8631, CS8618, CS9264");
        sb.AppendLine();

        if (namespaceName is not null)
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        string accessibility = EmitHelpers.GetAccessibility(symbol.DeclaredAccessibility);
        string typeParameterList = EmitHelpers.GetTypeParameterList(symbol);
        string constraintClauses = EmitHelpers.GetTypeConstraintClauses(symbol, string.Empty);

        sb.AppendLine($"{accessibility} partial class {symbol.Name}{typeParameterList}");
        if (!string.IsNullOrEmpty(constraintClauses))
        {
            sb.Append(constraintClauses);
        }
        sb.AppendLine("{");

        string indent = "    ";
        string currentTypeDisplay = symbol.ToDisplayString(EmitHelpers.TypeDisplayFormat);

        ResourceDefaultValuesEmitter.Emit(
            sb,
            indent,
            currentTypeDisplay,
            info,
            defaultsProviderMethod);
        sb.AppendLine();
        ToResourceMethodEmitter.Emit(sb, indent, currentTypeDisplay, info);
        sb.AppendLine();
        ScanPropertiesCoreEmitter.Emit(sb, indent, info);
        sb.AppendLine();
        ResourceClassEmitter.Emit(sb, indent, currentTypeDisplay, info);

        sb.AppendLine("}");

        return sb.ToString();
    }
}
