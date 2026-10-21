using System.Collections.Concurrent;
using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

// Reports EngineObject constructors that do not call ScanProperties<Self>(): the type still renders,
// but its properties are not saved, edited or recorded in history (36ff0d677).
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScanPropertiesCallAnalyzer : DiagnosticAnalyzer
{
    private const string EngineObjectMetadataName = "Beutl.Engine.EngineObject";
    private const string CoreObjectMetadataName = "Beutl.CoreObject";
    private const string PropertyMetadataName = "Beutl.IProperty";
    private const string ScanPropertiesMethodName = "ScanProperties";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.MissingScanProperties);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(EngineObjectMetadataName) is not { } engineObject
                || start.Compilation.GetTypeByMetadataName(CoreObjectMetadataName) is not { } coreObject
                || start.Compilation.GetTypeByMetadataName(PropertyMetadataName) is not { } property)
            {
                return;
            }

            start.RegisterSymbolStartAction(
                symbolStart => AnalyzeType(symbolStart, engineObject, coreObject, property),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(
        SymbolStartAnalysisContext context, INamedTypeSymbol engineObject, INamedTypeSymbol coreObject, INamedTypeSymbol property)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false } type
            || !InheritsFrom(type, engineObject)
            || !DeclaresProperty(type, property))
        {
            return;
        }

        var scanning = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        context.RegisterOperationBlockStartAction(block =>
        {
            if (block.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
                return;

            block.RegisterOperationAction(
                invocation =>
                {
                    if (ScansOrChains(((IInvocationOperation)invocation.Operation).TargetMethod, type, coreObject))
                        scanning.TryAdd(constructor, true);
                },
                OperationKind.Invocation);
        });

        context.RegisterSymbolEndAction(end =>
        {
            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (scanning.ContainsKey(constructor))
                    continue;

                // The compiler-declared constructor has no source of its own, so the type is reported.
                Location? location = constructor.IsImplicitlyDeclared
                    ? type.Locations.FirstOrDefault()
                    : constructor.Locations.FirstOrDefault();
                end.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingScanProperties,
                    location,
                    type.Name));
            }
        });
    }

    // A constructor that chains to another one of the same type is checked through that constructor.
    private static bool ScansOrChains(IMethodSymbol target, INamedTypeSymbol type, INamedTypeSymbol coreObject)
    {
        if (target.MethodKind == MethodKind.Constructor)
            return SymbolEqualityComparer.Default.Equals(target.ContainingType, type);

        return target is { Name: ScanPropertiesMethodName, TypeArguments.Length: 1 }
            && SymbolEqualityComparer.Default.Equals(target.ContainingType, coreObject)
            && SymbolEqualityComparer.Default.Equals(target.TypeArguments[0], type);
    }

    // Matches what CoreObject.ScanPropertiesCore registers: public instance properties typed as IProperty.
    private static bool DeclaresProperty(INamedTypeSymbol type, INamedTypeSymbol property)
    {
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IPropertySymbol
                {
                    IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public
                } candidate
                && (SymbolEqualityComparer.Default.Equals(candidate.Type, property)
                    || candidate.Type.AllInterfaces.Contains(property, SymbolEqualityComparer.Default)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
                return true;
        }

        return false;
    }
}
